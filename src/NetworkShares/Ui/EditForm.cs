using System.Text;

namespace NetworkShares;

/// <summary>Edit a share: rename it, add or remove people, change their access.</summary>
sealed class EditForm : Form
{
    readonly IBackend backend;
    readonly ShareRecord share;
    readonly Snapshot data;
    readonly ShareEditor editor = new(editing: true) { Dock = DockStyle.Top };
    readonly Card card = new("Edit share") { Dock = DockStyle.Top, Margin = Padding.Empty };
    bool busy;

    public EditForm(IBackend backend, Snapshot data, ShareRecord share)
    {
        (this.backend, this.data, this.share) = (backend, data, share);
        Text = $"Edit \"{share.Name}\"";
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.Foreground;
        Font = Theme.Body;
        Padding = new Padding(20, 16, 20, 4);
        ClientSize = new Size(700, 600);

        editor.SetData(data);
        editor.Load(share);
        editor.Submitted += Save;
        editor.GuestResetRequested += () => Dialog.Info(this, "Guest password", "Change the guest password from the main window.");
        card.Controls.Add(editor);
        Controls.Add(card);

        editor.SizeChanged += (_, _) => Fit();
    }

    void Fit()
    {
        card.Height = card.Padding.Vertical + editor.Height;
        ClientSize = new Size(ClientSize.Width, Padding.Vertical + card.Height + 12);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Fit();
    }

    async void Save()
    {
        if (busy) return;
        var (problem, grants) = editor.Collect();
        if (problem != null)
        {
            editor.ShowProblem(problem);
            return;
        }
        if (editor.IsAnonymous(grants) && !Dialog.Ask(this, "Share anonymously?", ShareEditor.AnonymousWarning, "Share anonymously", danger: true).Ok)
            return;

        // Users the app created who lose their last share with this edit: offer to delete them (ticked).
        var keptSids = grants.Where(g => g.Sid != null).Select(g => g.Sid!).ToHashSet();
        var removed = share.Grants.Where(g => !keptSids.Contains(g.Sid)).SelectMany(g => g.Sids);
        var orphans = Planning.OrphanedUsers(data.Shares, data.Users, share.Name, removed);
        var delete = new List<string>();
        if (orphans.Count > 0)
        {
            var (ok, checks) = Dialog.Ask(this, "Save changes?", "These users were created by this app and no other share uses them.",
                "Save", options: [.. orphans.Select(u => (u.Guest ? $"Delete the guest login \"{u.Name}\"" : $"Delete the user \"{u.Name}\"", true))]);
            if (!ok) return;
            delete.AddRange(orphans.Where((_, i) => checks[i]).Select(u => u.Name));
        }

        var spec = new UpdateSpec(share.Name, editor.ShareName, grants, delete);
        busy = true;
        editor.SetBusy(true, "Saving…");
        UseWaitCursor = true;
        ShareResult? result = null;
        try
        {
            result = await Task.Run(() => backend.Update(spec));
        }
        catch (Exception ex)
        {
            Dialog.Error(this, ex.Message);
        }
        finally
        {
            busy = false;
            UseWaitCursor = false;
            editor.SetBusy(false, "");
        }
        if (result == null) return;
        if (result.Logins.Count > 0) Results.Show(this, "Share saved", result);
        if (result.Warnings.Count > 0) Dialog.Info(this, "Saved, with problems", string.Join("\n\n", result.Warnings));
        DialogResult = DialogResult.OK;
    }
}

/// <summary>What to tell the people a share is for.</summary>
static class Results
{
    static readonly string Machine = Environment.MachineName;

    public static string Text(ShareRecord r, IReadOnlyList<Login> logins)
    {
        var sb = new StringBuilder();
        sb.AppendLine($@"Folder    \\{Machine}\{r.Name}");
        foreach (var g in r.Grants)
        {
            sb.AppendLine();
            var login = logins.FirstOrDefault(l => l.User.Equals(g.Account, StringComparison.OrdinalIgnoreCase));
            switch (g.Kind)
            {
                case ShareMode.Anonymous:
                    sb.AppendLine("Everyone  no sign-in (anonymous)");
                    break;
                case ShareMode.ExistingUser:
                    sb.AppendLine($"User      {(g.Account.Contains('\\') ? g.Account : $@"{Machine}\{g.Account}")}");
                    sb.AppendLine("Password  that user's own password");
                    break;
                default:
                    sb.AppendLine($"{(g.Kind == ShareMode.Guest ? "Everyone" : "User"),-10}{Machine}\\{g.Account}");
                    sb.AppendLine($"Password  {login?.Password ?? "(the one given when it was created)"}");
                    break;
            }
            sb.AppendLine($"Access    {AccessText.Of(g.Access)}");
        }
        return sb.ToString().TrimEnd();
    }

    public static void Show(IWin32Window owner, string title, ShareResult result)
    {
        var text = Text(result.Record, result.Logins);
        bool newUsers = result.Logins.Any(l => !l.Guest);
        Dialog.Info(owner, title,
            "Other PCs open the folder with these sign-ins. Enter the user name with the PC's name in front, as shown."
            + (newUsers ? " New users' passwords are only shown now." : ""), copy: text, mono: text);
    }
}
