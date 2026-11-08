namespace NetworkShares;

/// <summary>
/// Folder, share name and the people with access, plus a row to add a person: the "New share" card, and the Edit window.
/// With nobody in the list, the person in the add row is used, so sharing with one person stays a single click.
/// </summary>
sealed class ShareEditor : Panel
{
    static readonly string Machine = Environment.MachineName;

    public readonly bool Editing;
    readonly TableLayoutPanel grid = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Theme.Card };
    readonly TextBox folder = Kit.Box(), shareName = Kit.Box(), newUser = Kit.Box(), newPassword = Kit.Box(mono: true);
    readonly Label folderFixed = new() { AutoSize = true, ForeColor = Theme.Foreground };
    readonly ComboBox existingUser = Kit.Combo(editable: true);
    readonly Segmented kind = new("Existing user", "New user", "Everyone"), access = new("Read only", "Read & write");
    readonly PeopleList people = new();
    readonly Label guestInfo = Kit.Muted(), anonWarning = Kit.Muted(), hint = Kit.Muted();
    readonly Button guestCopy = Kit.Small("Copy"), guestReset = Kit.Small("New password"), add = Kit.Small("+  Add");
    readonly CheckBox anonymous = Kit.Check("Anonymous: no password at all (advanced)");
    readonly Panel actions = new() { Dock = DockStyle.Top, Height = 54 };
    readonly List<(Func<bool> When, Control[] Controls)> rules = [];
    public readonly Button Primary;
    Snapshot? data;
    bool nameEdited, userEdited, programmatic;

    public event Action? Submitted, GuestResetRequested;

    public const string AnonymousWarning =
        "Turns on Windows' built-in Guest account and lets it connect over the network; both are switched back off when the "
        + "last anonymous share is removed. Other PCs may still refuse: Windows 10 and 11 block guest access by default, and "
        + "Windows 11 24H2 also requires SMB signing.";

    public ShareEditor(bool editing)
    {
        Editing = editing;
        BackColor = Theme.Card;
        Primary = Kit.Button(editing ? "Save changes" : "Share folder", Theme.Primary, 150);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var browse = Kit.Small("Browse…");
        browse.Click += (_, _) => Browse();
        var generate = Kit.Small("Generate");
        generate.Click += (_, _) => newPassword.Text = Passwords.Generate();
        var copyPassword = Kit.Small("Copy");
        copyPassword.Click += (_, _) => Kit.Copy(newPassword.Text);
        guestCopy.Click += (_, _) => { if (data?.GuestName is { } g) Kit.Copy($"{Machine}\\{g}\r\n{data.GuestPassword}"); };
        guestReset.Click += (_, _) => GuestResetRequested?.Invoke();
        add.Click += (_, _) => AddPerson();

        folder.PlaceholderText = @"e.g. D:\Projects  (or drop a folder onto the window)";
        bool Existing() => kind.SelectedIndex == 0;
        bool New() => kind.SelectedIndex == 1;
        bool Everyone() => kind.SelectedIndex == 2;
        Row("Folder", editing ? folderFixed : Kit.Inline(folder, browse));
        Row("Share name", shareName);
        Span(new SectionTitle("People with access"));
        Span(people);
        Span(new SectionTitle("Add a person"));
        Row("Who", kind);
        Row("User", existingUser, Existing);
        Row("User name", newUser, New);
        Row("Password", Kit.Inline(newPassword, generate, copyPassword), New);
        Row("Sign in as", Kit.Inline(guestInfo, guestCopy, guestReset), () => Everyone() && !anonymous.Checked);
        Row("", anonymous, Everyone);
        Row("", anonWarning, () => Everyone() && anonymous.Checked);
        Row("Access", Kit.Inline(access, add));
        anonWarning.ForeColor = Theme.Warning;
        anonWarning.MaximumSize = new Size(560, 0);
        anonWarning.Text = AnonymousWarning;
        people.SetEmptyText(editing
            ? "Nobody has access. Add at least one person below."
            : "Nobody added yet. The person below gets access when you share; use Add to list several people.");

        Primary.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Primary.Click += (_, _) => Submitted?.Invoke();
        hint.Location = new Point(0, 18);
        hint.ForeColor = Theme.Destructive;
        actions.Controls.Add(Primary);
        actions.Controls.Add(hint);
        actions.Resize += (_, _) =>
        {
            Primary.Location = new Point(actions.Width - Primary.Width, 12);
            hint.MaximumSize = new Size(Math.Max(100, Primary.Left - 16), 0);
        };
        Controls.Add(actions);
        Controls.Add(grid);
        grid.SizeChanged += (_, _) => Fit();

        folder.TextChanged += (_, _) => Suggest();
        shareName.TextChanged += (_, _) =>
        {
            if (!programmatic) nameEdited = shareName.Text.Length > 0;
            Suggest();
        };
        newUser.TextChanged += (_, _) => { if (!programmatic) userEdited = newUser.Text.Length > 0; };
        kind.SelectedIndexChanged += (_, _) => UpdateRows();
        anonymous.CheckedChanged += (_, _) => UpdateRows();
        people.Changed += () => hint.Text = "";
        foreach (var c in new Control[] { folder, shareName, newUser, newPassword, existingUser }) c.TextChanged += (_, _) => hint.Text = "";

        newPassword.Text = Passwords.Generate();
        UpdateRows();
    }

    void Row(string label, Control c, Func<bool>? when = null)
    {
        var l = new Label { Text = label, AutoSize = true, ForeColor = Theme.MutedForeground, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 16, 8) };
        if (c is TableLayoutPanel) c.Dock = DockStyle.Fill;   // a control with buttons: take the whole cell
        else c.Anchor = c is Label or CheckBox ? AnchorStyles.Left : AnchorStyles.Left | AnchorStyles.Right;
        c.Margin = c.Margin with { Top = 5, Bottom = 5 };
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(l, 0, grid.RowCount);
        grid.Controls.Add(c, 1, grid.RowCount++);
        if (when != null) rules.Add((when, [l, c]));
    }

    void Span(Control c)
    {
        c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        if (c is PeopleList) c.Margin = new Padding(0, 4, 0, 4);
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(c, 0, grid.RowCount++);
        grid.SetColumnSpan(c, 2);
    }

    void UpdateRows()
    {
        grid.SuspendLayout();
        foreach (var (when, controls) in rules)
        {
            bool show = when();
            foreach (var c in controls) c.Visible = show;
        }
        grid.ResumeLayout();
        Fit();
    }

    void Fit() => Height = grid.Height + actions.Height;

    // ── Data ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The users to pick from and the guest login, after every change.</summary>
    public void SetData(Snapshot s)
    {
        data = s;
        var typed = existingUser.Text;
        existingUser.Items.Clear();
        existingUser.Items.AddRange([.. s.LocalUsers]);
        existingUser.Text = typed.Length > 0 ? typed
            : s.LocalUsers.FirstOrDefault(n => n.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase)) ?? Environment.UserName;

        bool exists = s.GuestName != null && s.GuestPassword != null;
        guestInfo.Text = exists
            ? $"{Machine}\\{s.GuestName}   ·   password  {s.GuestPassword}"
            : $"A shared login, {Sharing.GuestUserName}, is created with a password to pass on.";
        guestInfo.ForeColor = exists ? Theme.Foreground : Theme.MutedForeground;
        guestInfo.Font = exists ? Theme.Mono : Theme.Body;
        guestCopy.Visible = guestReset.Visible = exists;
    }

    /// <summary>Edit mode: the share as it is now.</summary>
    public void Load(ShareRecord r)
    {
        folderFixed.Text = r.Path;
        programmatic = true;
        shareName.Text = r.Name;
        programmatic = false;
        nameEdited = true;
        people.SetItems(r.Grants.Select(g => new GrantSpec(g.Kind, g.Account, null, g.Access, g.Sid)));
    }

    public string Folder => folder.Text.Trim();
    public string ShareName => shareName.Text.Trim();

    public void SetFolder(string path)
    {
        if (Editing) return;
        nameEdited = false;
        folder.Text = path;
    }

    public void ShowProblem(string text) => hint.Text = text;

    public void SetBusy(bool on, string label)
    {
        grid.Enabled = !on;
        Primary.Enabled = !on;
        Primary.Text = on ? label : Editing ? "Save changes" : "Share folder";
    }

    /// <summary>After sharing: an empty form for the next one.</summary>
    public void Reset()
    {
        programmatic = true;
        folder.Text = shareName.Text = newUser.Text = "";
        programmatic = false;
        nameEdited = userEdited = false;
        newPassword.Text = Passwords.Generate();
        people.SetItems([]);
        hint.Text = "";
    }

    // ── People ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>The person described by the add row, or why it isn't complete.</summary>
    (string? Problem, GrantSpec? Spec) Pending()
    {
        var a = access.SelectedIndex == 0 ? Access.Read : Access.ReadWrite;
        GrantSpec spec;
        switch (kind.SelectedIndex)
        {
            case 0:
                var user = existingUser.Text.Trim();
                if (user.Length == 0) return ("Choose a user.", null);
                spec = new GrantSpec(ShareMode.ExistingUser, user, null, a);
                break;
            case 1:
                var name = newUser.Text.Trim();
                if (Naming.UserNameError(name) is { } e) return (e, null);
                if (newPassword.Text.Length == 0) return ("Enter or generate a password.", null);
                if (data?.LocalUsers.Contains(name, StringComparer.OrdinalIgnoreCase) == true)
                    return ($"There's already a user called \"{name}\". Add it as an existing user.", null);
                spec = new GrantSpec(ShareMode.NewUser, name, newPassword.Text, a);
                break;
            default:
                spec = new GrantSpec(anonymous.Checked ? ShareMode.Anonymous : ShareMode.Guest, "Everyone", null, a);
                break;
        }
        if (spec.Kind is ShareMode.Guest or ShareMode.Anonymous && people.Items.Any(p => p.Kind is ShareMode.Guest or ShareMode.Anonymous))
            return ("Everyone is already on the list.", null);
        if (spec.Kind is ShareMode.ExistingUser or ShareMode.NewUser
            && people.Items.Any(p => p.Kind is ShareMode.ExistingUser or ShareMode.NewUser && SameUser(p.User, spec.User)))
            return ($"{spec.User} is already on the list.", null);
        return (null, spec);
    }

    static bool SameUser(string a, string b)
    {
        static string Bare(string n) => n.StartsWith(Machine + "\\", StringComparison.OrdinalIgnoreCase) ? n[(Machine.Length + 1)..] : n;
        return string.Equals(Bare(a.Trim()), Bare(b.Trim()), StringComparison.OrdinalIgnoreCase);
    }

    void AddPerson()
    {
        var (problem, spec) = Pending();
        if (problem != null)
        {
            hint.Text = problem;
            return;
        }
        people.Add(spec!);
        if (spec!.Kind == ShareMode.NewUser)
        {
            programmatic = true;
            newUser.Text = "";
            programmatic = false;
            userEdited = true;   // don't suggest the same name again
            newPassword.Text = Passwords.Generate();
        }
        if (spec.Kind == ShareMode.ExistingUser) existingUser.Text = "";
    }

    /// <summary>What to save: the list, or (if the list is empty and this is a new share) the person in the add row.</summary>
    public (string? Problem, IReadOnlyList<GrantSpec> Grants) Collect()
    {
        if (!Editing)
        {
            if (Folder.Length == 0) return ("Choose a folder to share.", []);
            if (!Directory.Exists(Folder)) return ("That folder doesn't exist.", []);
        }
        if (Naming.ShareNameError(ShareName) is { } e) return (e, []);
        if (people.Items.Count > 0) return (null, people.Items);
        if (Editing) return ("Add at least one person.", []);
        var (problem, spec) = Pending();
        return problem != null ? (problem, []) : (null, [spec!]);
    }

    public bool IsAnonymous(IReadOnlyList<GrantSpec> grants) => grants.Any(g => g.Kind == ShareMode.Anonymous && g.Sid == null);

    // ── Folder ──────────────────────────────────────────────────────────────────────────────────

    void Suggest()
    {
        programmatic = true;
        if (!nameEdited && folder.Text.Trim().Length > 0) shareName.Text = Naming.SuggestShareName(folder.Text);
        if (!userEdited && shareName.Text.Length > 0) newUser.Text = Naming.SuggestUserName(shareName.Text);
        programmatic = false;
    }

    void Browse()
    {
        using var d = new FolderBrowserDialog { Description = "Choose the folder to share", UseDescriptionForTitle = true, ShowNewFolderButton = true };
        if (Directory.Exists(folder.Text)) d.InitialDirectory = folder.Text;
        if (d.ShowDialog(FindForm()) == DialogResult.OK) SetFolder(d.SelectedPath);
    }
}
