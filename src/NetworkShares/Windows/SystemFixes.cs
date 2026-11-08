using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using static NetworkShares.Native;

namespace NetworkShares;

/// <summary>The one-click fixes, each undoable: the earliest "before" state is recorded in <see cref="Store"/>.</summary>
static class Fixes
{
    public static Health Check()
    {
        bool server, firewall;
        try { server = ServerService.IsReady(); } catch { server = true; }   // can't tell: don't nag
        try { firewall = Firewall.IsOpen(); } catch { firewall = true; }
        return new Health(server, firewall, Store.HasFixes());
    }

    public static void Apply(Health h)
    {
        if (!h.ServerOk) ServerService.Fix();
        if (!h.FirewallOk) Firewall.Open();
    }

    public static List<string> Undo()
    {
        var errors = new List<string>();
        try { Firewall.Undo(); } catch (Exception e) { errors.Add(e.Message); }
        try { ServerService.Undo(); } catch (Exception e) { errors.Add(e.Message); }
        Store.Prune();
        return errors;
    }
}

/// <summary>LanmanServer, the "Server" service that serves shares. On almost every PC it already runs.</summary>
static class ServerService
{
    const string Name = "LanmanServer", Section = @"Fixes\Server";

    sealed class Handle(nint scm, nint service) : IDisposable
    {
        public nint Service => service;

        public void Dispose()
        {
            if (service != 0) CloseServiceHandle(service);
            if (scm != 0) CloseServiceHandle(scm);
        }
    }

    static Handle Open()
    {
        var scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == 0) throw new System.ComponentModel.Win32Exception();
        var svc = OpenService(scm, Name, SERVICE_QUERY_CONFIG | SERVICE_CHANGE_CONFIG | SERVICE_QUERY_STATUS | SERVICE_START | SERVICE_STOP);
        if (svc == 0)
        {
            int err = Marshal.GetLastWin32Error();
            CloseServiceHandle(scm);
            throw new System.ComponentModel.Win32Exception(err);
        }
        return new Handle(scm, svc);
    }

    static (uint StartType, bool Running) State(Handle h)
    {
        if (!QueryServiceStatus(h.Service, out var status)) throw new System.ComponentModel.Win32Exception();
        QueryServiceConfig(h.Service, 0, 0, out int needed);
        var buf = Marshal.AllocHGlobal(Math.Max(needed, 64));
        try
        {
            if (!QueryServiceConfig(h.Service, buf, Math.Max(needed, 64), out _)) throw new System.ComponentModel.Win32Exception();
            uint startType = (uint)Marshal.ReadInt32(buf, 4);   // QUERY_SERVICE_CONFIG: dwServiceType, then dwStartType
            return (startType, status.currentState == SERVICE_RUNNING);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    public static bool IsReady()
    {
        using var h = Open();
        var (start, running) = State(h);
        return running && start != SERVICE_DISABLED;
    }

    public static void Fix()
    {
        using var h = Open();
        var (start, running) = State(h);
        using (var existing = Store.OpenSection(Section))
            if (existing == null)
            {
                using var k = Store.Section(Section);
                k.SetValue("StartType", (int)start, RegistryValueKind.DWord);
                k.SetValue("WasRunning", running ? 1 : 0, RegistryValueKind.DWord);
            }
        if (start == SERVICE_DISABLED && !ChangeServiceConfig(h.Service, SERVICE_NO_CHANGE, SERVICE_AUTO_START, SERVICE_NO_CHANGE,
                null, null, 0, null, null, null, null))
            throw new System.ComponentModel.Win32Exception();
        if (!running && !StartService(h.Service, 0, null))
        {
            int err = Marshal.GetLastWin32Error();
            if (err != 1056) throw new System.ComponentModel.Win32Exception(err);   // 1056: already running
        }
    }

    public static void Undo()
    {
        int startType, wasRunning;
        using (var k = Store.OpenSection(Section))
        {
            if (k == null) return;
            startType = k.GetValue("StartType") as int? ?? -1;
            wasRunning = k.GetValue("WasRunning") as int? ?? 1;
        }
        using (var h = Open())
        {
            if (wasRunning == 0)
            {
                // Services that depend on it (e.g. Computer Browser) can keep it running: then it stops at the next restart.
                ControlService(h.Service, SERVICE_CONTROL_STOP, out _);
            }
            if (startType >= 0 && !ChangeServiceConfig(h.Service, SERVICE_NO_CHANGE, (uint)startType, SERVICE_NO_CHANGE,
                    null, null, 0, null, null, null, null))
                throw new System.ComponentModel.Win32Exception();
        }
        Store.DeleteSection(Section);
    }
}

/// <summary>Windows Firewall's built-in "File and Printer Sharing (SMB-In)" rules (TCP 445). The fix only switches those
/// on, for the profile of the network the PC is on now; undo switches the same ones off again.</summary>
static class Firewall
{
    const string Group = "@FirewallAPI.dll,-28502";   // "File and Printer Sharing", the same in every language
    const string Section = @"Fixes\Firewall";
    const int Inbound = 1, Allow = 1;
    static readonly (int Bit, string Key)[] Profiles = [(1, "DomainProfile"), (2, "StandardProfile"), (4, "PublicProfile")];

    static dynamic Policy() => Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true)!)!;

    static IEnumerable<dynamic> SmbRules(dynamic policy)
    {
        foreach (dynamic rule in policy.Rules)
        {
            string? group = rule.Grouping, ports = rule.LocalPorts;
            if (group == Group && (int)rule.Direction == Inbound && (int)rule.Action == Allow && ports == "445") yield return rule;
        }
    }

    /// <summary>Profiles in use right now (a PC can be on several networks) whose firewall is on.</summary>
    static int GuardedProfiles(dynamic policy)
    {
        int current = policy.CurrentProfileTypes, guarded = 0;
        foreach (var (bit, key) in Profiles)
            if ((current & bit) != 0 && FirewallOn(key)) guarded |= bit;
        return guarded;
    }

    static bool FirewallOn(string profile)
    {
        // Group Policy wins over the local setting.
        using (var gp = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Policies\Microsoft\WindowsFirewall\{profile}"))
            if (gp?.GetValue("EnableFirewall") is int g) return g != 0;
        using var k = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{profile}");
        return k?.GetValue("EnableFirewall") is not int v || v != 0;
    }

    static int OpenProfiles(dynamic policy)
    {
        int open = 0;
        foreach (var rule in SmbRules(policy))
            if ((bool)rule.Enabled) open |= (int)rule.Profiles;
        return open;
    }

    public static bool IsOpen()
    {
        var policy = Policy();
        int guarded = GuardedProfiles(policy);
        return (guarded & ~OpenProfiles(policy)) == 0;
    }

    public static void Open()
    {
        var policy = Policy();
        int missing = GuardedProfiles(policy) & ~OpenProfiles(policy);
        if (missing == 0) return;
        var enabled = new List<string>();
        using (var k = Store.OpenSection(Section)) enabled.AddRange(k?.GetValue("Rules") as string[] ?? []);
        foreach (var rule in SmbRules(policy))
        {
            int p = rule.Profiles;
            if ((bool)rule.Enabled || (p & missing) == 0) continue;
            string id = $"{(string)rule.Name}|{p}";
            enabled.Add(id);
            using (var k = Store.Section(Section)) k.SetValue("Rules", enabled.ToArray(), RegistryValueKind.MultiString);   // before the change
            rule.Enabled = true;
        }
    }

    public static void Undo()
    {
        string[] ids;
        using (var k = Store.OpenSection(Section))
        {
            if (k == null) return;
            ids = k.GetValue("Rules") as string[] ?? [];
        }
        if (ids.Length > 0)
        {
            var policy = Policy();
            foreach (var rule in SmbRules(policy))
                if ((bool)rule.Enabled && ids.Contains($"{(string)rule.Name}|{(int)rule.Profiles}")) rule.Enabled = false;
        }
        Store.DeleteSection(Section);
    }
}

/// <summary>
/// Truly anonymous access (advanced): the built-in Guest account is enabled and allowed to connect over the network, and
/// each anonymous share is put on the server's null-session list. All of it is reverted when the last anonymous share
/// goes. Client PCs may still refuse: Windows 10/11 block "insecure guest logons" by default.
/// </summary>
static class Anonymous
{
    const string Section = "Anonymous", DenyNetwork = "SeDenyNetworkLogonRight";
    const string LanmanParams = @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters";
    public static readonly SecurityIdentifier Everyone = new(WellKnownSidType.WorldSid, null), AnonymousLogon = new(WellKnownSidType.AnonymousSid, null);

    public static void Enable()
    {
        var guest = Accounts.BuiltinGuest() ?? throw new InvalidOperationException("This PC has no built-in Guest account.");
        var guestSid = Accounts.Sid(guest.Name);
        bool denied = Lsa.HasRight(guestSid, DenyNetwork);
        using (var existing = Store.OpenSection(Section))
            if (existing == null)
            {
                using var k = Store.Section(Section);   // recorded before anything changes
                k.SetValue("GuestWasDisabled", guest.Disabled ? 1 : 0, RegistryValueKind.DWord);
                k.SetValue("GuestDeniedNetwork", denied ? 1 : 0, RegistryValueKind.DWord);
            }
        if (guest.Disabled) Accounts.SetDisabled(guest.Name, false);
        if (denied) Lsa.RemoveRight(guestSid, DenyNetwork);
    }

    public static void Disable()
    {
        int wasDisabled, wasDenied, valueCreated;
        using (var k = Store.OpenSection(Section))
        {
            if (k == null) return;
            wasDisabled = k.GetValue("GuestWasDisabled") as int? ?? 0;
            wasDenied = k.GetValue("GuestDeniedNetwork") as int? ?? 0;
            valueCreated = k.GetValue("NullSessionValueCreated") as int? ?? 0;
        }
        if (Accounts.BuiltinGuest() is { } guest)
        {
            if (wasDenied == 1) Lsa.AddRight(Accounts.Sid(guest.Name), DenyNetwork);
            if (wasDisabled == 1) Accounts.SetDisabled(guest.Name, true);
        }
        if (valueCreated == 1)
        {
            using var p = Registry.LocalMachine.OpenSubKey(LanmanParams, true);
            if (p?.GetValue("NullSessionShares") is string[] { Length: 0 }) p.DeleteValue("NullSessionShares", false);
        }
        Store.DeleteSection(Section);
    }

    /// <summary>Returns whether the app added it (it wasn't on the list already).</summary>
    public static bool AddNullSessionShare(string name)
    {
        using var p = Registry.LocalMachine.OpenSubKey(LanmanParams, true) ?? throw new InvalidOperationException("The Server service's settings are missing.");
        var value = p.GetValue("NullSessionShares");
        var list = value as string[] ?? [];
        if (list.Contains(name, StringComparer.OrdinalIgnoreCase)) return false;
        if (value == null)
            using (var k = Store.Section(Section)) k.SetValue("NullSessionValueCreated", 1, RegistryValueKind.DWord);
        p.SetValue("NullSessionShares", list.Append(name).ToArray(), RegistryValueKind.MultiString);
        return true;
    }

    public static void RemoveNullSessionShare(string name)
    {
        using var p = Registry.LocalMachine.OpenSubKey(LanmanParams, true);
        if (p?.GetValue("NullSessionShares") is not string[] list) return;
        p.SetValue("NullSessionShares", list.Where(s => !s.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray(), RegistryValueKind.MultiString);
    }
}

/// <summary>User rights assignment ("Deny access to this computer from the network" and friends).</summary>
static class Lsa
{
    static nint Open()
    {
        var attrs = new LSA_OBJECT_ATTRIBUTES { Length = Marshal.SizeOf<LSA_OBJECT_ATTRIBUTES>() };
        Throw(LsaOpenPolicy(0, ref attrs, POLICY_ALL_ACCESS, out var policy));
        return policy;
    }

    static void Throw(uint status)
    {
        if (status != 0) throw new System.ComponentModel.Win32Exception(LsaNtStatusToWinError(status));
    }

    static byte[] Bytes(SecurityIdentifier sid)
    {
        var b = new byte[sid.BinaryLength];
        sid.GetBinaryForm(b, 0);
        return b;
    }

    public static bool HasRight(SecurityIdentifier sid, string right)
    {
        var policy = Open();
        try
        {
            uint status = LsaEnumerateAccountRights(policy, Bytes(sid), out var rights, out uint count);
            if (status == STATUS_OBJECT_NAME_NOT_FOUND) return false;   // the account has no rights at all
            Throw(status);
            try
            {
                int size = Marshal.SizeOf<LSA_UNICODE_STRING>();
                for (int i = 0; i < count; i++)
                {
                    var s = Marshal.PtrToStructure<LSA_UNICODE_STRING>(rights + i * size);
                    if (Marshal.PtrToStringUni(s.Buffer, s.Length / 2) == right) return true;
                }
                return false;
            }
            finally
            {
                LsaFreeMemory(rights);
            }
        }
        finally
        {
            LsaClose(policy);
        }
    }

    public static void AddRight(SecurityIdentifier sid, string right) => Change(sid, right, true);

    public static void RemoveRight(SecurityIdentifier sid, string right) => Change(sid, right, false);

    static void Change(SecurityIdentifier sid, string right, bool add)
    {
        var policy = Open();
        var buf = Marshal.StringToHGlobalUni(right);
        try
        {
            var s = new[] { new LSA_UNICODE_STRING { Buffer = buf, Length = (ushort)(right.Length * 2), MaximumLength = (ushort)(right.Length * 2 + 2) } };
            Throw(add ? LsaAddAccountRights(policy, Bytes(sid), s, 1) : LsaRemoveAccountRights(policy, Bytes(sid), false, s, 1));
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
            LsaClose(policy);
        }
    }
}
