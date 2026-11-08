using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace NetworkShares;

sealed class MainForm : Form
{
    static readonly string Machine = Environment.MachineName;

    // Header
    readonly Pill pill = new();
    readonly Button fix = Kit.Button("Fix…", Theme.Primary);
    readonly LinkLabel undoLink, cleanLink, aboutLink;

    // New share
    readonly Card newCard = new("New share") { Dock = DockStyle.Top };
    readonly TableLayoutPanel grid = Grid();
    readonly TextBox folder = Kit.Box(), shareName = Kit.Box(), newUser = Kit.Box(), newPassword = Kit.Box(mono: true);
    readonly ComboBox existingUser = Kit.Combo(editable: true);
    readonly Segmented mode = new("Existing user", "New user", "Everyone"), access = new("Read only", "Read & write");
    readonly Label guestInfo = Kit.Muted(), anonWarning = Kit.Muted(), hint = Kit.Muted();
    readonly Button guestCopy = Kit.Small("Copy"), guestReset = Kit.Small("New password");
    readonly CheckBox anonymous = Kit.Check("Anonymous: no password at all (advanced)");
    readonly Button share = Kit.Button("Share folder", Theme.Primary, 150);
    readonly Panel actions = new() { Dock = DockStyle.Top, Height = 54 };
    readonly List<(Func<bool> When, Control[] Controls)> rules = [];

    // Shares
    readonly Card sharesCard = new("Shares made with this app") { Dock = DockStyle.Fill, Margin = Padding.Empty };
    readonly ShareList list = new() { Dock = DockStyle.Fill };
    readonly Label footer = new() { AutoSize = true, ForeColor = Theme.MutedForeground, Location = new Point(0, 10) };

    Health? health;
    bool busy, nameEdited, userEdited, programmatic;

    public MainForm()
    {
        Text = "Network Shares";
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(840, 780);
        MinimumSize = new Size(720, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Foreground;
        Font = Theme.Body;
        Padding = new Padding(20, 12, 20, 6);

        // ── Header: name on the left; fixes, clean up, about and the state on the right ──────────
        var header = new Panel { Dock = DockStyle.Top, Height = 58 };
        header.Controls.Add(new Label { Text = "Network Shares", Font = Theme.Title, AutoSize = true, Location = new Point(0, 12) });
        undoLink = Kit.Link("Undo fixes", UndoFixes);
        cleanLink = Kit.Link("Clean up", CleanUp);
        aboutLink = Kit.Link("About", () => { using var f = new AboutForm(); f.ShowDialog(this); });
        fix.MinimumSize = new Size(0, 28);
        fix.Font = Theme.Body;
        fix.Visible = false;
        undoLink.Visible = false;
        fix.Click += (_, _) => Fix();
        header.Controls.AddRange([pill, aboutLink, cleanLink, undoLink, fix]);
        void Place()
        {
            pill.Location = new Point(header.Width - pill.Width, 14);
            int x = pill.Left - 16;
            foreach (Control c in new Control[] { aboutLink, cleanLink, undoLink, fix })
            {
                if (!c.Visible) continue;
                c.Location = new Point(x - c.Width, 14 + (pill.Height - c.Height) / 2);
                x = c.Left - 14;
            }
        }
        header.Resize += (_, _) => Place();
        pill.Resize += (_, _) => Place();
        fix.VisibleChanged += (_, _) => Place();
        undoLink.VisibleChanged += (_, _) => Place();
        pill.Set("Checking…", Tone.Neutral);

        // ── New share ───────────────────────────────────────────────────────────────────────────
        var browse = Kit.Small("Browse…");
        browse.Click += (_, _) => Browse();
        var generate = Kit.Small("Generate");
        generate.Click += (_, _) => newPassword.Text = Passwords.Generate();
        var copyPassword = Kit.Small("Copy");
        copyPassword.Click += (_, _) => Kit.Copy(newPassword.Text);
        guestCopy.Click += (_, _) => { if (Sharing.Guest() is { } g) Kit.Copy($"{Machine}\\{g.Name}\r\n{Sharing.GuestPassword()}"); };
        guestReset.Click += (_, _) => ResetGuest();

        folder.PlaceholderText = @"e.g. D:\Projects  (or drop a folder onto the window)";
        bool Existing() => mode.SelectedIndex == 0;
        bool New() => mode.SelectedIndex == 1;
        bool Everyone() => mode.SelectedIndex == 2;
        Row("Folder", Kit.Inline(folder, browse));
        Row("Share name", shareName);
        Row("Share with", mode);
        Row("User", existingUser, Existing);
        Row("User name", newUser, New);
        Row("Password", Kit.Inline(newPassword, generate, copyPassword), New);
        Row("Sign in as", Kit.Inline(guestInfo, guestCopy, guestReset), () => Everyone() && !anonymous.Checked);
        Row("", anonymous, Everyone);
        Row("", anonWarning, () => Everyone() && anonymous.Checked);
        Row("Access", access);
        guestInfo.AutoSize = true;
        anonWarning.ForeColor = Theme.Warning;
        anonWarning.MaximumSize = new Size(560, 0);
        anonWarning.Text = "Turns on Windows' built-in Guest account and lets it connect over the network; both are switched "
                           + "back off when the last anonymous share is removed. Other PCs may still refuse: Windows 10 and 11 "
                           + "block guest access by default, and Windows 11 24H2 also requires SMB signing.";

        share.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        share.Click += (_, _) => Share();
        hint.Location = new Point(0, 18);
        hint.ForeColor = Theme.Destructive;
        actions.Controls.Add(share);
        actions.Controls.Add(hint);
        actions.Resize += (_, _) => share.Location = new Point(actions.Width - share.Width, 12);
        newCard.Controls.Add(actions);
        newCard.Controls.Add(grid);
        grid.SizeChanged += (_, _) => FitCard();

        // ── Shares ──────────────────────────────────────────────────────────────────────────────
        list.CopyClicked += r =>
        {
            Kit.Copy($@"\\{Machine}\{r.Name}");
            Flash($@"Copied \\{Machine}\{r.Name}");
        };
        list.RemoveClicked += Remove;
        sharesCard.Controls.Add(list);

        var foot = new Panel { Dock = DockStyle.Bottom, Height = 38 };
        var copyAddress = Kit.Link("Copy", () => Kit.Copy($@"\\{Machine}"));
        foot.Controls.Add(footer);
        foot.Controls.Add(copyAddress);
        footer.SizeChanged += (_, _) => copyAddress.Location = new Point(footer.Right + 10, footer.Top);
        footer.Text = FooterText();
        NetworkChange.NetworkAddressChanged += (_, _) => BeginInvoke(() => footer.Text = FooterText());

        Controls.Add(sharesCard);
        Controls.Add(foot);
        Controls.Add(newCard);
        Controls.Add(header);

        // ── Behaviour ───────────────────────────────────────────────────────────────────────────
        folder.TextChanged += (_, _) => Suggest();
        shareName.TextChanged += (_, _) =>
        {
            if (!programmatic) nameEdited = shareName.Text.Length > 0;
            Suggest();
        };
        newUser.TextChanged += (_, _) => { if (!programmatic) userEdited = newUser.Text.Length > 0; };
        mode.SelectedIndexChanged += (_, _) => UpdateRows();
        anonymous.CheckedChanged += (_, _) => UpdateRows();
        foreach (var c in new Control[] { folder, shareName, newUser, newPassword, existingUser }) c.TextChanged += (_, _) => hint.Text = "";

        existingUser.Items.AddRange([.. Accounts.Selectable()]);
        existingUser.Text = existingUser.Items.Cast<string>().FirstOrDefault(n => n.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase))
                            ?? Environment.UserName;
        newPassword.Text = Passwords.Generate();
        access.SelectedIndex = 0;
        UpdateRows();
        RefreshList();
    }

    // ── Layout helpers ──────────────────────────────────────────────────────────────────────────

    static TableLayoutPanel Grid()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Theme.Card };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }

    void Row(string label, Control c, Func<bool>? when = null)
    {
        var l = new Label { Text = label, AutoSize = true, ForeColor = Theme.MutedForeground, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 16, 8) };
        if (c is TableLayoutPanel) c.Dock = DockStyle.Fill;   // a text box with buttons: take the whole cell
        else c.Anchor = c is Label or CheckBox ? AnchorStyles.Left : AnchorStyles.Left | AnchorStyles.Right;
        c.Margin = c.Margin with { Top = 5, Bottom = 5 };
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(l, 0, grid.RowCount);
        grid.Controls.Add(c, 1, grid.RowCount++);
        if (when != null) rules.Add((when, [l, c]));
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
        if (mode.SelectedIndex == 2) RefreshGuest();
        FitCard();
    }

    void FitCard() => newCard.Height = newCard.Padding.Vertical + grid.Height + actions.Height;

    string FooterText()
    {
        var ips = Addresses();
        return $@"Other PCs connect to   \\{Machine}" + (ips.Count > 0 ? "   ·   " + string.Join("   ·   ", ips.Select(ip => @"\\" + ip)) : "");
    }

    static List<string> Addresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count > 0)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254."))
                .Select(a => a.Address.ToString()).Distinct().Take(2).ToList();
        }
        catch
        {
            return [];
        }
    }

    // ── New share ───────────────────────────────────────────────────────────────────────────────

    void Suggest()
    {
        programmatic = true;
        if (!nameEdited && folder.Text.Trim().Length > 0) shareName.Text = Naming.SuggestShareName(folder.Text);
        if (!userEdited && shareName.Text.Length > 0) newUser.Text = Naming.SuggestUserName(shareName.Text);
        programmatic = false;
    }

    void SetFolder(string path)
    {
        nameEdited = false;
        folder.Text = path;
    }

    void Browse()
    {
        using var d = new FolderBrowserDialog { Description = "Choose the folder to share", UseDescriptionForTitle = true, ShowNewFolderButton = true };
        if (Directory.Exists(folder.Text)) d.InitialDirectory = folder.Text;
        if (d.ShowDialog(this) == DialogResult.OK) SetFolder(d.SelectedPath);
    }

    void RefreshGuest()
    {
        var g = Sharing.Guest();
        var pw = g != null ? Sharing.GuestPassword() : null;
        bool exists = g != null && pw != null;
        guestInfo.Text = exists
            ? $"{Machine}\\{g!.Name}   ·   password  {pw}"
            : $"A shared login, {Sharing.GuestUserName}, is created with a password to pass on.";
        guestInfo.ForeColor = exists ? Theme.Foreground : Theme.MutedForeground;
        guestInfo.Font = exists ? Theme.Mono : Theme.Body;
        guestCopy.Visible = guestReset.Visible = exists;
    }

    ShareMode Mode => mode.SelectedIndex switch
    {
        0 => ShareMode.ExistingUser,
        1 => ShareMode.NewUser,
        _ => anonymous.Checked ? ShareMode.Anonymous : ShareMode.Guest,
    };

    string? Problem()
    {
        var f = folder.Text.Trim();
        if (f.Length == 0) return "Choose a folder to share.";
        if (!Directory.Exists(f)) return "That folder doesn't exist.";
        if (Naming.ShareNameError(shareName.Text) is { } e) return e;
        if (Mode == ShareMode.ExistingUser && existingUser.Text.Trim().Length == 0) return "Choose a user.";
        if (Mode == ShareMode.NewUser)
        {
            if (Naming.UserNameError(newUser.Text.Trim()) is { } u) return u;
            if (newPassword.Text.Length == 0) return "Enter or generate a password.";
        }
        return null;
    }

    async void Share()
    {
        if (busy) return;
        if (Problem() is { } problem)
        {
            hint.Text = problem;
            return;
        }
        if (Mode == ShareMode.Anonymous)
        {
            var (ok, _) = Dialog.Ask(this, "Share anonymously?", anonWarning.Text, "Share anonymously", danger: true);
            if (!ok) return;
        }
        var request = new ShareRequest(folder.Text.Trim(), shareName.Text.Trim(), Mode, access.SelectedIndex == 0 ? Access.Read : Access.ReadWrite,
            Mode == ShareMode.NewUser ? newUser.Text.Trim() : existingUser.Text.Trim(), newPassword.Text);
        var result = await Work("Sharing…", () => Sharing.Create(request));
        if (result == null) return;
        RefreshList();
        CheckHealth();
        ShowResult(result);
        programmatic = true;
        folder.Text = shareName.Text = "";
        programmatic = false;
        nameEdited = userEdited = false;
        if (Mode == ShareMode.NewUser)
        {
            newUser.Text = "";
            newPassword.Text = Passwords.Generate();
        }
        if (mode.SelectedIndex == 2) RefreshGuest();
        existingUser.Items.Clear();
        existingUser.Items.AddRange([.. Accounts.Selectable()]);
    }

    void ShowResult(ShareResult r)
    {
        var path = $@"\\{Machine}\{r.Record.Name}";
        var sb = new StringBuilder();
        sb.AppendLine($"Folder     {path}");
        string message;
        switch (r.Record.Mode)
        {
            case ShareMode.Anonymous:
                sb.AppendLine("Sign-in    none");
                message = "Anyone on the network can open it without a password, if their PC allows guest access.";
                break;
            case ShareMode.ExistingUser:
                sb.AppendLine($"User       {(r.Record.Account.Contains('\\') ? r.Record.Account : $@"{Machine}\{r.Record.Account}")}");
                sb.AppendLine("Password   that user's own password");
                message = "Other PCs can open it with this account. Enter the user name with the PC's name in front, as shown.";
                break;
            default:
                sb.AppendLine($@"User       {Machine}\{r.User}");
                sb.AppendLine($"Password   {r.Password}");
                message = r.Record.Mode == ShareMode.Guest
                    ? "Everyone you give this login to can open it. The same login works for every \"Everyone\" share."
                    : "Pass this login on to whoever should open the folder. The app shows the password only now.";
                break;
        }
        sb.Append($"Access     {r.Record.AccessText}");
        var text = sb.ToString();
        Dialog.Info(this, "Folder shared", message, copy: text, mono: text);
    }

    void ResetGuest()
    {
        var (ok, _) = Dialog.Ask(this, "New guest password?",
            "PCs that saved the current password will have to enter the new one.", "New password");
        if (!ok) return;
        try
        {
            Sharing.ResetGuestPassword();
            RefreshGuest();
        }
        catch (Exception e)
        {
            Dialog.Error(this, e.Message);
        }
    }

    // ── Shares ──────────────────────────────────────────────────────────────────────────────────

    void RefreshList()
    {
        try
        {
            var items = Store.Shares().Select(r => new ShareList.Item(r, SmbShares.PathOf(r.Name) is not { } p || !r.SamePath(p))).ToList();
            list.SetItems(items);
            cleanLink.Visible = items.Count > 0 || Store.Users().Count > 0 || Store.HasFixes();
        }
        catch (Exception e)
        {
            Dialog.Error(this, e.Message);
        }
    }

    async void Remove(ShareRecord r)
    {
        if (busy) return;
        var user = Sharing.DeletableUser(r);
        var options = user == null ? [] : new[] { (user.Guest
            ? $"Also delete the guest login \"{user.Name}\" (no other share uses it)"
            : $"Also delete the user \"{user.Name}\" that was created for this share", true) };
        var (ok, checks) = Dialog.Ask(this, $"Remove \"{r.Name}\"?",
            $"Stops sharing {r.Path} and removes the permissions this app gave it. The folder and its files stay as they are.",
            "Remove", danger: true, options: options);
        if (!ok) return;
        bool deleteUser = checks.Length > 0 && checks[0];
        var warnings = await Work("Removing…", () => Sharing.Remove(r, deleteUser));
        RefreshList();
        if (mode.SelectedIndex == 2) RefreshGuest();
        if (warnings is { Count: > 0 }) Dialog.Info(this, "Removed, with problems", string.Join("\n\n", warnings));
    }

    // ── Fixes and clean-up ──────────────────────────────────────────────────────────────────────

    async void CheckHealth()
    {
        try
        {
            health = await Task.Run(Fixes.Check);
        }
        catch
        {
            return;
        }
        if (IsDisposed) return;
        pill.Set(health.Ok ? "Ready to share" : "Sharing blocked", health.Ok ? Tone.Good : Tone.Warning);
        fix.Visible = !health.Ok;
        undoLink.Visible = health.HasFixes;
        cleanLink.Visible = Store.Shares().Count > 0 || Store.Users().Count > 0 || health.HasFixes;
    }

    async void Fix()
    {
        if (busy || health == null) return;
        var changes = new List<string>();
        if (!health.ServerOk) changes.Add("• Start the Server service (file sharing) and set it to start automatically.");
        if (!health.FirewallOk) changes.Add("• Turn on Windows Firewall's built-in \"File and Printer Sharing (SMB-In)\" rule for the current network.");
        var (ok, _) = Dialog.Ask(this, "Fix sharing?",
            string.Join("\n\n", health.Problems()) + "\n\nThis will:\n" + string.Join("\n", changes)
            + "\n\nNothing else is changed, and \"Undo fixes\" puts it back the way it was.", "Fix");
        if (!ok) return;
        var h = health;
        await Work("Fixing…", () => { Fixes.Apply(h); return h; });
        CheckHealth();
    }

    async void UndoFixes()
    {
        if (busy) return;
        var (ok, _) = Dialog.Ask(this, "Undo fixes?",
            "Puts the firewall rule and the Server service back to how they were before this app changed them. "
            + "Other PCs may no longer reach your shares afterwards.", "Undo fixes");
        if (!ok) return;
        var errors = await Work("Undoing…", Fixes.Undo);
        CheckHealth();
        if (errors is { Count: > 0 }) Dialog.Info(this, "Undone, with problems", string.Join("\n\n", errors));
    }

    async void CleanUp()
    {
        if (busy) return;
        int shares = Store.Shares().Count, users = Store.Users().Count;
        bool fixes = Store.HasFixes();
        var parts = new List<string>();
        if (shares > 0) parts.Add($"• Remove {shares} share{(shares == 1 ? "" : "s")} and the folder permissions the app added");
        if (fixes) parts.Add("• Undo the sharing fixes (firewall rule, Server service)");
        if (parts.Count == 0 && users == 0)
        {
            Dialog.Info(this, "Nothing to clean up", "This app hasn't changed anything on this PC.");
            return;
        }
        var options = users > 0 ? new[] { ($"Also delete the {users} user{(users == 1 ? "" : "s")} this app created", true) } : [];
        var (ok, checks) = Dialog.Ask(this, "Clean up everything?",
            "Puts this PC back the way it was before this app was used:\n" + string.Join("\n", parts)
            + "\n\nUse this before you take the app off a PC. Folders and files are never touched.", "Clean up", danger: true, options: options);
        if (!ok) return;
        bool deleteUsers = checks.Length > 0 && checks[0];
        var warnings = await Work("Cleaning up…", () => Sharing.CleanUp(deleteUsers));
        RefreshList();
        CheckHealth();
        if (mode.SelectedIndex == 2) RefreshGuest();
        Dialog.Info(this, warnings is { Count: > 0 } ? "Cleaned up, with problems" : "All cleaned up",
            warnings is { Count: > 0 } ? string.Join("\n\n", warnings) : "Everything this app changed has been removed.");
    }

    // ── Busy state ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Runs slow work (folder permissions on big trees, the firewall) off the UI thread; errors become a dialog.</summary>
    async Task<T?> Work<T>(string label, Func<T> work) where T : class
    {
        SetBusy(true, label);
        try
        {
            return await Task.Run(work);
        }
        catch (Exception e)
        {
            Dialog.Error(this, e.Message);
            return null;
        }
        finally
        {
            SetBusy(false, label);
        }
    }

    void SetBusy(bool on, string label)
    {
        busy = on;
        UseWaitCursor = on;
        share.Text = on ? label : "Share folder";
        foreach (Control c in new Control[] { grid, share, list, fix, undoLink, cleanLink }) c.Enabled = !on;
    }

    async void Flash(string text)
    {
        footer.Text = text + "  ✓";
        await Task.Delay(1500);
        if (!IsDisposed) footer.Text = FooterText();
    }

    // ── Window ──────────────────────────────────────────────────────────────────────────────────

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FitCard();
        CheckHealth();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
        // The app runs elevated, and Windows doesn't let a normal Explorer window drop onto an elevated one unless the
        // window opts in to these messages. (OLE drag and drop can't cross that line at all, hence WM_DROPFILES.)
        foreach (int msg in new[] { Native.WM_DROPFILES, Native.WM_COPYDATA, Native.WM_COPYGLOBALDATA })
            Native.ChangeWindowMessageFilterEx(Handle, msg, 1 /* MSGFLT_ALLOW */, 0);
        Native.DragAcceptFiles(Handle, true);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_DROPFILES)
        {
            var sb = new StringBuilder(32768);
            if (Native.DragQueryFile(m.WParam, 0, sb, sb.Capacity) > 0)
            {
                var path = sb.ToString();
                if (File.Exists(path)) path = Path.GetDirectoryName(path) ?? path;
                if (Directory.Exists(path) && !busy) SetFolder(path);
            }
            Native.DragFinish(m.WParam);
            return;
        }
        base.WndProc(ref m);
    }
}
