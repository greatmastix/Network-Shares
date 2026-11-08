using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace NetworkShares;

sealed class MainForm : Form
{
    static readonly string Machine = Environment.MachineName;

    // Header
    readonly Pill pill = new();
    readonly Button action = Kit.Button("Fix…", Theme.Primary);   // Fix…, or Allow… while there are no admin rights
    readonly LinkLabel undoLink, cleanLink, aboutLink;

    readonly Card newCard = new("New share") { Dock = DockStyle.Top };
    readonly ShareEditor editor = new(editing: false) { Dock = DockStyle.Top };
    readonly Card sharesCard = new("Shares made with this app") { Dock = DockStyle.Fill, Margin = Padding.Empty };
    readonly ShareList list = new() { Dock = DockStyle.Fill };
    readonly Label footer = new() { AutoSize = true, ForeColor = Theme.MutedForeground, Location = new Point(0, 10) };

    IBackend? backend;
    Snapshot? data;
    Health? health;
    bool busy, starting;

    public MainForm()
    {
        Text = "Network Shares";
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(860, 860);
        MinimumSize = new Size(740, 720);
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
        action.MinimumSize = new Size(0, 28);
        action.Font = Theme.Body;
        action.Visible = undoLink.Visible = cleanLink.Visible = false;
        action.Click += (_, _) => { if (backend == null) Connect(); else Fix(); };
        header.Controls.AddRange([pill, aboutLink, cleanLink, undoLink, action]);
        void Place()
        {
            pill.Location = new Point(header.Width - pill.Width, 14);
            int x = pill.Left - 16;
            foreach (Control c in new Control[] { aboutLink, cleanLink, undoLink, action })
            {
                if (!c.Visible) continue;
                c.Location = new Point(x - c.Width, 14 + (pill.Height - c.Height) / 2);
                x = c.Left - 14;
            }
        }
        header.Resize += (_, _) => Place();
        pill.Resize += (_, _) => Place();
        foreach (var c in new Control[] { action, undoLink, cleanLink }) c.VisibleChanged += (_, _) => Place();
        action.SizeChanged += (_, _) => Place();

        // ── New share ───────────────────────────────────────────────────────────────────────────
        editor.Submitted += Share;
        editor.GuestResetRequested += ResetGuest;
        newCard.Controls.Add(editor);
        editor.SizeChanged += (_, _) => newCard.Height = newCard.Padding.Vertical + editor.Height;

        // ── Shares ──────────────────────────────────────────────────────────────────────────────
        list.CopyClicked += r =>
        {
            Kit.Copy($@"\\{Machine}\{r.Name}");
            Flash($@"Copied \\{Machine}\{r.Name}");
        };
        list.EditClicked += Edit;
        list.RemoveClicked += Remove;
        sharesCard.Controls.Add(list);

        var foot = new Panel { Dock = DockStyle.Bottom, Height = 38 };
        var copyAddress = Kit.Link("Copy", () => Kit.Copy($@"\\{Machine}"));
        foot.Controls.Add(footer);
        foot.Controls.Add(copyAddress);
        footer.SizeChanged += (_, _) => copyAddress.Location = new Point(footer.Right + 10, footer.Top);
        footer.Text = FooterText();
        NetworkChange.NetworkAddressChanged += (_, _) => { if (IsHandleCreated) BeginInvoke(() => footer.Text = FooterText()); };

        Controls.Add(sharesCard);
        Controls.Add(foot);
        Controls.Add(newCard);
        Controls.Add(header);

        EnableDrop(this);
        newCard.Enabled = false;
        pill.Set("Starting…", Tone.Neutral);
        list.SetEmptyText("Waiting for administrator rights…");
        list.SetItems([]);
    }

    // ── Admin helper ────────────────────────────────────────────────────────────────────────────

    /// <summary>Gets administrator rights: in this process if it already has them, otherwise from the helper (one UAC prompt).</summary>
    async void Connect()
    {
        if (starting || backend != null) return;
        starting = true;
        SetConnected(false, "Waiting for administrator rights…");
        try
        {
            backend = LocalBackend.Elevated() ? new LocalBackend() : await RemoteBackend.StartAsync();
            SetConnected(true, "");
            await Reload();
        }
        catch (OperationCanceledException)
        {
            SetConnected(false, "This app needs administrator rights to create shares and users. Press Allow… to ask again.");
        }
        catch (Exception e)
        {
            SetConnected(false, e.Message);
        }
        finally
        {
            starting = false;
        }
    }

    void SetConnected(bool on, string message)
    {
        newCard.Enabled = on;
        if (!on)
        {
            pill.Set(starting ? "Starting…" : "Needs admin rights", starting ? Tone.Neutral : Tone.Warning);
            action.Text = "Allow…";
            action.Visible = !starting;
            undoLink.Visible = cleanLink.Visible = false;
            list.SetEmptyText(message);
            list.SetItems([]);
        }
        else
        {
            action.Text = "Fix…";
            action.Visible = false;
            list.SetEmptyText("No shares yet.\nPick a folder above, or drop one onto the window.");
        }
    }

    /// <summary>Fetches the shares, users and guest login; then checks the network side, which can take a moment.</summary>
    async Task Reload()
    {
        if (backend == null) return;
        try
        {
            var b = backend;
            data = await Task.Run(b.Snapshot);
            editor.SetData(data);
            list.SetItems(data.Shares.Select(r => new ShareList.Item(r, data.Missing.Contains(r.Name))).ToList());
            cleanLink.Visible = data.Shares.Count > 0 || data.Users.Count > 0 || data.HasFixes;
            health = await Task.Run(b.Health);
        }
        catch (Exception e)
        {
            Dialog.Error(this, e.Message);
            return;
        }
        if (IsDisposed) return;
        pill.Set(health.Ok ? "Ready to share" : "Sharing blocked", health.Ok ? Tone.Good : Tone.Warning);
        action.Visible = !health.Ok;
        undoLink.Visible = health.HasFixes;
        cleanLink.Visible = data.Shares.Count > 0 || data.Users.Count > 0 || health.HasFixes;
    }

    // ── Shares ──────────────────────────────────────────────────────────────────────────────────

    async void Share()
    {
        if (busy || backend == null) return;
        var (problem, grants) = editor.Collect();
        if (problem != null)
        {
            editor.ShowProblem(problem);
            return;
        }
        if (editor.IsAnonymous(grants) && !Dialog.Ask(this, "Share anonymously?", ShareEditor.AnonymousWarning, "Share anonymously", danger: true).Ok)
            return;
        var spec = new ShareSpec(editor.Folder, editor.ShareName, grants);
        var b = backend;
        var result = await Work("Sharing…", () => b.Create(spec));
        if (result == null) return;
        editor.Reset();
        await Reload();
        Results.Show(this, "Folder shared", result);
    }

    async void Edit(ShareRecord r)
    {
        if (busy || backend == null || data == null) return;
        using var f = new EditForm(backend, data, r);
        if (f.ShowDialog(this) == DialogResult.OK) await Reload();
    }

    async void Remove(ShareRecord r)
    {
        if (busy || backend == null || data == null) return;
        var orphans = Planning.OrphanedUsers(data.Shares, data.Users, r.Name, r.AllSids);
        var (ok, checks) = Dialog.Ask(this, $"Remove \"{r.Name}\"?",
            $"Stops sharing {r.Path} and removes the permissions this app gave it. The folder and its files stay as they are.",
            "Remove", danger: true, options: [.. orphans.Select(u => (u.Guest
                ? $"Also delete the guest login \"{u.Name}\" (no other share uses it)"
                : $"Also delete the user \"{u.Name}\" (created by this app, no other share uses it)", true))]);
        if (!ok) return;
        var spec = new RemoveSpec(r.Name, orphans.Where((_, i) => checks[i]).Select(u => u.Name).ToList());
        var b = backend;
        var warnings = await Work("Removing…", () => b.Remove(spec));
        await Reload();
        if (warnings is { Count: > 0 }) Dialog.Info(this, "Removed, with problems", string.Join("\n\n", warnings));
    }

    async void ResetGuest()
    {
        if (busy || backend == null) return;
        if (!Dialog.Ask(this, "New guest password?", "PCs that saved the current password will have to enter the new one.", "New password").Ok)
            return;
        var b = backend;
        await Work("Working…", b.ResetGuest);
        await Reload();
    }

    // ── Fixes and clean-up ──────────────────────────────────────────────────────────────────────

    async void Fix()
    {
        if (busy || backend == null || health == null) return;
        var changes = new List<string>();
        if (!health.ServerOk) changes.Add("• Start the Server service (file sharing) and set it to start automatically.");
        if (!health.FirewallOk) changes.Add("• Turn on Windows Firewall's built-in \"File and Printer Sharing (SMB-In)\" rule for the current network.");
        var (ok, _) = Dialog.Ask(this, "Fix sharing?",
            string.Join("\n\n", health.Problems()) + "\n\nThis will:\n" + string.Join("\n", changes)
            + "\n\nNothing else is changed, and \"Undo fixes\" puts it back the way it was.", "Fix");
        if (!ok) return;
        var b = backend;
        await Work("Fixing…", () => { b.Fix(); return b; });
        await Reload();
    }

    async void UndoFixes()
    {
        if (busy || backend == null) return;
        var (ok, _) = Dialog.Ask(this, "Undo fixes?",
            "Puts the firewall rule and the Server service back to how they were before this app changed them. "
            + "Other PCs may no longer reach your shares afterwards.", "Undo fixes");
        if (!ok) return;
        var b = backend;
        var errors = await Work("Undoing…", b.UndoFixes);
        await Reload();
        if (errors is { Count: > 0 }) Dialog.Info(this, "Undone, with problems", string.Join("\n\n", errors));
    }

    async void CleanUp()
    {
        if (busy || backend == null || data == null) return;
        int shares = data.Shares.Count, users = data.Users.Count;
        bool fixes = health?.HasFixes ?? data.HasFixes;
        var parts = new List<string>();
        if (shares > 0) parts.Add($"• Remove {shares} share{(shares == 1 ? "" : "s")} and the folder permissions the app added");
        if (fixes) parts.Add("• Undo the sharing fixes (firewall rule, Server service)");
        if (parts.Count == 0 && users == 0)
        {
            Dialog.Info(this, "Nothing to clean up", "This app hasn't changed anything on this PC.");
            return;
        }
        var (ok, checks) = Dialog.Ask(this, "Clean up everything?",
            "Puts this PC back the way it was before this app was used:\n" + string.Join("\n", parts)
            + "\n\nUse this before you take the app off a PC. Folders and files are never touched.", "Clean up", danger: true,
            options: users > 0 ? [($"Also delete the {users} user{(users == 1 ? "" : "s")} this app created", true)] : []);
        if (!ok) return;
        bool deleteUsers = checks.Length > 0 && checks[0];
        var b = backend;
        var warnings = await Work("Cleaning up…", () => b.CleanUp(deleteUsers));
        await Reload();
        if (warnings == null) return;
        Dialog.Info(this, warnings.Count > 0 ? "Cleaned up, with problems" : "All cleaned up",
            warnings.Count > 0 ? string.Join("\n\n", warnings) : "Everything this app changed has been removed.");
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
        editor.SetBusy(on, label);
        foreach (Control c in new Control[] { list, action, undoLink, cleanLink }) c.Enabled = !on;
    }

    // ── Footer ──────────────────────────────────────────────────────────────────────────────────

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

    async void Flash(string text)
    {
        footer.Text = text + "  ✓";
        await Task.Delay(1500);
        if (!IsDisposed) footer.Text = FooterText();
    }

    // ── Drag and drop ───────────────────────────────────────────────────────────────────────────

    /// <summary>Accepts a folder (or a file: its folder) dropped anywhere on the window. Every control registers, so the
    /// drop works wherever the mouse is.</summary>
    void EnableDrop(Control c)
    {
        c.AllowDrop = true;
        c.DragEnter += OnDragEnter;
        c.DragOver += OnDragEnter;
        c.DragDrop += OnDragDrop;
        foreach (Control child in c.Controls) EnableDrop(child);
        c.ControlAdded += (_, e) => { if (e.Control != null) EnableDrop(e.Control); };
    }

    static string? DroppedFolder(IDataObject? data)
    {
        if (data?.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return null;
        var path = paths[0];
        if (File.Exists(path)) path = Path.GetDirectoryName(path) ?? path;
        return Directory.Exists(path) ? path : null;
    }

    void OnDragEnter(object? sender, DragEventArgs e) =>
        e.Effect = !busy && newCard.Enabled && DroppedFolder(e.Data) != null ? DragDropEffects.Copy : DragDropEffects.None;

    void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (!busy && newCard.Enabled && DroppedFolder(e.Data) is { } path) editor.SetFolder(path);
    }

    // ── Window ──────────────────────────────────────────────────────────────────────────────────

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        newCard.Height = newCard.Padding.Vertical + editor.Height;
        Connect();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
        // When started as administrator anyway, Explorer can't drop through OLE into this window; these messages let the
        // older WM_DROPFILES route through where Windows allows it.
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
                if (Directory.Exists(path) && !busy && newCard.Enabled) editor.SetFolder(path);
            }
            Native.DragFinish(m.WParam);
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        backend?.Dispose();
        base.OnFormClosed(e);
    }
}
