using System.Security.Principal;

namespace NetworkShares;

public sealed record ShareRequest(string Folder, string Name, ShareMode Mode, Access Access, string User, string Password);

/// <summary>A new share, plus the login to pass on when the app created one.</summary>
public sealed record ShareResult(ShareRecord Record, string? User, string? Password);

/// <summary>Creating and removing shares as one step each: everything a share needs is set up together, rolled back
/// together if any part fails, and recorded in <see cref="Store"/> so removal can undo exactly that.</summary>
static class Sharing
{
    public const string GuestUserName = "share-guest";

    public static ShareResult Create(ShareRequest q)
    {
        var undo = new Stack<Action>();
        try
        {
            var folder = Path.GetFullPath(q.Folder.Trim());
            if (!Directory.Exists(folder)) throw new InvalidOperationException("The folder doesn't exist.");
            if (Naming.ShareNameError(q.Name) is { } nameError) throw new InvalidOperationException(nameError);
            if (SmbShares.PathOf(q.Name) != null) throw new InvalidOperationException($"There's already a share called \"{q.Name}\" on this PC.");

            string account;
            string? user = null, password = null;
            List<SecurityIdentifier> shareSids, folderSids;
            switch (q.Mode)
            {
                case ShareMode.ExistingUser:
                {
                    var sid = Accounts.Sid(q.User);
                    account = Accounts.Name(sid);
                    shareSids = folderSids = [sid];
                    break;
                }
                case ShareMode.NewUser:
                {
                    if (Naming.UserNameError(q.User) is { } userError) throw new InvalidOperationException(userError);
                    if (string.IsNullOrEmpty(q.Password)) throw new InvalidOperationException("Enter a password for the new user.");
                    if (Accounts.Exists(q.User)) throw new InvalidOperationException($"There's already a user called \"{q.User}\". Pick it under Existing user, or choose another name.");
                    var sid = Accounts.Create(q.User, q.Password);
                    undo.Push(() => Accounts.Delete(q.User));
                    Store.SaveUser(new CreatedUser(q.User, sid.Value, false));
                    undo.Push(() => Store.DeleteUser(q.User));
                    (account, user, password) = (q.User, q.User, q.Password);
                    shareSids = folderSids = [sid];
                    break;
                }
                case ShareMode.Guest:
                {
                    var (name, pw, sid) = EnsureGuest(undo);
                    (account, user, password) = (name, name, pw);
                    shareSids = folderSids = [sid];
                    break;
                }
                case ShareMode.Anonymous:
                {
                    Anonymous.Enable();
                    undo.Push(() => { if (!Store.Shares().Any(s => s.Mode == ShareMode.Anonymous)) Anonymous.Disable(); });
                    account = "Everyone";
                    shareSids = folderSids = [Anonymous.Everyone, Anonymous.AnonymousLogon];
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(q));
            }

            var acl = new List<AclChange>();
            foreach (var sid in folderSids)
                if (FolderAcl.Grant(folder, sid, q.Access) is { } change)
                {
                    acl.Add(change);
                    undo.Push(() => FolderAcl.Revert(folder, change));
                }

            SmbShares.Add(q.Name, folder, shareSids, q.Access);
            undo.Push(() => SmbShares.Remove(q.Name));

            bool nullSession = false;
            if (q.Mode == ShareMode.Anonymous)
            {
                nullSession = Anonymous.AddNullSessionShare(q.Name);
                if (nullSession) undo.Push(() => Anonymous.RemoveNullSessionShare(q.Name));
            }

            var record = new ShareRecord(q.Name, folder, q.Mode, q.Access, account, shareSids.Select(s => s.Value).ToList(), acl, nullSession);
            Store.Save(record);
            return new ShareResult(record, user, password);
        }
        catch
        {
            while (undo.Count > 0)
                try { undo.Pop()(); } catch { /* best effort: keep unwinding */ }
            try { Store.Prune(); } catch { }
            throw;
        }
    }

    /// <summary>The shared guest login (created the first time), with its password.</summary>
    static (string Name, string Password, SecurityIdentifier Sid) EnsureGuest(Stack<Action> undo)
    {
        if (Guest() is { } g && Accounts.Exists(g.Name))
        {
            var pw = Dpapi.Unprotect(Store.Secret(g.Name));
            if (pw == null)   // unreadable (e.g. the registry was copied from another PC): give it a fresh one
            {
                pw = Passwords.Generate();
                Accounts.SetPassword(g.Name, pw);
                Store.SaveUser(g, Dpapi.Protect(pw));
            }
            return (g.Name, pw, new SecurityIdentifier(g.Sid));
        }
        if (Guest() is { } stale) Store.DeleteUser(stale.Name);   // deleted outside the app
        if (Accounts.Exists(GuestUserName))
            throw new InvalidOperationException($"A user called \"{GuestUserName}\" already exists but wasn't created by this app. Rename or delete it, or share with that user instead.");
        var password = Passwords.Generate();
        var sid = Accounts.Create(GuestUserName, password);
        undo.Push(() => Accounts.Delete(GuestUserName));
        Store.SaveUser(new CreatedUser(GuestUserName, sid.Value, true), Dpapi.Protect(password));
        undo.Push(() => Store.DeleteUser(GuestUserName));
        return (GuestUserName, password, sid);
    }

    public static CreatedUser? Guest() => Store.Users().FirstOrDefault(u => u.Guest);

    public static string? GuestPassword() => Guest() is { } g ? Dpapi.Unprotect(Store.Secret(g.Name)) : null;

    /// <summary>A new password for the guest login. Other PCs that saved the old one have to enter it again.</summary>
    public static string ResetGuestPassword()
    {
        var g = Guest() ?? throw new InvalidOperationException("There's no guest login yet.");
        var pw = Passwords.Generate();
        Accounts.SetPassword(g.Name, pw);
        Store.SaveUser(g, Dpapi.Protect(pw));
        return pw;
    }

    /// <summary>The user the app created for this share, if no other share of the app still uses it.</summary>
    public static CreatedUser? DeletableUser(ShareRecord r) =>
        Store.Users().FirstOrDefault(u => u.Sid == r.Sid) is { } u && !Store.Shares().Any(s => s.Name != r.Name && s.Sids.Contains(u.Sid))
            ? u : null;

    /// <summary>Removes the share and undoes what it changed. Returns problems that didn't stop the removal.</summary>
    public static List<string> Remove(ShareRecord r, bool deleteUser)
    {
        var warnings = new List<string>();
        var user = deleteUser ? DeletableUser(r) : null;

        // The share: only if it's still the one the app made (same name could have been reused for another folder).
        var current = SmbShares.PathOf(r.Name);
        if (current != null && r.SamePath(current)) SmbShares.Remove(r.Name);
        else if (current != null) warnings.Add($"A share called \"{r.Name}\" now points to {current}; it was left alone.");

        if (r.NullSession) Try(() => Anonymous.RemoveNullSessionShare(r.Name), warnings);

        // Folder permissions. If another of the app's shares of the same folder relies on an entry, hand it over instead.
        var others = Store.Shares().Where(s => s.Name != r.Name).ToList();
        foreach (var change in r.Acl.Reverse())
        {
            var heir = others.FirstOrDefault(s => s.SamePath(r.Path) && s.Sids.Contains(change.Sid));
            if (heir != null)
            {
                var updated = heir with { Acl = [.. heir.Acl, change] };
                Store.Save(updated);
                others[others.IndexOf(heir)] = updated;
            }
            else
            {
                Try(() => FolderAcl.Revert(r.Path, change), warnings, "Couldn't restore the folder's permissions");
            }
        }

        Store.Delete(r);

        if (user != null)
            Try(() =>
            {
                Accounts.Delete(user.Name);
                Store.DeleteUser(user.Name);
            }, warnings);

        if (r.Mode == ShareMode.Anonymous && !others.Any(s => s.Mode == ShareMode.Anonymous))
            Try(Anonymous.Disable, warnings, "Couldn't switch anonymous access off again");

        Store.Prune();
        return warnings;
    }

    /// <summary>Removes every share the app made, optionally its users, and undoes every fix.</summary>
    public static List<string> CleanUp(bool deleteUsers)
    {
        var warnings = new List<string>();
        foreach (var r in Store.Shares())
            try { warnings.AddRange(Remove(r, deleteUsers)); }
            catch (Exception e) { warnings.Add(e.Message); }
        if (deleteUsers)
            foreach (var u in Store.Users())
                Try(() =>
                {
                    Accounts.Delete(u.Name);
                    Store.DeleteUser(u.Name);
                }, warnings);
        warnings.AddRange(Fixes.Undo());
        Store.Prune();
        return warnings;
    }

    static void Try(Action a, List<string> warnings, string? what = null)
    {
        try { a(); }
        catch (Exception e) { warnings.Add(what == null ? e.Message : $"{what}: {e.Message}"); }
    }
}
