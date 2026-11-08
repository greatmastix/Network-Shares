using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace NetworkShares;

/// <summary>The MediaMTXUI dark palette: near-black surfaces, one red accent.</summary>
static class Theme
{
    public static readonly Color Background = Hex(0x0c0c0e), Card = Hex(0x151517), Muted = Hex(0x1c1c1f), Accent = Hex(0x252529),
        Border = Hex(0x2a2a2f), Foreground = Hex(0xf3f3f4), MutedForeground = Hex(0xb3b3ba), Primary = Hex(0xd6323d),
        Signal = Hex(0xef4b55), Good = Hex(0x0ca30c), Warning = Hex(0xfab219), Destructive = Hex(0xff5f68);

    public static readonly Font Body = new("Segoe UI", 9.75f), Small = new("Segoe UI Semibold", 8.25f),
        Title = new("Segoe UI Semibold", 14f), BodyBold = new("Segoe UI Semibold", 9.75f), RowTitle = new("Segoe UI Semibold", 10.5f), Mono = MonoFont(10f), Big = MonoFont(56f, FontStyle.Bold);

    static Color Hex(int rgb) => Color.FromArgb(255, Color.FromArgb(rgb));

    static Font MonoFont(float size, FontStyle style = FontStyle.Regular) =>
        new(FontFamily.Families.Any(f => f.Name == "Cascadia Mono") ? "Cascadia Mono" : "Consolas", size, style);

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Dark title bar, caption in the page colour (Windows 11; ignored elsewhere).</summary>
    public static void DarkTitleBar(nint hwnd)
    {
        int on = 1, caption = 0x0e0c0c, border = 0x2f2a2a;   // COLORREF is 0x00BBGGRR
        DwmSetWindowAttribute(hwnd, 20, ref on, 4);
        DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
        DwmSetWindowAttribute(hwnd, 34, ref border, 4);
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
}

/// <summary>A rounded card with a small-caps section title behind a red bar, as on the MediaMTXUI dashboard.</summary>
sealed class Card : Panel
{
    readonly string title;

    public Card(string title)
    {
        this.title = title.ToUpperInvariant();
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Theme.Background;
        Padding = new Padding(18, 46, 18, 18);
        Margin = new Padding(0, 0, 0, 16);
    }

    // The card's own background is the page colour (for the rounded corners); its contents sit on the card colour.
    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is { } c and not Button) c.BackColor = Theme.Card;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float s = DeviceDpi / 96f;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 12 * s))
        using (var fill = new SolidBrush(Theme.Card))
        using (var pen = new Pen(Theme.Border))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
        using (var bar = new SolidBrush(Theme.Signal)) using (var p = Theme.Rounded(new RectangleF(18 * s, 19 * s, 3 * s, 13 * s), 1.2f * s)) g.FillPath(bar, p);
        TextRenderer.DrawText(g, title, Theme.Small, new Point((int)(28 * s), (int)(17 * s)), Theme.MutedForeground, Theme.Card);
    }
}

/// <summary>A state as the dashboard shows it: a neutral pill with a coloured dot and a word, never colour alone.</summary>
sealed class Pill : Control
{
    Tone tone;

    public Pill()
    {
        DoubleBuffered = true;
        Font = Theme.Body;
        ForeColor = Theme.Foreground;
        BackColor = Theme.Background;
    }

    public void Set(string text, Tone t)
    {
        if (text == Text && t == tone) return;
        (Text, tone) = (text, t);
        float s = DeviceDpi / 96f;
        Size = new Size(TextRenderer.MeasureText(text, Font).Width + (int)(34 * s), (int)(28 * s));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float s = DeviceDpi / 96f;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Theme.Accent)) using (var p = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) / 2f)) g.FillPath(bg, p);
        var dot = tone switch { Tone.Good => Theme.Good, Tone.Warning => Theme.Warning, _ => Theme.MutedForeground };
        using (var b = new SolidBrush(dot)) g.FillEllipse(b, 11 * s, Height / 2f - 4 * s, 8 * s, 8 * s);
        TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(24 * s), 0, Width, Height), ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}

/// <summary>A sub-heading inside a card: small caps in the muted colour, then a hairline to the right edge. The red bar
/// stays reserved for card titles, so the two levels read differently.</summary>
sealed class SectionTitle : Control
{
    public SectionTitle(string text)
    {
        Text = text.ToUpperInvariant();
        Font = Theme.Small;
        Height = 34;
        Anchor = AnchorStyles.Left | AnchorStyles.Right;
        Margin = new Padding(0, 6, 0, 2);
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var size = TextRenderer.MeasureText(Text, Font);
        int y = Height - size.Height - 2;
        TextRenderer.DrawText(e.Graphics, Text, Font, new Point(0, y), Theme.MutedForeground, BackColor);
        using var pen = new Pen(Theme.Border);
        int lineY = y + size.Height / 2, x = size.Width + (int)(6 * DeviceDpi / 96f);
        e.Graphics.DrawLine(pen, x, lineY, Width, lineY);
    }
}


enum Tone { Neutral, Good, Warning }

/// <summary>A row of choices in one rounded track; the chosen one is filled red. Arrow keys move the choice.</summary>
sealed class Segmented : Control
{
    readonly string[] options;
    int selected, hover = -1;

    public event EventHandler? SelectedIndexChanged;

    public Segmented(params string[] options)
    {
        this.options = options;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Font = Theme.Body;
        BackColor = Theme.Card;
        Cursor = Cursors.Hand;
        Height = 32;
        TabStop = true;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => selected;
        set
        {
            value = Math.Clamp(value, 0, options.Length - 1);
            if (value == selected) return;
            selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Height = (int)(32 * DeviceDpi / 96f);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Height = (int)(32 * DeviceDpi / 96f);
    }

    int At(int x) => Math.Clamp(x * options.Length / Math.Max(1, Width), 0, options.Length - 1);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = At(e.X);
        if (h != hover) { hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Left) SelectedIndex = At(e.X);
        base.OnMouseDown(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) SelectedIndex--;
        if (e.KeyCode == Keys.Right) SelectedIndex++;
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }

    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        float s = DeviceDpi / 96f;
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var p = Theme.Rounded(track, 8 * s))
        using (var b = new SolidBrush(Theme.Muted))
        using (var pen = new Pen(Focused ? Theme.MutedForeground : Theme.Border))
        {
            g.FillPath(b, p);
            g.DrawPath(pen, p);
        }
        float w = (Width - 6 * s) / options.Length;
        for (int i = 0; i < options.Length; i++)
        {
            var r = new RectangleF(3 * s + i * w, 3 * s, w, Height - 6 * s);
            if (i == selected || i == hover)
            {
                using var p = Theme.Rounded(r, 6 * s);
                using var b = new SolidBrush(i == selected ? Theme.Primary : Theme.Accent);
                g.FillPath(b, p);
            }
            TextRenderer.DrawText(g, options[i], i == selected ? Theme.BodyBold : Font, Rectangle.Round(r),
                i == selected ? Color.White : Theme.MutedForeground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }
}
