using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace NetworkShares;

/// <summary>
/// Everything the app has changed, in one registry key: HKLM\SOFTWARE\NetworkShares. Only administrators and SYSTEM can
/// read it. The key exists only while something is recorded, and is deleted when the last share, user and fix is gone.
/// <code>
///   Shares\&lt;name&gt;     Path, Mode, Access, Account, Sids, Acl, NullSession
///   Users\&lt;name&gt;      Sid, Guest, Secret (guest login only: its password, encrypted with DPAPI)
///   Fixes\Server     StartType, WasRunning       (how the Server service was before the fix)
///   Fixes\Firewall   Rules                       (the rules the fix turned on)
///   Anonymous        GuestWasDisabled, GuestDeniedNetwork, NullSessionValueCreated
///   (values)         UserListCreated             (the app created the key that hides users from the sign-in screen)
/// </code>
/// </summary>
static class Store
{
    const string RootPath = @"SOFTWARE\NetworkShares";

    /// <summary>Opens the key for writing, creating it (admin-only) the first time something is recorded.</summary>
    static RegistryKey Root()
    {
        if (Registry.LocalMachine.OpenSubKey(RootPath, true) is { } existing) return existing;
        var sec = new RegistrySecurity();
        sec.SetAccessRuleProtection(true, false);
        foreach (var type in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
            sec.AddAccessRule(new RegistryAccessRule(new SecurityIdentifier(type, null), RegistryRights.FullControl,
                InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow));
        return Registry.LocalMachine.CreateSubKey(RootPath, RegistryKeyPermissionCheck.ReadWriteSubTree, sec);
    }

    static RegistryKey? Read(string sub) => Registry.LocalMachine.OpenSubKey(sub.Length == 0 ? RootPath : $@"{RootPath}\{sub}");

    /// <summary>A section to write to, e.g. "Fixes\Server"; created if needed.</summary>
    public static RegistryKey Section(string sub)
    {
        using var root = Root();
        return root.CreateSubKey(sub, true);
    }

    public static RegistryKey? OpenSection(string sub) => Read(sub);

    public static void DeleteSection(string sub)
    {
        using var root = Registry.LocalMachine.OpenSubKey(RootPath, true);
        root?.DeleteSubKeyTree(sub, false);
    }

    public static int GetInt(string name)
    {
        using var root = Read("");
        return root?.GetValue(name) is int i ? i : 0;
    }

    public static void SetInt(string name, int value)
    {
        if (value == 0)
        {
            using var r = Registry.LocalMachine.OpenSubKey(RootPath, true);
            r?.DeleteValue(name, false);
            return;
        }
        using var root = Root();
        root.SetValue(name, value, RegistryValueKind.DWord);
    }

    // ── Shares ──────────────────────────────────────────────────────────────────────────────────

    public static List<ShareRecord> Shares()
    {
        using var key = Read("Shares");
        if (key == null) return [];
        var list = new List<ShareRecord>();
        foreach (var name in key.GetSubKeyNames())
        {
            using var s = key.OpenSubKey(name);
            if (s == null) continue;
            list.Add(new ShareRecord(
                name,
                s.GetValue("Path") as string ?? "",
                (ShareMode)(s.GetValue("Mode") as int? ?? 0),
                (Access)(s.GetValue("Access") as int? ?? 0),
                s.GetValue("Account") as string ?? "",
                s.GetValue("Sids") as string[] ?? [],
                (s.GetValue("Acl") as string[] ?? []).Select(AclChange.Parse).OfType<AclChange>().ToList(),
                s.GetValue("NullSession") as int? == 1));
        }
        return list.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void Save(ShareRecord r)
    {
        using var k = Section($@"Shares\{r.Name}");
        k.SetValue("Path", r.Path);
        k.SetValue("Mode", (int)r.Mode, RegistryValueKind.DWord);
        k.SetValue("Access", (int)r.Access, RegistryValueKind.DWord);
        k.SetValue("Account", r.Account);
        k.SetValue("Sids", r.Sids.ToArray(), RegistryValueKind.MultiString);
        k.SetValue("Acl", r.Acl.Select(a => a.ToString()).ToArray(), RegistryValueKind.MultiString);
        k.SetValue("NullSession", r.NullSession ? 1 : 0, RegistryValueKind.DWord);
    }

    public static void Delete(ShareRecord r) => DeleteSection($@"Shares\{r.Name}");

    // ── Users ───────────────────────────────────────────────────────────────────────────────────

    public static List<CreatedUser> Users()
    {
        using var key = Read("Users");
        if (key == null) return [];
        var list = new List<CreatedUser>();
        foreach (var name in key.GetSubKeyNames())
        {
            using var u = key.OpenSubKey(name);
            if (u != null) list.Add(new CreatedUser(name, u.GetValue("Sid") as string ?? "", u.GetValue("Guest") as int? == 1));
        }
        return list;
    }

    public static void SaveUser(CreatedUser u, byte[]? secret = null)
    {
        using var k = Section($@"Users\{u.Name}");
        k.SetValue("Sid", u.Sid);
        k.SetValue("Guest", u.Guest ? 1 : 0, RegistryValueKind.DWord);
        if (secret != null) k.SetValue("Secret", secret, RegistryValueKind.Binary);
    }

    public static byte[]? Secret(string user)
    {
        using var k = Read($@"Users\{user}");
        return k?.GetValue("Secret") as byte[];
    }

    public static void DeleteUser(string name) => DeleteSection($@"Users\{name}");

    public static bool HasFixes()
    {
        using var k = Read("Fixes");
        return k != null && k.SubKeyCount > 0;
    }

    /// <summary>Deletes empty sections, and the whole key once nothing is recorded.</summary>
    public static void Prune()
    {
        using (var root = Registry.LocalMachine.OpenSubKey(RootPath, true))
        {
            if (root == null) return;
            foreach (var sub in root.GetSubKeyNames()) PruneKey(root, sub);
            if (root.SubKeyCount > 0 || root.ValueCount > 0) return;
        }
        Registry.LocalMachine.DeleteSubKey(RootPath, false);
    }

    static void PruneKey(RegistryKey parent, string name)
    {
        bool empty;
        using (var k = parent.OpenSubKey(name, true))
        {
            if (k == null) return;
            foreach (var sub in k.GetSubKeyNames()) PruneKey(k, sub);
            empty = k.SubKeyCount == 0 && k.ValueCount == 0;
        }
        if (empty) parent.DeleteSubKey(name, false);
    }
}
