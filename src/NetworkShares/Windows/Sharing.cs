using System.Security.Principal;

namespace NetworkShares;

/// <summary>Creating, editing and removing shares as one step each: everything a share needs is set up together, rolled
/// back together if any part fails, and recorded in <see cref="Store"/> so removal can undo exactly that.</summary>
static class Sharing
{
    public const string GuestUserName = "share-guest";

    /// <summary>Steps to undo, newest first, if a later step fails.</summary>
    sealed class Undo
    {
        readonly Stack<Action> steps = new();

        public void Push(Action a) => steps.Push(a);

        public void Run()
        {
            while (steps.Count > 0)
                try { steps.Pop()(); } catch { /* best effort: keep unwinding */ }
            try { Store.Prune(); } catch { }
        }
    }

    public static Snapshot Snapshot()
    {
        var shares = Store.Shares();
        var missing = shares.Where(r => SmbShares.PathOf(r.Name) is not { } p || !r.SamePath(p)).Select(r => r.Name).ToList();
        var guest = Guest();
        return new Snapshot(shares, missing, Store.Users(), Store.HasFixes(), guest?.Name, guest != null ? GuestPassword() : null,
            Accounts.Selectable());
    }

    // ── Create ──────────────────────────────────────────────────────────────────────────────────

    public static ShareResult Create(ShareSpec q)
    {
        var undo = new Undo();
        try
        {
            var folder = Path.GetFullPath(q.Folder.Trim());
            var name = q.Name.Trim();
            if (!Directory.Exists(folder)) throw new InvalidOperationException("The folder doesn't exist.");
            if (Naming.ShareNameError(name) is { } nameError) throw new InvalidOperationException(nameError);
            if (SmbShares.PathOf(name) != null) throw new InvalidOperationException($"There's already a share called \"{name}\" on this PC.");
            if (q.Grants.Count == 0) throw new InvalidOperationException("Add at least one person.");

            var logins = new List<Login>();
            var grants = q.Grants.Select(g => Resolve(g, logins, undo)).ToList();
            if (Planning.GrantsError(grants) is { } grantsError) throw new InvalidOperationException(grantsError);

            var (acl, _) = ApplyFolder(folder, grants, [], undo);
            SmbShares.Add(name, folder, grants);
            undo.Push(() => SmbShares.Remove(name));

            bool nullSession = false;
            if (grants.Any(g => g.Kind == ShareMode.Anonymous))
            {
                nullSession = Anonymous.AddNullSessionShare(name);
                if (nullSession) undo.Push(() => Anonymous.RemoveNullSessionShare(name));
            }

            var record = new ShareRecord(name, folder, grants, acl, nullSession);
            Store.Save(record);
            return new ShareResult(record, logins, []);
        }
        catch
        {
            undo.Run();
            throw;
        }
    }

    // ── Edit ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Renames a share and/or changes who has access, changing only what differs.</summary>
    public static ShareResult Update(UpdateSpec q)
    {
        var old = Store.Shares().FirstOrDefault(s => s.Name == q.OldName)
                  ?? throw new InvalidOperationException($"The share \"{q.OldName}\" isn't in this app's list any more.");
        var name = q.Name.Trim();
        bool renamed = !string.Equals(name, old.Name, StringComparison.OrdinalIgnoreCase) || name != old.Name;
        var undo = new Undo();
        var logins = new List<Login>();
        List<AclChange> released;
        ShareRecord record;
        try
        {
            if (!Directory.Exists(old.Path)) throw new InvalidOperationException("The folder doesn't exist any more. Remove the share instead.");
            if (Naming.ShareNameError(name) is { } nameError) throw new InvalidOperationException(nameError);
            bool sameNameNewCase = renamed && string.Equals(name, old.Name, StringComparison.OrdinalIgnoreCase);
            if (renamed && !sameNameNewCase && SmbShares.PathOf(name) != null)
                throw new InvalidOperationException($"There's already a share called \"{name}\" on this PC.");

            // People: the ones kept (maybe with new access), then the new ones.
            var grants = new List<Grant>();
            foreach (var spec in q.Grants)
            {
                if (spec.Sid == null) continue;
                var existing = old.Grants.FirstOrDefault(g => g.Sid == spec.Sid)
                               ?? throw new InvalidOperationException($"{spec.User} isn't on this share any more.");
                grants.Add(existing with { Access = spec.Access });
            }
            foreach (var spec in q.Grants.Where(g => g.Sid == null)) grants.Add(Resolve(spec, logins, undo));
            if (Planning.GrantsError(grants) is { } grantsError) throw new InvalidOperationException(grantsError);

            List<AclChange> acl;
            (acl, released) = ApplyFolder(old.Path, grants, old.Acl, undo);

            // The share: renamed means removed and created again (Windows can't rename one); otherwise new permissions.
            var current = SmbShares.PathOf(old.Name);
            bool exists = current != null && old.SamePath(current);
            if (renamed)
            {
                if (exists)
                {
                    SmbShares.Remove(old.Name);
                    undo.Push(() => SmbShares.Add(old.Name, old.Path, old.Grants));
                }
                SmbShares.Add(name, old.Path, grants);
                undo.Push(() => SmbShares.Remove(name));
            }
            else if (exists)
            {
                SmbShares.SetPermissions(name, grants);
                undo.Push(() => SmbShares.SetPermissions(old.Name, old.Grants));
            }
            else
            {
                if (current != null) throw new InvalidOperationException($"A share called \"{name}\" now points to {current}. Pick another name.");
                SmbShares.Add(name, old.Path, grants);   // it was removed outside the app: share it again
                undo.Push(() => SmbShares.Remove(name));
            }

            // The anonymous list, by share name.
            bool anonymous = grants.Any(g => g.Kind == ShareMode.Anonymous), nullSession = old.NullSession;
            if (old.NullSession && (renamed || !anonymous))
            {
                Anonymous.RemoveNullSessionShare(old.Name);
                undo.Push(() => Anonymous.AddNullSessionShare(old.Name));
                nullSession = false;
            }
            if (anonymous && !nullSession)
            {
                nullSession = Anonymous.AddNullSessionShare(name);
                if (nullSession) undo.Push(() => Anonymous.RemoveNullSessionShare(name));
            }

            record = new ShareRecord(name, old.Path, grants, acl, nullSession);
            if (renamed) Store.Delete(old);
            Store.Save(record);
        }
        catch
        {
            undo.Run();
            throw;
        }

        // Saved. What's left only tidies up, so problems become warnings instead of undoing the edit.
        var warnings = new List<string>();
        Release(record.Name, record.Path, released, warnings);
        DeleteUsers(q.DeleteUsers, warnings);
        DisableAnonymousIfUnused(warnings);
        Store.Prune();
        return new ShareResult(record, logins, warnings);
    }

    // ── Remove ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Removes the share and undoes what it changed. Returns problems that didn't stop the removal.</summary>
    public static List<string> Remove(RemoveSpec q)
    {
        var r = Store.Shares().FirstOrDefault(s => s.Name == q.Name)
                ?? throw new InvalidOperationException($"The share \"{q.Name}\" isn't in this app's list any more.");
        var warnings = new List<string>();

        // The share: only if it's still the one the app made (the name could have been reused for another folder).
        var current = SmbShares.PathOf(r.Name);
        if (current != null && r.SamePath(current)) SmbShares.Remove(r.Name);
        else if (current != null) warnings.Add($"A share called \"{r.Name}\" now points to {current}; it was left alone.");

        if (r.NullSession) Try(() => Anonymous.RemoveNullSessionShare(r.Name), warnings);
        Store.Delete(r);
        Release(r.Name, r.Path, r.Acl, warnings);
        DeleteUsers(q.DeleteUsers, warnings);
        DisableAnonymousIfUnused(warnings);
        Store.Prune();
        return warnings;
    }

    /// <summary>Removes every share the app made, optionally its users, and undoes every fix.</summary>
    public static List<string> CleanUp(bool deleteUsers)
    {
        var warnings = new List<string>();
        foreach (var r in Store.Shares())
            try { warnings.AddRange(Remove(new RemoveSpec(r.Name, []))); }
            catch (Exception e) { warnings.Add(e.Message); }
        if (deleteUsers) DeleteUsers(Store.Users().Select(u => u.Name), warnings);
        warnings.AddRange(Fixes.Undo());
        Store.Prune();
        return warnings;
    }

    // ── Steps ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Turns a person from the editor into a grant: looks up an existing user, or creates the new user or the
    /// guest login, or switches anonymous access on.</summary>
    static Grant Resolve(GrantSpec g, List<Login> logins, Undo undo)
    {
        switch (g.Kind)
        {
            case ShareMode.ExistingUser:
            {
                var sid = Accounts.Sid(g.User);
                return new Grant(ShareMode.ExistingUser, Accounts.Name(sid), [sid.Value], g.Access);
            }
            case ShareMode.NewUser:
            {
                var user = g.User.Trim();
                if (Naming.UserNameError(user) is { } userError) throw new InvalidOperationException(userError);
                if (string.IsNullOrEmpty(g.Password)) throw new InvalidOperationException($"Enter a password for {user}.");
                if (Accounts.Exists(user)) throw new InvalidOperationException($"There's already a user called \"{user}\". Add it as an existing user, or choose another name.");
                var sid = Accounts.Create(user, g.Password);
                undo.Push(() => Accounts.Delete(user));
                Store.SaveUser(new CreatedUser(user, sid.Value, false));
                undo.Push(() => Store.DeleteUser(user));
                logins.Add(new Login(user, g.Password, false));
                return new Grant(ShareMode.NewUser, user, [sid.Value], g.Access);
            }
            case ShareMode.Guest:
            {
                var (user, password, sid) = EnsureGuest(undo);
                logins.Add(new Login(user, password, true));
                return new Grant(ShareMode.Guest, user, [sid.Value], g.Access);
            }
            case ShareMode.Anonymous:
            {
                Anonymous.Enable();
                undo.Push(() => DisableAnonymousIfUnused([]));
                return new Grant(ShareMode.Anonymous, "Everyone", [Anonymous.Everyone.Value, Anonymous.AnonymousLogon.Value], g.Access);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(g));
        }
    }

    /// <summary>Brings the folder's NTFS permissions in line with the grants. Entries the app already manages are adjusted;
    /// missing access is added. Returns the managed entries, and the ones no grant needs any more (to release).</summary>
    static (List<AclChange> Acl, List<AclChange> Released) ApplyFolder(string folder, IReadOnlyList<Grant> grants,
        IReadOnlyList<AclChange> managed, Undo undo)
    {
        var before = FolderAcl.Snapshot(folder);
        undo.Push(() => FolderAcl.Restore(folder, before));
        var acl = new List<AclChange>();
        foreach (var (sid, access) in Planning.FolderAccess(grants))
        {
            if (managed.FirstOrDefault(c => c.Sid == sid) is { } change)
            {
                FolderAcl.Set(folder, change, access);
                acl.Add(change);
            }
            else if (FolderAcl.Grant(folder, new SecurityIdentifier(sid), access) is { } added)
            {
                acl.Add(added);
            }
        }
        var released = managed.Where(c => acl.All(a => a.Sid != c.Sid)).ToList();
        return (acl, released);
    }

    /// <summary>Entries a share no longer needs: handed to another of the app's shares of the same folder that relies on
    /// them, otherwise reverted.</summary>
    static void Release(string shareName, string path, IEnumerable<AclChange> changes, List<string> warnings)
    {
        var others = Store.Shares().Where(s => s.Name != shareName).ToList();
        foreach (var change in changes.Reverse())
        {
            var heir = others.FirstOrDefault(s => s.SamePath(path) && s.AllSids.Contains(change.Sid) && s.Acl.All(a => a.Sid != change.Sid));
            if (heir != null)
            {
                var updated = heir with { Acl = [.. heir.Acl, change] };
                Store.Save(updated);
                others[others.IndexOf(heir)] = updated;
            }
            else
            {
                Try(() => FolderAcl.Revert(path, change), warnings, "Couldn't restore the folder's permissions");
            }
        }
    }

    /// <summary>Deletes users the app created, but only ones no share of the app uses any more.</summary>
    static void DeleteUsers(IEnumerable<string> names, List<string> warnings)
    {
        var shares = Store.Shares();
        var created = Store.Users();
        foreach (var name in names)
        {
            var u = created.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (u == null || shares.Any(s => s.AllSids.Contains(u.Sid))) continue;
            Try(() =>
            {
                Accounts.Delete(u.Name);
                Store.DeleteUser(u.Name);
            }, warnings);
        }
    }

    static void DisableAnonymousIfUnused(List<string> warnings)
    {
        if (!Store.Shares().Any(s => s.Grants.Any(g => g.Kind == ShareMode.Anonymous)))
            Try(Anonymous.Disable, warnings, "Couldn't switch anonymous access off again");
    }

    // ── Guest login ─────────────────────────────────────────────────────────────────────────────

    /// <summary>The shared guest login (created the first time), with its password.</summary>
    static (string Name, string Password, SecurityIdentifier Sid) EnsureGuest(Undo undo)
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
            throw new InvalidOperationException($"A user called \"{GuestUserName}\" already exists but wasn't created by this app. Rename or delete it, or add that user as an existing user instead.");
        var password = Passwords.Generate();
        var sid = Accounts.Create(GuestUserName, password);
        undo.Push(() => Accounts.Delete(GuestUserName));
        Store.SaveUser(new CreatedUser(GuestUserName, sid.Value, true), Dpapi.Protect(password));
        undo.Push(() => Store.DeleteUser(GuestUserName));
        return (GuestUserName, password, sid);
    }

    static CreatedUser? Guest() => Store.Users().FirstOrDefault(u => u.Guest);

    static string? GuestPassword() => Guest() is { } g ? Dpapi.Unprotect(Store.Secret(g.Name)) : null;

    /// <summary>A new password for the guest login. Other PCs that saved the old one have to enter it again.</summary>
    public static string ResetGuestPassword()
    {
        var g = Guest() ?? throw new InvalidOperationException("There's no guest login yet.");
        var pw = Passwords.Generate();
        Accounts.SetPassword(g.Name, pw);
        Store.SaveUser(g, Dpapi.Protect(pw));
        return pw;
    }

    static void Try(Action a, List<string> warnings, string? what = null)
    {
        try { a(); }
        catch (Exception e) { warnings.Add(what == null ? e.Message : $"{what}: {e.Message}"); }
    }
}
