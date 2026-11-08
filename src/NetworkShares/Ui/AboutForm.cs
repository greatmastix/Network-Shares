namespace NetworkShares;

/// <summary>Version, what the app changes, credits, and the licence notices the MIT licences ask to ship with the app.</summary>
sealed class AboutForm : Form
{
    // One line per paragraph: the text box wraps them to its width.
    const string Mit =
        "Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the \"Software\"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:\n\n" +
        "The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.\n\n" +
        "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.";

    static readonly string Credits = $"""
        Network Shares
        Copyright (c) 2026 Mastix. MIT License.

        WHAT IT CHANGES ON THIS PC

        Everything the app changes is recorded in the registry under HKLM\SOFTWARE\NetworkShares (readable by
        administrators only) and can be undone: per share, with Remove, or all at once with Clean up. Nothing is
        installed and no files are written. The key disappears once nothing is recorded.

        BUILT WITH

        .NET and Windows Forms
        Copyright (c) .NET Foundation and Contributors. MIT License.
        https://github.com/dotnet/runtime · https://github.com/dotnet/winforms
        The app includes the .NET runtime, which contains further third-party components:
        https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT

        LICENCE TEXT (MIT)

        Applies to this app and .NET, each with its copyright notice above.

        {Mit}
        """;

    public AboutForm()
    {
        Text = "About Network Shares";
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(620, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.Foreground;
        Font = Theme.Body;
        Padding = new Padding(20, 16, 20, 20);

        var version = typeof(AboutForm).Assembly.GetName().Version;
        var header = new Panel { Dock = DockStyle.Top, Height = 64 };
        header.Controls.Add(new Label { Text = "Network Shares", Font = Theme.Title, AutoSize = true, Location = new Point(0, 0) });
        header.Controls.Add(new Label
        {
            Text = $"Version {version?.ToString(3)}  ·  share folders on the network", AutoSize = true,
            ForeColor = Theme.MutedForeground, Location = new Point(2, 34),
        });

        var card = new Card("Credits") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        card.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical,
            ForeColor = Theme.Foreground, Font = Theme.Body, TabStop = false,
            Text = Credits.ReplaceLineEndings("\r\n"),
        });

        var close = new Button
        {
            Text = "Close", Dock = DockStyle.Bottom, Height = 38, FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent,
            ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 10f), Cursor = Cursors.Hand, DialogResult = DialogResult.OK,
        };
        close.FlatAppearance.BorderSize = 0;
        close.FlatAppearance.MouseOverBackColor = Theme.Border;
        AcceptButton = CancelButton = close;

        Controls.Add(card);
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 14 });
        Controls.Add(close);
        Controls.Add(header);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
    }
}
