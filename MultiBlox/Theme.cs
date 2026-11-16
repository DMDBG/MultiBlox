using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MultiBlox;

static class Theme
{
    public static readonly Color Bg = Color.FromArgb(13, 15, 20);
    public static readonly Color Sidebar = Color.FromArgb(18, 21, 28);
    public static readonly Color Card = Color.FromArgb(24, 28, 36);
    public static readonly Color CardAlt = Color.FromArgb(28, 32, 41);
    public static readonly Color CardBorder = Color.FromArgb(38, 44, 56);
    public static readonly Color Input = Color.FromArgb(31, 36, 46);
    public static readonly Color Hover = Color.FromArgb(40, 46, 58);
    public static readonly Color Selection = Color.FromArgb(40, 58, 102);
    public static readonly Color Text = Color.FromArgb(232, 235, 240);
    public static readonly Color TextDim = Color.FromArgb(196, 201, 210);
    public static readonly Color Muted = Color.FromArgb(132, 141, 156);
    public static readonly Color Accent = Color.FromArgb(91, 140, 255);
    public static readonly Color AccentHover = Color.FromArgb(120, 162, 255);
    public static readonly Color AccentDown = Color.FromArgb(70, 116, 230);
    public static readonly Color Success = Color.FromArgb(52, 199, 128);
    public static readonly Color Danger = Color.FromArgb(240, 92, 92);
    public static readonly Color Warn = Color.FromArgb(245, 180, 70);

    public static readonly Font Body = new("Segoe UI", 9.5f);
    public static readonly Font Semi = new("Segoe UI Semibold", 9.5f);
    public static readonly Font SmallBold = new("Segoe UI Semibold", 8f);
    public static readonly Font H2 = new("Segoe UI Semibold", 12f);
    public static readonly Font Title = new("Segoe UI Semibold", 19f);
    public static readonly Font Stat = new("Segoe UI Semibold", 17f);
    public static readonly Font Mono = new("Consolas", 9f);
    public static readonly Font Icons = new("Segoe MDL2 Assets", 11.5f);

    public static GraphicsPath Round(Rectangle r, int rad)
    {
        var p = new GraphicsPath();
        if (rad <= 0) { p.AddRectangle(r); return p; }
        int d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Rectangle r, int rad, Color fill, Color border)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Round(r, rad);
        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        if (border != Color.Empty) using (var pen = new Pen(border)) g.DrawPath(pen, path);
        g.SmoothingMode = SmoothingMode.Default;
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);

    /// <summary>Dark window title bar (Windows 10 20H1+ / 11).</summary>
    public static void DarkTitleBar(Form f)
    {
        f.HandleCreated += (_, _) =>
        {
            int on = 1;
            DwmSetWindowAttribute(f.Handle, 20, ref on, 4);
            int caption = ColorTranslator.ToWin32(Sidebar);
            DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);   // Windows 11 only, ignored elsewhere
        };
    }

    /// <summary>Dark scrollbars on standard controls.</summary>
    public static void DarkScroll(Control c)
    {
        if (c.IsHandleCreated) SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
        c.HandleCreated += (_, _) => SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
    }

    public static void DoubleBuffer(Control c) =>
        typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(c, true);

    /// <summary>Turns a WinForms ListView into a dark, roomy, owner-drawn table.</summary>
    public static void StyleList(ListView lv, int statusCol = -1)
    {
        lv.BackColor = Card;
        lv.ForeColor = Text;
        lv.BorderStyle = BorderStyle.None;
        lv.Font = Body;
        lv.OwnerDraw = true;
        lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        lv.SmallImageList = new ImageList { ImageSize = new Size(1, 34) };   // sets row height
        DoubleBuffer(lv);
        DarkScroll(lv);

        lv.DrawColumnHeader += (_, e) =>
        {
            using (var b = new SolidBrush(Card)) e.Graphics.FillRectangle(b, e.Bounds);
            using (var pen = new Pen(CardBorder)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            var r = e.Bounds;
            int pad = e.ColumnIndex == 0 && lv.CheckBoxes ? 32 : 12;
            r.X += pad; r.Width -= pad;
            TextRenderer.DrawText(e.Graphics, e.Header.Text.ToUpperInvariant(), SmallBold, r, Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        };
        lv.DrawItem += (_, e) => e.DrawDefault = false;
        lv.DrawSubItem += (_, e) =>
        {
            var g = e.Graphics;
            Color bg = e.Item.Selected ? Selection : (e.ItemIndex % 2 == 0 ? Card : CardAlt);
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, e.Bounds);
            var r = e.Bounds;
            r.X += 12; r.Width -= 12;

            if (e.ColumnIndex == 0 && lv.CheckBoxes)
            {
                var box = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + (e.Bounds.Height - 16) / 2, 16, 16);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = Round(box, 4);
                if (e.Item.Checked)
                {
                    using (var b = new SolidBrush(Accent)) g.FillPath(b, path);
                    using var pen = new Pen(Color.White, 2f);
                    g.DrawLines(pen, new[] { new Point(box.X + 4, box.Y + 8), new Point(box.X + 7, box.Y + 11), new Point(box.X + 12, box.Y + 5) });
                }
                else using (var pen = new Pen(Muted, 1.5f)) g.DrawPath(pen, path);
                g.SmoothingMode = SmoothingMode.Default;
                r.X = box.Right + 12;
                r.Width = e.Bounds.Right - r.X;
            }

            string t = e.SubItem.Text;
            Color fg = e.ColumnIndex == 0 ? Text : TextDim;
            if (e.ColumnIndex == statusCol)
            {
                if (t.StartsWith("✖")) fg = Danger;
                else if (t.StartsWith("Running") || t.StartsWith("Cookie OK") || t == "visible") fg = Success;
                else if (t.EndsWith("...") || t == "loading") fg = Warn;
                else fg = Muted;
            }
            TextRenderer.DrawText(g, t, e.ColumnIndex == 0 ? Semi : Body, r, fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        };
        // Work around ListView owner-draw hover glitches.
        lv.MouseMove += (_, e) => { var it = lv.GetItemAt(e.X, e.Y); if (it != null) lv.Invalidate(it.Bounds); };
        // Last column stretches so the header never shows an unpainted strip.
        void Stretch()
        {
            if (lv.Columns.Count == 0) return;
            int used = 0;
            for (int i = 0; i < lv.Columns.Count - 1; i++) used += lv.Columns[i].Width;
            lv.Columns[^1].Width = Math.Max(80, lv.ClientSize.Width - used);
        }
        lv.Resize += (_, _) => Stretch();
        lv.HandleCreated += (_, _) => Stretch();
    }
}

enum BtnKind { Primary, Secondary, Danger }

class ModernButton : Button
{
    public BtnKind Kind { get; set; }
    bool hover, down;

    public ModernButton(string text, BtnKind kind = BtnKind.Secondary)
    {
        Text = text;
        Kind = kind;
        Font = Theme.Semi;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(0, 0, 8, 0);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public override Size GetPreferredSize(Size proposed) =>
        new(TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix).Width + LogicalToDeviceUnits(36), LogicalToDeviceUnits(36));

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        Color fill, fg = Theme.Text, border = Color.Empty;
        switch (Kind)
        {
            case BtnKind.Primary:
                fill = !Enabled ? Color.FromArgb(48, 60, 92) : down ? Theme.AccentDown : hover ? Theme.AccentHover : Theme.Accent;
                fg = Enabled ? Color.White : Color.FromArgb(150, 160, 185);
                break;
            case BtnKind.Danger:
                fill = down ? Color.FromArgb(90, 36, 40) : hover ? Color.FromArgb(72, 32, 36) : Color.FromArgb(50, 28, 32);
                fg = Theme.Danger;
                border = Color.FromArgb(84, 40, 46);
                break;
            default:
                fill = down ? Color.FromArgb(50, 57, 71) : hover ? Theme.Hover : Theme.Input;
                border = Theme.CardBorder;
                if (!Enabled) fg = Theme.Muted;
                break;
        }
        Theme.FillRound(g, new Rectangle(0, 0, Width - 1, Height - 1), LogicalToDeviceUnits(8), fill, border);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

/// <summary>iOS-style switch that behaves like a CheckBox.</summary>
class Toggle : CheckBox
{
    bool hover;

    public Toggle(string text, bool on = false)
    {
        Text = text;
        Checked = on;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        AutoSize = true;
        Margin = new Padding(0, 4, 16, 4);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public override Size GetPreferredSize(Size proposed) =>
        new(TextRenderer.MeasureText(Text, Font).Width + LogicalToDeviceUnits(56), LogicalToDeviceUnits(30));

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Card);
        int tw = LogicalToDeviceUnits(40), th = LogicalToDeviceUnits(22);
        var track = new Rectangle(0, (Height - th) / 2, tw, th);
        Color fill = Checked ? (hover ? Theme.AccentHover : Theme.Accent) : (hover ? Color.FromArgb(70, 77, 92) : Color.FromArgb(56, 62, 76));
        Theme.FillRound(g, track, th / 2, fill, Color.Empty);
        int k = th - LogicalToDeviceUnits(6);
        int kx = Checked ? track.Right - k - LogicalToDeviceUnits(3) : track.X + LogicalToDeviceUnits(3);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx, track.Y + LogicalToDeviceUnits(3), k, k);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
        var tr = new Rectangle(tw + LogicalToDeviceUnits(12), 0, Width - tw - LogicalToDeviceUnits(12), Height);
        TextRenderer.DrawText(g, Text, Font, tr, Enabled ? Theme.Text : Theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
    }
}

/// <summary>Rounded panel used for fixed-size containers (lists, inputs, log).</summary>
class RoundPanel : Panel
{
    public int Radius { get; set; }
    Color border;
    public Color BorderColor { get => border; set { border = value; Invalidate(); } }

    public RoundPanel(Color fill, int radius, Color? borderColor = null)
    {
        BackColor = fill;
        Radius = radius;
        border = borderColor ?? Theme.CardBorder;
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Bg);
        Theme.FillRound(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), LogicalToDeviceUnits(Radius), BackColor, border);
    }
}

/// <summary>Rounded, auto-sizing card that lays its children out in a flow.</summary>
class Card : FlowLayoutPanel
{
    public Card(FlowDirection dir = FlowDirection.TopDown)
    {
        FlowDirection = dir;
        WrapContents = dir == FlowDirection.LeftToRight;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Theme.Card;
        Padding = new Padding(20, 16, 20, 14);
        Margin = new Padding(0, 0, 0, 14);
        DoubleBuffered = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Bg);
        Theme.FillRound(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), LogicalToDeviceUnits(12), BackColor, Theme.CardBorder);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        FitNotes(this, Width - Padding.Horizontal - 8);
        Invalidate();
    }

    static void FitNotes(Control c, int w)
    {
        foreach (Control k in c.Controls)
        {
            if (k is Label l && "note".Equals(l.Tag)) { if (l.MaximumSize.Width != w) l.MaximumSize = new Size(Math.Max(120, w), 0); }
            else FitNotes(k, w);
        }
    }
}

/// <summary>Dark drop-down picker (Windows' ComboBox can't be fully themed): shows the choice + ▾, opens a dark menu.</summary>
class DarkSelect : Control
{
    public readonly List<string> Items = new();
    int index = -1;
    bool hover;
    public event EventHandler SelectedIndexChanged;

    public DarkSelect(int width)
    {
        Size = new Size(width, 34);
        Margin = new Padding(0);
        Cursor = Cursors.Hand;
        Font = Theme.Body;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
    }

    public int SelectedIndex
    {
        get => index;
        set
        {
            int v = Items.Count == 0 ? -1 : Math.Clamp(value, -1, Items.Count - 1);
            if (v == index) return;
            index = v;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SelectedItem => index >= 0 && index < Items.Count ? Items[index] : null;

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (Items.Count == 0) return;
        var menu = new ContextMenuStrip
        {
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuTable()), BackColor = Theme.Card, ForeColor = Theme.Text,
            Font = Font, ShowImageMargin = false, ShowCheckMargin = true, MinimumSize = new Size(Width, 0)
        };
        for (int i = 0; i < Items.Count; i++)
        {
            int k = i;
            menu.Items.Add(new ToolStripMenuItem(Items[i], null, (_, _) => SelectedIndex = k)
            { Checked = i == index, ForeColor = Theme.Text, Padding = new Padding(2, 3, 2, 3) });
        }
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(this, new Point(0, Height + 2));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Card);
        Theme.FillRound(g, new Rectangle(0, 0, Width - 1, Height - 1), LogicalToDeviceUnits(8), hover ? Theme.Hover : Theme.Input, Focused ? Theme.Accent : Theme.CardBorder);
        int pad = LogicalToDeviceUnits(10), arrow = LogicalToDeviceUnits(22);
        TextRenderer.DrawText(g, SelectedItem ?? "", Font, new Rectangle(pad, 0, Width - pad - arrow, Height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "▾", Theme.Semi, new Rectangle(Width - arrow - LogicalToDeviceUnits(4), 0, arrow, Height), Theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
    }

    class DarkMenuTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Card;
        public override Color MenuBorder => Theme.CardBorder;
        public override Color MenuItemBorder => Theme.Hover;
        public override Color MenuItemSelected => Theme.Hover;
        public override Color MenuItemSelectedGradientBegin => Theme.Hover;
        public override Color MenuItemSelectedGradientEnd => Theme.Hover;
        public override Color CheckBackground => Theme.Selection;
        public override Color CheckSelectedBackground => Theme.Selection;
        public override Color CheckPressedBackground => Theme.Selection;
        public override Color ImageMarginGradientBegin => Theme.Card;
        public override Color ImageMarginGradientMiddle => Theme.Card;
        public override Color ImageMarginGradientEnd => Theme.Card;
    }
}

class NavButton : Control
{
    readonly string glyph;
    bool hover, active;
    public bool Active { get => active; set { active = value; Invalidate(); } }

    public NavButton(string text, string glyph)
    {
        Text = text;
        this.glyph = glyph;
        Cursor = Cursors.Hand;
        Font = Theme.Semi;
        Size = new Size(192, 42);
        Margin = new Padding(0, 0, 0, 4);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Sidebar);
        if (active || hover)
            Theme.FillRound(g, new Rectangle(0, 0, Width - 1, Height - 1), LogicalToDeviceUnits(8),
                active ? Color.FromArgb(32, 40, 58) : Color.FromArgb(26, 30, 39), Color.Empty);
        if (active)
            Theme.FillRound(g, new Rectangle(0, LogicalToDeviceUnits(10), LogicalToDeviceUnits(3), Height - LogicalToDeviceUnits(20)), 2, Theme.Accent, Color.Empty);
        Color fg = active ? Theme.Text : hover ? Theme.TextDim : Theme.Muted;
        TextRenderer.DrawText(g, glyph, Theme.Icons, new Rectangle(LogicalToDeviceUnits(14), 0, LogicalToDeviceUnits(24), Height),
            active ? Theme.Accent : fg, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(LogicalToDeviceUnits(48), 0, Width, Height), fg,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

class StatTile : Control
{
    string value = "-";
    Color valueColor = Theme.Text;
    readonly Color dot;
    public string Value { get => value; set { if (this.value != value) { this.value = value; Invalidate(); } } }
    public Color ValueColor { set { if (valueColor != value) { valueColor = value; Invalidate(); } } }

    public StatTile(string title, Color dot)
    {
        Text = title;
        this.dot = dot;
        Dock = DockStyle.Fill;
        Margin = new Padding(0, 0, 12, 0);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        Theme.FillRound(g, new Rectangle(0, 0, Width - 1, Height - 1), LogicalToDeviceUnits(12), Theme.Card, Theme.CardBorder);
        int S(int v) => LogicalToDeviceUnits(v);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(dot)) g.FillEllipse(b, S(18), S(19), S(8), S(8));
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
        TextRenderer.DrawText(g, Text.ToUpperInvariant(), Theme.SmallBold, new Point(S(32), S(15)), Theme.Muted);
        TextRenderer.DrawText(g, value, Theme.Stat, new Point(S(15), S(36)), valueColor);
    }
}
