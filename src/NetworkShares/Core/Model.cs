namespace NetworkShares;

/// <summary>Who a grant is for. Saved as numbers: append only.</summary>
public enum ShareMode { ExistingUser, NewUser, Guest, Anonymous }

public enum Access { Read, ReadWrite }

/// <summary>
/// One permission entry the app added to a folder's NTFS permissions. <see cref="Prev"/> is what the same account had
/// before in an explicit, inherited-down entry on that folder (0 = nothing), so removing the share puts that back exactly.
/// </summary>
public sealed record AclChange(string Sid, int Prev)
{
    public override string ToString() => $"{Sid}|{Prev}";

    public static AclChange? Parse(string s)
    {
        var p = s.Split('|');
        return p.Length == 2 && p[0].StartsWith("S-", StringComparison.Ordinal) && int.TryParse(p[1], out int prev) ? new(p[0], prev) : null;
    }
}

/// <summary>One person (or Everyone) with access to a share.</summary>
/// <param name="Sids">The accounts behind it: one user, or Everyone and Anonymous Logon for anonymous access.</param>
public sealed record Grant(ShareMode Kind, string Account, IReadOnlyList<string> Sids, Access Access)
{
    public string Sid => Sids.Count > 0 ? Sids[0] : "";

    public bool IsEveryone => Kind is ShareMode.Guest or ShareMode.Anonymous;

    public string Label => Kind switch
    {
        ShareMode.Anonymous => "Everyone (anonymous)",
        ShareMode.Guest => $"Everyone ({Account})",
        _ => Account,
    };

    public override string ToString() => $"{(int)Kind}|{(int)Access}|{Account}|{string.Join(',', Sids)}";

    public static Grant? Parse(string s)
    {
        var p = s.Split('|');
        if (p.Length != 4 || !int.TryParse(p[0], out int kind) || !int.TryParse(p[1], out int access)) return null;
        if (!Enum.IsDefined(typeof(ShareMode), kind) || !Enum.IsDefined(typeof(Access), access)) return null;
        var sids = p[3].Split(',', StringSplitOptions.RemoveEmptyEntries);
        return sids.Length == 0 || !sids.All(x => x.StartsWith("S-", StringComparison.Ordinal))
            ? null : new Grant((ShareMode)kind, p[2], sids, (Access)access);
    }
}

public static class AccessText
{
    public static string Of(Access a) => a == Access.Read ? "Read only" : "Read & write";
}

/// <summary>A share the app created, as remembered in the registry.</summary>
/// <param name="Acl">The NTFS entries the app added for this share, to revert on removal.</param>
/// <param name="NullSession">The share is on the server's anonymous-access list (anonymous access).</param>
public sealed record ShareRecord(string Name, string Path, IReadOnlyList<Grant> Grants, IReadOnlyList<AclChange> Acl, bool NullSession)
{
    public IEnumerable<string> AllSids => Grants.SelectMany(g => g.Sids);

    public string Who => Grants.Count switch
    {
        0 => "nobody",
        1 => $"{Grants[0].Label}  ·  {AccessText.Of(Grants[0].Access)}",
        2 => $"{Grants[0].Label}, {Grants[1].Label}",
        _ => $"{Grants[0].Label} and {Grants.Count - 1} more",
    };

    public bool SamePath(string other) => string.Equals(Normalize(Path), Normalize(other), StringComparison.OrdinalIgnoreCase);

    static string Normalize(string p) => p.Replace('/', '\\').TrimEnd('\\');
}

/// <summary>A local account the app created (for a share, or the shared guest login).</summary>
public sealed record CreatedUser(string Name, string Sid, bool Guest);

/// <summary>A person to give access, as entered in the editor. <see cref="Sid"/> is set for people the share already has
/// (when editing); <see cref="Password"/> only for a new user.</summary>
public sealed record GrantSpec(ShareMode Kind, string User, string? Password, Access Access, string? Sid = null);

public sealed record ShareSpec(string Folder, string Name, IReadOnlyList<GrantSpec> Grants);

/// <param name="DeleteUsers">Users the app created that lose their last share with this edit and should be deleted.</param>
public sealed record UpdateSpec(string OldName, string Name, IReadOnlyList<GrantSpec> Grants, IReadOnlyList<string> DeleteUsers);

public sealed record RemoveSpec(string Name, IReadOnlyList<string> DeleteUsers);

/// <summary>A sign-in to pass on: a new user's, or the guest login.</summary>
public sealed record Login(string User, string Password, bool Guest);

public sealed record ShareResult(ShareRecord Record, IReadOnlyList<Login> Logins, IReadOnlyList<string> Warnings);

/// <summary>Everything the window shows, in one round trip.</summary>
public sealed record Snapshot(
    IReadOnlyList<ShareRecord> Shares, IReadOnlyList<string> Missing, IReadOnlyList<CreatedUser> Users, bool HasFixes,
    string? GuestName, string? GuestPassword, IReadOnlyList<string> LocalUsers);

/// <summary>What stands between a share and the network.</summary>
public sealed record Health(bool ServerOk, bool FirewallOk, bool HasFixes)
{
    public bool Ok => ServerOk && FirewallOk;

    public IEnumerable<string> Problems()
    {
        if (!ServerOk) yield return "The Server service (file sharing) isn't running.";
        if (!FirewallOk) yield return "Windows Firewall blocks file sharing on the current network.";
    }
}

public static class Planning
{
    /// <summary>Users the app created that no share would use any more once <paramref name="shareName"/> stops giving
    /// <paramref name="removedSids"/> access: the ones to offer for deletion.</summary>
    public static List<CreatedUser> OrphanedUsers(IEnumerable<ShareRecord> shares, IEnumerable<CreatedUser> users, string shareName,
        IEnumerable<string> removedSids)
    {
        var removed = removedSids.ToHashSet();
        var others = shares.Where(s => s.Name != shareName).SelectMany(s => s.AllSids).ToHashSet();
        return users.Where(u => removed.Contains(u.Sid) && !others.Contains(u.Sid)).ToList();
    }

    /// <summary>The access each account needs on the folder: the highest any of its grants gives.</summary>
    public static Dictionary<string, Access> FolderAccess(IEnumerable<Grant> grants)
    {
        var d = new Dictionary<string, Access>();
        foreach (var g in grants)
            foreach (var sid in g.Sids)
                d[sid] = d.TryGetValue(sid, out var a) && a == Access.ReadWrite ? a : g.Access;
        return d;
    }

    /// <summary>Why a set of grants can't be saved, or null if it can.</summary>
    public static string? GrantsError(IReadOnlyList<Grant> grants)
    {
        if (grants.Count == 0) return "Add at least one person.";
        if (grants.Count(g => g.IsEveryone) > 1) return "Everyone can only be added once.";
        var twice = grants.GroupBy(g => g.Sid).FirstOrDefault(x => x.Count() > 1);
        return twice != null ? $"{twice.First().Label} is listed twice." : null;
    }
}
