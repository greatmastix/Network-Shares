namespace NetworkShares;

/// <summary>The app's message box: dark, with a title, text, optional check boxes, and an optional Copy button.</summary>
sealed class Dialog : Form
{
    readonly CheckBox[] checks;

    Dialog(string title, string message, string ok, string? cancel, bool danger, string? copy, (string Text, bool Checked)[] options, string? mono)
    {
        Text = title;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.Foreground;
        Font = Theme.Body;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(22, 18, 22, 18);

        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
        flow.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI Semibold", 12f), AutoSize = true, Margin = new Padding(0, 0, 0, 10) });
        if (message.Length > 0)
            flow.Controls.Add(new Label { Text = message, AutoSize = true, MaximumSize = new Size(460, 0), ForeColor = Theme.MutedForeground, Margin = new Padding(0, 0, 0, 10) });
        if (mono != null)
        {
            var box = new Label
            {
                Text = mono, AutoSize = true, Font = Theme.Mono, BackColor = Theme.Card, ForeColor = Theme.Foreground,
                Padding = new Padding(12, 10, 12, 10), Margin = new Padding(0, 0, 0, 10), MinimumSize = new Size(460, 0), MaximumSize = new Size(460, 0),
            };
            flow.Controls.Add(box);
        }
        checks = options.Select(o => Kit.Check(o.Text)).ToArray();
        for (int i = 0; i < checks.Length; i++)
        {
            checks[i].Checked = options[i].Checked;
            checks[i].MaximumSize = new Size(460, 0);
            flow.Controls.Add(checks[i]);
        }

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Margin = new Padding(0, 14, 0, 0), MinimumSize = new Size(460, 0) };
        var okButton = Kit.Button(ok, danger ? Theme.Primary : cancel == null ? Theme.Accent : Theme.Primary);
        okButton.Margin = new Padding(10, 0, 0, 0);
        okButton.MinimumSize = new Size(96, 34);
        okButton.DialogResult = DialogResult.OK;
        buttons.Controls.Add(okButton);
        AcceptButton = okButton;
        if (cancel != null)
        {
            var c = Kit.Button(cancel, Theme.Accent);
            c.Margin = new Padding(10, 0, 0, 0);
            c.MinimumSize = new Size(96, 34);
            c.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(c);
            CancelButton = c;
        }
        else CancelButton = okButton;
        if (copy != null)
        {
            var c = Kit.Button("Copy", Theme.Accent);
            c.Margin = new Padding(10, 0, 0, 0);
            c.MinimumSize = new Size(96, 34);
            c.Click += (_, _) =>
            {
                Kit.Copy(copy);
                c.Text = "Copied ✓";
            };
            buttons.Controls.Add(c);
        }
        flow.Controls.Add(buttons);
        Controls.Add(flow);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
    }

    /// <summary>Returns whether OK was pressed, and the state of each check box.</summary>
    public static (bool Ok, bool[] Checks) Ask(IWin32Window owner, string title, string message, string ok, string? cancel = "Cancel",
        bool danger = false, string? copy = null, string? mono = null, params (string Text, bool Checked)[] options)
    {
        using var d = new Dialog(title, message, ok, cancel, danger, copy, options, mono);
        bool result = d.ShowDialog(owner) == DialogResult.OK;
        return (result, d.checks.Select(c => c.Checked).ToArray());
    }

    public static void Info(IWin32Window owner, string title, string message, string? copy = null, string? mono = null) =>
        Ask(owner, title, message, "OK", null, copy: copy, mono: mono);

    public static void Error(IWin32Window owner, string message) => Ask(owner, "Something went wrong", message, "OK", null);
}
