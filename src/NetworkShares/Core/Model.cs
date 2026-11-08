namespace NetworkShares;

/// <summary>Who a share is for. Saved as numbers: append only.</summary>
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

/// <summary>A share the app created, as remembered in the registry.</summary>
/// <param name="Sids">The accounts the share is for; the first is the main one (a user, the guest login, or Everyone).</param>
/// <param name="Acl">The NTFS entries the app added for this share, to revert on removal.</param>
/// <param name="NullSession">The share was added to the server's anonymous-access list (anonymous mode).</param>
public sealed record ShareRecord(string Name, string Path, ShareMode Mode, Access Access, string Account,
    IReadOnlyList<string> Sids, IReadOnlyList<AclChange> Acl, bool NullSession)
{
    public string Sid => Sids.Count > 0 ? Sids[0] : "";

    public string Who => Mode switch
    {
        ShareMode.Anonymous => "Everyone · anonymous",
        ShareMode.Guest => $"Everyone · {Account}",
        _ => Account,
    };

    public string AccessText => Access == Access.Read ? "Read only" : "Read & write";

    public bool SamePath(string other) => string.Equals(Normalize(Path), Normalize(other), StringComparison.OrdinalIgnoreCase);

    static string Normalize(string p) => p.Replace('/', '\\').TrimEnd('\\');
}

/// <summary>A local account the app created (for one share, or the shared guest login).</summary>
public sealed record CreatedUser(string Name, string Sid, bool Guest);
