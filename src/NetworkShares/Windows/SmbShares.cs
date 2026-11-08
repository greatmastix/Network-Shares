using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using static NetworkShares.Native;

namespace NetworkShares;

/// <summary>The shares themselves (netapi32), with their share-level permissions.</summary>
static class SmbShares
{
    /// <summary>The description other PCs see next to the share. The app's own list is the registry, not this text.</summary>
    public const string Remark = "Shared with Network Shares";

    // Share permissions as Windows' own sharing dialog writes them: Read, and Change (read, write, delete).
    const int ReadMask = 0x1200A9, ChangeMask = 0x1301BF;

    /// <summary>Calls <paramref name="use"/> with a native security descriptor that allows each grant its access.</summary>
    static void WithDescriptor(IEnumerable<Grant> grants, Action<nint> use)
    {
        var aces = grants.SelectMany(g => g.Sids.Select(sid => (Sid: new SecurityIdentifier(sid), g.Access))).ToList();
        var dacl = new RawAcl(GenericAcl.AclRevision, aces.Count);
        foreach (var (sid, access) in aces)
            dacl.InsertAce(dacl.Count, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed,
                access == Access.Read ? ReadMask : ChangeMask, sid, false, null));
        var sd = new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent | ControlFlags.SelfRelative, null, null, null, dacl);
        var bytes = new byte[sd.BinaryLength];
        sd.GetBinaryForm(bytes, 0);
        var mem = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, mem, bytes.Length);
            use(mem);
        }
        finally
        {
            Marshal.FreeHGlobal(mem);
        }
    }

    public static void Add(string name, string path, IEnumerable<Grant> grants) => WithDescriptor(grants, sd =>
    {
        var info = new SHARE_INFO_502
        {
            netname = name, type = 0 /* STYPE_DISKTREE */, remark = Remark, max_uses = -1, path = path, security_descriptor = sd,
        };
        Check(NetShareAdd(null, 502, ref info, out _), $"Couldn't create the share \"{name}\"");
    });

    /// <summary>Replaces who may use the share, without interrupting anyone connected.</summary>
    public static void SetPermissions(string name, IEnumerable<Grant> grants) => WithDescriptor(grants, sd =>
    {
        var info = new SHARE_INFO_1501 { security_descriptor = sd };
        Check(NetShareSetInfo(null, name, 1501, ref info, out _), $"Couldn't change the share \"{name}\"");
    });

    public static void Remove(string name)
    {
        int r = NetShareDel(null, name, 0);
        if (r != NERR_NetNameNotFound) Check(r, $"Couldn't remove the share \"{name}\"");
    }

    /// <summary>The folder a share points to, or null if there's no share by that name.</summary>
    public static string? PathOf(string name)
    {
        int r = NetShareGetInfo(null, name, 2, out var buf);
        try
        {
            return r == 0 ? Marshal.PtrToStructure<SHARE_INFO_2>(buf).path : null;
        }
        finally
        {
            if (buf != 0) NetApiBufferFree(buf);
        }
    }
}

/// <summary>The folder's NTFS permissions: the second lock a network user has to pass, after the share's own.</summary>
static class FolderAcl
{
    const InheritanceFlags Inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    static FileSystemRights Rights(Access a) => a == Access.Read
        ? FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize
        : FileSystemRights.Modify | FileSystemRights.Synchronize;

    static FileSystemAccessRule Rule(SecurityIdentifier sid, FileSystemRights rights) =>
        new(sid, rights, Inherit, PropagationFlags.None, AccessControlType.Allow);

    static FileSystemAccessRule? Explicit(DirectorySecurity sec, SecurityIdentifier sid) =>
        sec.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().FirstOrDefault(r =>
            r.AccessControlType == AccessControlType.Allow && r.IdentityReference.Equals(sid)
            && r.InheritanceFlags == Inherit && r.PropagationFlags == PropagationFlags.None);

    /// <summary>Gives the account access to the folder and everything in it. Returns what to record for undoing it, or
    /// null if the folder already granted that explicitly (then nothing is changed).</summary>
    public static AclChange? Grant(string path, SecurityIdentifier sid, Access access)
    {
        var dir = new DirectoryInfo(path);
        var sec = dir.GetAccessControl();
        var want = Rights(access);
        var have = Explicit(sec, sid)?.FileSystemRights ?? 0;
        if ((have & want) == want) return null;
        sec.AddAccessRule(Rule(sid, want));
        dir.SetAccessControl(sec);
        return new AclChange(sid.Value, (int)have);
    }

    /// <summary>For an entry the app manages: sets it to what the account had before plus <paramref name="access"/>.</summary>
    public static void Set(string path, AclChange change, Access access)
    {
        var dir = new DirectoryInfo(path);
        var sec = dir.GetAccessControl();
        var sid = new SecurityIdentifier(change.Sid);
        var target = (FileSystemRights)change.Prev | Rights(access);
        var current = Explicit(sec, sid);
        if (current?.FileSystemRights == target) return;
        if (current != null) sec.RemoveAccessRuleSpecific(current);
        sec.AddAccessRule(Rule(sid, target));
        dir.SetAccessControl(sec);
    }

    /// <summary>Puts the account's entry back to what it was before <see cref="Grant"/>.</summary>
    public static void Revert(string path, AclChange change)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists) return;
        var sec = dir.GetAccessControl();
        var sid = new SecurityIdentifier(change.Sid);
        if (Explicit(sec, sid) is { } current) sec.RemoveAccessRuleSpecific(current);
        if (change.Prev != 0) sec.AddAccessRule(Rule(sid, (FileSystemRights)change.Prev));
        dir.SetAccessControl(sec);
    }

    /// <summary>The folder's permission list as it is now, to put back if a multi-step change fails half-way.</summary>
    public static byte[] Snapshot(string path) =>
        new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm();

    public static void Restore(string path, byte[] snapshot)
    {
        var sec = new DirectorySecurity();
        sec.SetSecurityDescriptorBinaryForm(snapshot, AccessControlSections.Access);
        new DirectoryInfo(path).SetAccessControl(sec);
    }
}
