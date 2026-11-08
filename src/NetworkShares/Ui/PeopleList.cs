using System.Drawing.Drawing2D;

namespace NetworkShares;

/// <summary>The people a share is for, in the editor: one compact row each, with its access (click to switch) and a
/// remove button.</summary>
sealed class PeopleList : Panel
{
    const int RowHeight = 38, MaxRows = 5;

    readonly List<GrantSpec> items = [];
    readonly Label empty = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.MutedForeground,
        Text = "Nobody added yet. The person below gets access when you share; use Add to list several people.",
    };

    public event Action? Changed;

    public PeopleList()
    {
        AutoScroll = true;
        BackColor = Theme.Card;
        Controls.Add(empty);
        Fit();
    }

    public void SetEmptyText(string text) => empty.Text = text;

    public IReadOnlyList<GrantSpec> Items => items;

    public void SetItems(IEnumerable<GrantSpec> specs)
    {
        items.Clear();
        items.AddRange(specs);
        Rebuild();
    }

    public void Add(GrantSpec spec)
    {
        items.Add(spec);
        Rebuild();
    }

    void Rebuild()
    {
        SuspendLayout();
        foreach (var c in Controls.OfType<Row>().ToList())
        {
            Controls.Remove(c);
            c.Dispose();
        }
        empty.Visible = items.Count == 0;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var row = new Row(items[i]) { Dock = DockStyle.Top };
            row.AccessToggled += spec =>
            {
                int at = items.IndexOf(spec);
                items[at] = spec with { Access = spec.Access == Access.Read ? Access.ReadWrite : Access.Read };
                Rebuild();
            };
            row.Removed += spec =>
            {
                items.Remove(spec);
                Rebuild();
            };
            Controls.Add(row);
        }
        ResumeLayout();
        Fit();
        Changed?.Invoke();
    }

    void Fit()
    {
        float s = DeviceDpi / 96f;
        Height = (int)((items.Count == 0 ? 30 : Math.Min(items.Count, MaxRows) * RowHeight) * s);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Fit();
    }

    public static string Title(GrantSpec g) => g.Kind switch
    {
        ShareMode.Guest => "Everyone",
        ShareMode.Anonymous => "Everyone",
        _ => g.User,
    };

    public static string Detail(GrantSpec g) => g.Kind switch
    {
        ShareMode.ExistingUser => g.User.Contains('\\') || g.User.Contains('@') ? "domain account" : "user on this PC",
        ShareMode.NewUser => g.Sid == null ? "new user · created when you save" : "user created by this app",
        ShareMode.Guest => $"shared guest login ({Sharing.GuestUserName})",
        _ => "anonymous · no password",
    };

    sealed class Row : Control
    {
        readonly GrantSpec spec;
        Rectangle accessRect, removeRect;
        int hover = -1;   // -1 none, 0 row, 1 access, 2 remove

        public event Action<GrantSpec>? AccessToggled, Removed;

        public Row(GrantSpec spec)
        {
            this.spec = spec;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Height = RowHeight;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Height = (int)(RowHeight * DeviceDpi / 96f);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = accessRect.Contains(e.Location) ? 1 : removeRect.Contains(e.Location) ? 2 : 0;
            Cursor = h > 0 ? Cursors.Hand : Cursors.Default;
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (accessRect.Contains(e.Location)) AccessToggled?.Invoke(spec);
                else if (removeRect.Contains(e.Location)) Removed?.Invoke(spec);
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            float s = DeviceDpi / 96f;
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 5 * s - 1);
            using (var p = Theme.Rounded(box, 8 * s))
            using (var b = new SolidBrush(hover >= 0 ? Theme.Accent : Theme.Muted))
                g.FillPath(b, p);

            int bh = (int)(24 * s), by = (int)(box.Height / 2 - bh / 2f);
            string accessText = AccessText.Of(spec.Access);
            int aw = Math.Max(TextRenderer.MeasureText("Read & write", Theme.Body).Width, TextRenderer.MeasureText(accessText, Theme.Body).Width) + (int)(20 * s);
            int rw = bh + (int)(4 * s);
            removeRect = new Rectangle(Width - (int)(6 * s) - rw, by, rw, bh);
            accessRect = new Rectangle(removeRect.Left - (int)(6 * s) - aw, by, aw, bh);
            Pill(g, accessRect, accessText, hover == 1 ? Theme.Border : Theme.Background,
                spec.Access == Access.ReadWrite ? Theme.Signal : Theme.Foreground, s);
            Pill(g, removeRect, "✕", hover == 2 ? Theme.Primary : Theme.Background, hover == 2 ? Color.White : Theme.MutedForeground, s);

            int left = (int)(12 * s), right = accessRect.Left - (int)(10 * s);
            string title = Title(spec);
            int tw = Math.Min(TextRenderer.MeasureText(title, Theme.BodyBold).Width, Math.Max(0, right - left));
            TextRenderer.DrawText(g, title, Theme.BodyBold, new Rectangle(left, 0, tw, (int)box.Height), Theme.Foreground,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            int dl = left + tw + (int)(10 * s);
            if (right > dl)
                TextRenderer.DrawText(g, Detail(spec), Theme.Body, new Rectangle(dl, 0, right - dl, (int)box.Height), Theme.MutedForeground,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        static void Pill(Graphics g, Rectangle r, string text, Color back, Color fore, float s)
        {
            using (var p = Theme.Rounded(r, 6 * s))
            using (var b = new SolidBrush(back))
            using (var pen = new Pen(Theme.Border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, text, Theme.Body, r, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
