using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace NetworkShares;

/// <summary>The Windows APIs the app calls directly: no net.exe, PowerShell or WMI.</summary>
static class Native
{
    // ── netapi32: shares and users ──────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHARE_INFO_502
    {
        public string? netname;
        public uint type;
        public string? remark;
        public uint permissions;
        public int max_uses;
        public uint current_uses;
        public string? path;
        public string? passwd;
        public uint reserved;
        public nint security_descriptor;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHARE_INFO_2
    {
        public string? netname;
        public uint type;
        public string? remark;
        public uint permissions;
        public int max_uses;
        public uint current_uses;
        public string? path;
        public string? passwd;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct USER_INFO_1
    {
        public string? name;
        public string? password;
        public uint password_age;
        public uint priv;
        public string? home_dir;
        public string? comment;
        public uint flags;
        public string? script_path;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct USER_INFO_20
    {
        public string? name;
        public string? full_name;
        public string? comment;
        public uint flags;
        public uint user_id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct USER_INFO_1003 { public string? password; }

    [StructLayout(LayoutKind.Sequential)]
    public struct USER_INFO_1008 { public uint flags; }

    public const uint UF_SCRIPT = 0x1, UF_ACCOUNTDISABLE = 0x2, UF_PASSWD_CANT_CHANGE = 0x40, UF_DONT_EXPIRE_PASSWD = 0x10000;
    public const uint USER_PRIV_USER = 1;
    public const int FILTER_NORMAL_ACCOUNT = 2, MAX_PREFERRED_LENGTH = -1;
    public const int NERR_UserNotFound = 2221, NERR_NetNameNotFound = 2310;

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetShareAdd(string? server, int level, ref SHARE_INFO_502 buf, out int parmErr);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetShareDel(string? server, string netname, int reserved);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetShareGetInfo(string? server, string netname, int level, out nint buf);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserAdd(string? server, int level, ref USER_INFO_1 buf, out int parmErr);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserDel(string? server, string user);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserGetInfo(string? server, string user, int level, out nint buf);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserSetInfo(string? server, string user, int level, ref USER_INFO_1003 buf, out int parmErr);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserSetInfo(string? server, string user, int level, ref USER_INFO_1008 buf, out int parmErr);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserEnum(string? server, int level, int filter, out nint buf, int prefMaxLen,
        out int entriesRead, out int totalEntries, ref int resumeHandle);

    [DllImport("netapi32.dll")]
    public static extern int NetApiBufferFree(nint buf);

    /// <summary>What a netapi32 / Win32 error code means, in words a user can act on.</summary>
    public static string Message(int code) => code switch
    {
        5 => "Access denied.",
        2116 => "The folder doesn't exist.",
        2118 => "A share with this name already exists.",
        2221 => "The user doesn't exist.",
        2224 => "A user or group with this name already exists.",
        2245 => "The password doesn't meet this PC's password rules (length or complexity).",
        2310 => "The share doesn't exist.",
        2351 => "The computer name isn't valid.",
        _ => new Win32Exception(code).Message,
    };

    public static void Check(int code, string what)
    {
        if (code != 0) throw new InvalidOperationException($"{what}: {Message(code)}");
    }

    // ── advapi32: the Server service ────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_STATUS
    {
        public uint serviceType, currentState, controlsAccepted, win32ExitCode, serviceSpecificExitCode, checkPoint, waitHint;
    }

    public const uint SC_MANAGER_CONNECT = 0x1, SERVICE_QUERY_CONFIG = 0x1, SERVICE_CHANGE_CONFIG = 0x2, SERVICE_QUERY_STATUS = 0x4,
        SERVICE_START = 0x10, SERVICE_STOP = 0x20, SERVICE_NO_CHANGE = 0xFFFFFFFF, SERVICE_CONTROL_STOP = 1;
    public const uint SERVICE_STOPPED = 1, SERVICE_RUNNING = 4;
    public const uint SERVICE_AUTO_START = 2, SERVICE_DISABLED = 4;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint OpenSCManager(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint OpenService(nint scm, string name, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool CloseServiceHandle(nint handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool QueryServiceStatus(nint service, out SERVICE_STATUS status);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool QueryServiceConfig(nint service, nint buf, int size, out int needed);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ChangeServiceConfig(nint service, uint type, uint startType, uint errorControl, string? path,
        string? loadOrderGroup, nint tagId, string? dependencies, string? account, string? password, string? displayName);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool StartService(nint service, int argc, string[]? argv);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool ControlService(nint service, uint control, out SERVICE_STATUS status);

    // ── advapi32: user rights (LSA) ─────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct LSA_OBJECT_ATTRIBUTES
    {
        public int Length;
        public nint RootDirectory, ObjectName;
        public uint Attributes;
        public nint SecurityDescriptor, SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LSA_UNICODE_STRING
    {
        public ushort Length, MaximumLength;
        public nint Buffer;
    }

    public const int POLICY_ALL_ACCESS = 0x00F0FFF;
    public const uint STATUS_OBJECT_NAME_NOT_FOUND = 0xC0000034;

    [DllImport("advapi32.dll")]
    public static extern uint LsaOpenPolicy(nint systemName, ref LSA_OBJECT_ATTRIBUTES attributes, int access, out nint policy);

    [DllImport("advapi32.dll")]
    public static extern uint LsaEnumerateAccountRights(nint policy, byte[] sid, out nint rights, out uint count);

    [DllImport("advapi32.dll")]
    public static extern uint LsaAddAccountRights(nint policy, byte[] sid, LSA_UNICODE_STRING[] rights, uint count);

    [DllImport("advapi32.dll")]
    public static extern uint LsaRemoveAccountRights(nint policy, byte[] sid, [MarshalAs(UnmanagedType.U1)] bool allRights,
        LSA_UNICODE_STRING[] rights, uint count);

    [DllImport("advapi32.dll")]
    public static extern uint LsaFreeMemory(nint buf);

    [DllImport("advapi32.dll")]
    public static extern uint LsaClose(nint policy);

    [DllImport("advapi32.dll")]
    public static extern int LsaNtStatusToWinError(uint status);

    // ── crypt32: protecting the guest password at rest ──────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct DATA_BLOB
    {
        public int cbData;
        public nint pbData;
    }

    public const int CRYPTPROTECT_UI_FORBIDDEN = 0x1, CRYPTPROTECT_LOCAL_MACHINE = 0x4;

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CryptProtectData(ref DATA_BLOB dataIn, string? description, nint entropy, nint reserved,
        nint prompt, int flags, out DATA_BLOB dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    public static extern bool CryptUnprotectData(ref DATA_BLOB dataIn, nint description, nint entropy, nint reserved,
        nint prompt, int flags, out DATA_BLOB dataOut);

    [DllImport("kernel32.dll")]
    public static extern nint LocalFree(nint mem);

    // ── shell32 / user32: drag and drop into an elevated window ─────────────────────────────────

    public const int WM_DROPFILES = 0x0233, WM_COPYDATA = 0x004A, WM_COPYGLOBALDATA = 0x0049;

    [DllImport("shell32.dll")]
    public static extern void DragAcceptFiles(nint hwnd, bool accept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint DragQueryFile(nint drop, uint index, StringBuilder? file, int size);

    [DllImport("shell32.dll")]
    public static extern void DragFinish(nint drop);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ChangeWindowMessageFilterEx(nint hwnd, int message, int action, nint filterStatus);
}
