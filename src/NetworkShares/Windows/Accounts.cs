using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using static NetworkShares.Native;

namespace NetworkShares;

/// <summary>Local user accounts: listing, creating, deleting, and hiding the app's own from the sign-in screen.</summary>
static class Accounts
{
    public const string Comment = "Created by Network Shares";
    const string SpecialAccounts = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts";
    const string UserList = SpecialAccounts + @"\UserList";

    public sealed record LocalUser(string Name, uint Flags, uint Rid)
    {
        public bool Disabled => (Flags & UF_ACCOUNTDISABLE) != 0;
    }

    public static List<LocalUser> All()
    {
        int resume = 0;
        int r = NetUserEnum(null, 20, FILTER_NORMAL_ACCOUNT, out var buf, MAX_PREFERRED_LENGTH, out int read, out _, ref resume);
        try
        {
            Check(r, "Couldn't list the users");
            var list = new List<LocalUser>(read);
            int size = Marshal.SizeOf<USER_INFO_20>();
            for (int i = 0; i < read; i++)
            {
                var u = Marshal.PtrToStructure<USER_INFO_20>(buf + i * size);
                if (u.name != null) list.Add(new LocalUser(u.name, u.flags, u.user_id));
            }
            return list;
        }
        finally
        {
            if (buf != 0) NetApiBufferFree(buf);
        }
    }

    /// <summary>Enabled accounts someone could sign in with over the network.</summary>
    public static List<string> Selectable()
    {
        try
        {
            return All().Where(u => !u.Disabled).Select(u => u.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            return [];
        }
    }

    public static bool Exists(string name)
    {
        int r = NetUserGetInfo(null, name, 20, out var buf);
        if (buf != 0) NetApiBufferFree(buf);
        return r == 0;
    }

    /// <summary>The built-in Guest account (RID 501), whatever it's called on this PC.</summary>
    public static LocalUser? BuiltinGuest() => All().FirstOrDefault(u => u.Rid == 501);

    /// <summary>A user or group by name: "mike", "PC\mike", "DOMAIN\mike" or "mike@example.com".</summary>
    public static SecurityIdentifier Sid(string name)
    {
        name = name.Trim();
        var account = name.Contains('\\') || name.Contains('@') ? name : $@"{Environment.MachineName}\{name}";
        try
        {
            return (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));
        }
        catch (IdentityNotMappedException)
        {
            if (account != name)   // not a local account: maybe a group or domain name without a prefix
                try { return (SecurityIdentifier)new NTAccount(name).Translate(typeof(SecurityIdentifier)); } catch (IdentityNotMappedException) { }
            throw new InvalidOperationException($"There's no user called \"{name}\" on this PC.");
        }
    }

    /// <summary>The account's name as people know it: "mike" for a local account, "DOMAIN\mike" otherwise.</summary>
    public static string Name(SecurityIdentifier sid)
    {
        try
        {
            var n = ((NTAccount)sid.Translate(typeof(NTAccount))).Value;
            var local = Environment.MachineName + "\\";
            return n.StartsWith(local, StringComparison.OrdinalIgnoreCase) ? n[local.Length..] : n;
        }
        catch (IdentityNotMappedException)
        {
            return sid.Value;
        }
    }

    /// <summary>Creates a standard (non-admin) local user whose password never expires, hidden from the sign-in screen:
    /// it exists only to connect to shares. Windows only creates a profile folder on an interactive sign-in, so network
    /// access leaves nothing else behind.</summary>
    public static SecurityIdentifier Create(string name, string password)
    {
        var info = new USER_INFO_1
        {
            name = name, password = password, priv = USER_PRIV_USER, comment = Comment,
            flags = UF_SCRIPT | UF_DONT_EXPIRE_PASSWD | UF_PASSWD_CANT_CHANGE,
        };
        Check(NetUserAdd(null, 1, ref info, out _), $"Couldn't create the user \"{name}\"");
        try
        {
            Hide(name);
        }
        catch
        {
            // Cosmetic only; the account works either way.
        }
        return Sid(name);
    }

    public static void SetPassword(string name, string password)
    {
        var info = new USER_INFO_1003 { password = password };
        Check(NetUserSetInfo(null, name, 1003, ref info, out _), $"Couldn't set the password of \"{name}\"");
    }

    public static void SetDisabled(string name, bool disabled)
    {
        var u = All().FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"There's no user called \"{name}\".");
        var info = new USER_INFO_1008 { flags = disabled ? u.Flags | UF_ACCOUNTDISABLE : u.Flags & ~UF_ACCOUNTDISABLE };
        Check(NetUserSetInfo(null, name, 1008, ref info, out _), $"Couldn't change the user \"{name}\"");
    }

    public static void Delete(string name)
    {
        int r = NetUserDel(null, name);
        if (r != NERR_UserNotFound) Check(r, $"Couldn't delete the user \"{name}\"");
        Unhide(name);
    }

    static void Hide(string name)
    {
        using (var existing = Registry.LocalMachine.OpenSubKey(SpecialAccounts))
            if (existing == null) Store.SetInt("UserListCreated", 1);
        using var k = Registry.LocalMachine.CreateSubKey(UserList, true);
        k.SetValue(name, 0, RegistryValueKind.DWord);
    }

    static void Unhide(string name)
    {
        bool empty;
        using (var k = Registry.LocalMachine.OpenSubKey(UserList, true))
        {
            if (k == null) return;
            k.DeleteValue(name, false);
            empty = k.ValueCount == 0 && k.SubKeyCount == 0;
        }
        // Remove the keys again only if the app created them and nothing else uses them.
        if (empty && Store.GetInt("UserListCreated") == 1)
        {
            using (var sa = Registry.LocalMachine.OpenSubKey(SpecialAccounts))
                if (sa is { SubKeyCount: 1, ValueCount: 0 }) Registry.LocalMachine.DeleteSubKeyTree(SpecialAccounts, false);
            Store.SetInt("UserListCreated", 0);
        }
    }
}

/// <summary>The guest password is kept so the app can show it again later: encrypted for this machine (DPAPI), inside
/// a registry key only administrators can read.</summary>
static class Dpapi
{
    public static byte[] Protect(string text) => Run(System.Text.Encoding.UTF8.GetBytes(text), true);

    public static string? Unprotect(byte[]? data)
    {
        if (data == null) return null;
        try
        {
            return System.Text.Encoding.UTF8.GetString(Run(data, false));
        }
        catch
        {
            return null;
        }
    }

    static byte[] Run(byte[] data, bool protect)
    {
        var mem = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, mem, data.Length);
            var input = new DATA_BLOB { cbData = data.Length, pbData = mem };
            const int flags = CRYPTPROTECT_UI_FORBIDDEN | CRYPTPROTECT_LOCAL_MACHINE;
            bool ok = protect
                ? CryptProtectData(ref input, "Network Shares", 0, 0, 0, flags, out var output)
                : CryptUnprotectData(ref input, 0, 0, 0, 0, flags, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally
            {
                LocalFree(output.pbData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(mem);
        }
    }
}
