using System.Drawing.Drawing2D;

namespace NetworkShares;

/// <summary>The shares the app made: one rounded row each, with Copy path and Remove on the right.</summary>
sealed class ShareList : Panel
{
    public sealed record Item(ShareRecord Record, bool Missing);

    readonly Label empty = new()
    {
        Text = "No shares yet.\nPick a folder above, or drop one onto the window.", Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.MutedForeground,
    };

    public event Action<ShareRecord>? CopyClicked, RemoveClicked;

    public ShareList()
    {
        AutoScroll = true;
        BackColor = Theme.Card;
        Controls.Add(empty);
    }

    public void SetItems(IReadOnlyList<Item> items)
    {
        SuspendLayout();
        foreach (var c in Controls.OfType<Row>().ToList())
        {
            Controls.Remove(c);
            c.Dispose();
        }
        empty.Visible = items.Count == 0;
        // Dock = Top stacks the last added at the top, so add in reverse.
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var row = new Row(items[i]) { Dock = DockStyle.Top };
            row.CopyClicked += r => CopyClicked?.Invoke(r);
            row.RemoveClicked += r => RemoveClicked?.Invoke(r);
            Controls.Add(row);
        }
        ResumeLayout();
    }

    sealed class Row : Control
    {
        readonly Item item;
        Rectangle copyRect, removeRect;
        int hover = -1;   // -1 none, 0 row, 1 copy, 2 remove

        public event Action<ShareRecord>? CopyClicked, RemoveClicked;

        public Row(Item item)
        {
            this.item = item;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Height = 66;
            Padding = new Padding(0, 0, 0, 8);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Height = (int)(66 * DeviceDpi / 96f);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = copyRect.Contains(e.Location) ? 1 : removeRect.Contains(e.Location) ? 2 : 0;
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
                if (copyRect.Contains(e.Location)) CopyClicked?.Invoke(item.Record);
                else if (removeRect.Contains(e.Location)) RemoveClicked?.Invoke(item.Record);
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            float s = DeviceDpi / 96f;
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 8 * s - 1);
            using (var p = Theme.Rounded(box, 10 * s))
            using (var b = new SolidBrush(hover >= 0 ? Theme.Accent : Theme.Muted))
            using (var pen = new Pen(Theme.Border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }

            // Status dot: green while the share exists, amber if it was removed outside the app.
            float cy = box.Height / 2;
            using (var dot = new SolidBrush(item.Missing ? Theme.Warning : Theme.Good)) g.FillEllipse(dot, 14 * s, cy - 4 * s, 8 * s, 8 * s);

            // Buttons, right to left.
            var (copyText, removeText) = ("Copy path", "Remove");
            int bh = (int)(28 * s), by = (int)(cy - bh / 2f);
            int rw = TextRenderer.MeasureText(removeText, Theme.Body).Width + (int)(22 * s);
            int cw = TextRenderer.MeasureText(copyText, Theme.Body).Width + (int)(22 * s);
            removeRect = new Rectangle(Width - (int)(12 * s) - rw, by, rw, bh);
            copyRect = new Rectangle(removeRect.Left - (int)(8 * s) - cw, by, cw, bh);
            if (item.Missing) copyRect = Rectangle.Empty;
            else Pill(g, copyRect, copyText, hover == 1 ? Theme.Border : Theme.Background, Theme.Foreground, s);
            Pill(g, removeRect, removeText, hover == 2 ? Theme.Primary : Theme.Background, hover == 2 ? Color.White : Theme.Destructive, s);

            // Who and access, then name and path.
            int left = (int)(34 * s), right = (item.Missing ? removeRect.Left : copyRect.Left) - (int)(16 * s);
            var r = item.Record;
            string meta = $"{r.Who}  ·  {r.AccessText}";
            int metaW = Math.Min(TextRenderer.MeasureText(meta, Theme.Body).Width, Math.Max(0, (right - left) / 2));
            TextRenderer.DrawText(g, meta, Theme.Body, new Rectangle(right - metaW, 0, metaW, (int)box.Height), Theme.MutedForeground,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            int textW = right - metaW - left - (int)(12 * s);
            string title = r.Name + (item.Missing ? "   — no longer shared" : "");
            TextRenderer.DrawText(g, title, Theme.RowTitle, new Rectangle(left, (int)(9 * s), textW, (int)(22 * s)),
                item.Missing ? Theme.Warning : Theme.Foreground, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, r.Path, Theme.Body, new Rectangle(left, (int)(31 * s), textW, (int)(20 * s)), Theme.MutedForeground,
                TextFormatFlags.PathEllipsis | TextFormatFlags.SingleLine);
        }

        static void Pill(Graphics g, Rectangle r, string text, Color back, Color fore, float s)
        {
            using (var p = Theme.Rounded(r, 7 * s))
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
