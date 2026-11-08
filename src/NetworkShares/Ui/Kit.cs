namespace NetworkShares;

/// <summary>Themed standard controls, as in the other apps of this family.</summary>
static class Kit
{
    public static Button Button(string text, Color back, int width = 0)
    {
        var b = new Button
        {
            Text = text, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = Color.White,
            Font = Theme.BodyBold, Cursor = Cursors.Hand, Margin = new Padding(0, 4, 10, 4), UseVisualStyleBackColor = false,
        };
        if (width > 0) b.Width = width;
        else
        {
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(10, 0, 10, 0);
            b.MinimumSize = new Size(0, 34);
        }
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = back == Theme.Primary ? Theme.Signal : Theme.Border;
        return b;
    }

    /// <summary>A small grey button that sits next to a text box.</summary>
    public static Button Small(string text)
    {
        var b = Button(text, Theme.Accent);
        b.Font = Theme.Body;
        b.MinimumSize = new Size(0, 28);
        b.Height = 28;
        b.Margin = new Padding(6, 0, 0, 0);
        return b;
    }

    public static TextBox Box(bool mono = false) => new()
    {
        BackColor = Theme.Muted, ForeColor = Theme.Foreground, BorderStyle = BorderStyle.FixedSingle, Font = mono ? Theme.Mono : Theme.Body,
    };

    public static ComboBox Combo(bool editable = false) => new()
    {
        DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Muted, ForeColor = Theme.Foreground,
    };

    public static CheckBox Check(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = Theme.Foreground, Margin = new Padding(0, 6, 0, 2), Cursor = Cursors.Hand,
    };

    public static Label Muted(string text = "") => new()
    {
        Text = text, AutoSize = true, ForeColor = Theme.MutedForeground, Margin = new Padding(0, 4, 0, 4),
    };

    public static LinkLabel Link(string text, Action onClick)
    {
        var l = new LinkLabel
        {
            Text = text, AutoSize = true, LinkColor = Theme.MutedForeground, ActiveLinkColor = Theme.Signal,
            VisitedLinkColor = Theme.MutedForeground, LinkBehavior = LinkBehavior.HoverUnderline,
        };
        l.LinkClicked += (_, _) => onClick();
        return l;
    }

    /// <summary>A control that fills the row, with buttons on its right.</summary>
    public static Control Inline(Control main, params Control[] extras)
    {
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = extras.Length + 1, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Card };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        main.Margin = Padding.Empty;
        t.Controls.Add(main, 0, 0);
        for (int i = 0; i < extras.Length; i++)
        {
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            extras[i].Anchor = AnchorStyles.Left;
            t.Controls.Add(extras[i], i + 1, 0);
        }
        return t;
    }

    public static void Copy(string text)
    {
        try { Clipboard.SetText(text); } catch { /* the clipboard can be locked by another app for a moment */ }
    }
}
