using System.Runtime.InteropServices;

namespace MultiBlox;

/// <summary>Windows' live window thumbnails (DWM) - the same thing Alt+Tab uses.</summary>
static class Dwm
{
    [StructLayout(LayoutKind.Sequential)]
    struct THUMB_PROPS
    {
        public int dwFlags;
        public Native.RECT rcDestination;
        public Native.RECT rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }
    [StructLayout(LayoutKind.Sequential)] struct PSIZE { public int x, y; }
    const int TNP_RECTDESTINATION = 0x1, TNP_OPACITY = 0x4, TNP_VISIBLE = 0x8, TNP_SOURCECLIENTAREAONLY = 0x10;

    [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
    [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumb);
    [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref THUMB_PROPS props);
    [DllImport("dwmapi.dll")] static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out PSIZE size);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    public static IntPtr Register(IntPtr dest, IntPtr src) => DwmRegisterThumbnail(dest, src, out var t) == 0 ? t : IntPtr.Zero;
    public static void Unregister(IntPtr thumb) { if (thumb != IntPtr.Zero) DwmUnregisterThumbnail(thumb); }

    public static Size SourceSize(IntPtr thumb) =>
        DwmQueryThumbnailSourceSize(thumb, out var s) == 0 && s.x > 0 && s.y > 0 ? new Size(s.x, s.y) : Size.Empty;

    /// <summary>Draws the thumbnail inside <paramref name="box"/>, letterboxed to keep the game's shape.</summary>
    public static void Place(IntPtr thumb, Rectangle box, bool visible)
    {
        var src = SourceSize(thumb);
        if (!src.IsEmpty)
        {
            double scale = Math.Min(box.Width / (double)src.Width, box.Height / (double)src.Height);
            int w = (int)(src.Width * scale), h = (int)(src.Height * scale);
            box = new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
        }
        var p = new THUMB_PROPS
        {
            dwFlags = TNP_RECTDESTINATION | TNP_VISIBLE | TNP_OPACITY | TNP_SOURCECLIENTAREAONLY,
            rcDestination = new Native.RECT { Left = box.Left, Top = box.Top, Right = box.Right, Bottom = box.Bottom },
            opacity = 255,
            fVisible = visible,
            fSourceClientAreaOnly = true
        };
        DwmUpdateThumbnailProperties(thumb, ref p);
    }

    /// <summary>Rounded window corners on Windows 11 (ignored on Windows 10).</summary>
    public static void RoundCorners(IntPtr h) { int round = 2; DwmSetWindowAttribute(h, 33, ref round, 4); }
}

/// <summary>
/// A small always-on-top window that shows a live preview of every Roblox window, at any size
/// (Roblox won't let its own window shrink past a minimum, but these previews can be tiny).
/// Hovering a preview brings the real Roblox window up beside the viewer as the active window.
///
/// Minimized windows can't be previewed, so instead of minimizing, the viewer "parks" the real
/// Roblox windows just off-screen at their normal size, where they keep running and rendering.
/// </summary>
class MiniViewer : Form
{
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);

    const int CaptionH = 26, Pad = 6;

    /// <summary>One preview card. The live picture is drawn by Windows over <see cref="Screen"/>.</summary>
    class Tile : Panel
    {
        public IntPtr Src, Thumb;
        public int Pid;
        public string Title = "";
        public bool Hot, Parked, Dragging;
        public readonly Panel Screen = new() { Dock = DockStyle.Top, BackColor = Color.FromArgb(6, 7, 10), Cursor = Cursors.Hand };

        public Tile()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            Cursor = Cursors.Hand;
            Padding = new Padding(Pad, Pad, Pad, 0);
            BackColor = Theme.Bg;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            Theme.FillRound(g, r, 10, Dragging ? Color.FromArgb(36, 48, 78) : Hot ? Color.FromArgb(30, 40, 62) : Theme.Card,
                Hot || Dragging ? Theme.Accent : Theme.CardBorder);
            if (Hot || Dragging)   // brighter double outline = "glow"
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), 11);
                using var pen = new Pen(Color.FromArgb(110, Theme.Accent), 1f);
                g.DrawPath(pen, path);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
            }

            // Caption: status dot + account name (+ state on wider tiles)
            int top = Screen.Bottom, h = Height - top;
            var dot = new Rectangle(Pad + 4, top + (h - 8) / 2, 8, 8);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Parked ? Theme.Success : Theme.Accent)) g.FillEllipse(b, dot);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
            int right = Width - Pad - 4;
            if (Width >= 170)
            {
                string state = Parked ? "LIVE" : "ON SCREEN";
                int sw = TextRenderer.MeasureText(state, Theme.SmallBold).Width;
                TextRenderer.DrawText(g, state, Theme.SmallBold, new Rectangle(right - sw, top, sw, h),
                    Parked ? Theme.Success : Theme.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine);
                right -= sw + 6;
            }
            TextRenderer.DrawText(g, Title, Theme.SmallBold, new Rectangle(dot.Right + 6, top, Math.Max(0, right - dot.Right - 6), h),
                Hot ? Theme.Text : Theme.TextDim,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    readonly Func<List<(int pid, IntPtr hwnd)>> source;
    readonly Func<int, string> nameOf;
    readonly Action<List<int>> onReorder;   // tiles were dragged into a new order (PIDs, first to last)
    readonly Dictionary<IntPtr, Tile> tiles = new();
    readonly Dictionary<IntPtr, Native.RECT> homeRects = new();   // where each window was before parking
    readonly HashSet<IntPtr> parked = new();
    readonly FlowLayoutPanel grid;
    readonly Label empty, count;
    readonly Toggle chkPark, chkTop, chkPeek;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1500 };
    readonly System.Windows.Forms.Timer hoverTimer = new() { Interval = 60 };
    readonly int tileW, tileH, columns;
    int lastCount = -1;
    Tile hot;
    DateTime hotSince;

    public bool ParkEnabled => chkPark.Checked;
    public bool IsParked(IntPtr h) => parked.Contains(h);

    public MiniViewer(Func<List<(int, IntPtr)>> source, Func<int, string> nameOf, Action<List<int>> onReorder, int tileW, int tileH, int columns)
    {
        this.source = source;
        this.nameOf = nameOf;
        this.onReorder = onReorder;
        this.tileW = tileW;
        this.tileH = tileH;
        this.columns = columns;

        AutoScaleMode = AutoScaleMode.None;   // tile sizes are real pixels, like Roblox window sizes
        Text = "MultiBlox - Mini Viewer";
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        TopMost = true;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(260, 150);
        Theme.DarkTitleBar(this);
        HandleCreated += (_, _) => Dwm.RoundCorners(Handle);

        // Header: accent strip + switches + running count
        var accent = new Panel { Dock = DockStyle.Top, Height = 3, BackColor = Theme.Accent };
        var bar = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Theme.Sidebar, Padding = new Padding(10, 4, 10, 0) };
        var switches = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Sidebar };
        chkPark = new Toggle("Hide real windows", true) { Margin = new Padding(0, 0, 10, 0) };
        chkPeek = new Toggle("Pop up on hover", true) { Margin = new Padding(0, 0, 10, 0) };
        chkTop = new Toggle("On top", true) { Margin = new Padding(0) };
        switches.Controls.AddRange(new Control[] { chkPark, chkPeek, chkTop });
        count = new Label { Dock = DockStyle.Right, AutoSize = true, ForeColor = Theme.Success, Font = Theme.SmallBold, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 8, 0, 0) };
        bar.Controls.Add(switches);
        bar.Controls.Add(count);
        chkPark.CheckedChanged += (_, _) => { if (chkPark.Checked) ParkAll(); else UnparkAll(); RefreshCaptions(); };
        chkTop.CheckedChanged += (_, _) => TopMost = chkTop.Checked;
        chkPeek.CheckedChanged += (_, _) => { if (!chkPeek.Checked) TuckSurfaced(); };

        grid = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8), BackColor = Theme.Bg };
        Theme.DarkScroll(grid);
        empty = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted,
            Text = "No Roblox windows open yet.\nThey'll show up here automatically."
        };
        grid.Layout += (_, _) => PositionThumbs();
        grid.Scroll += (_, _) => PositionThumbs();

        Controls.Add(grid);
        Controls.Add(empty);
        Controls.Add(bar);
        Controls.Add(accent);

        var area = Screen.PrimaryScreen.WorkingArea;
        Size = new Size(380, 260);
        Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);

        timer.Tick += (_, _) => Sync();
        hoverTimer.Tick += (_, _) => TrackHover();
        Shown += (_, _) => { Sync(); timer.Start(); hoverTimer.Start(); };
        Resize += (_, _) => PositionThumbs();
        FormClosing += (_, _) =>
        {
            timer.Stop();
            hoverTimer.Stop();
            UnparkAll();
            foreach (var t in tiles.Values) Dwm.Unregister(t.Thumb);
            tiles.Clear();
        };
    }

    // ───────────────────────────── tiles ─────────────────────────────

    void Sync()
    {
        var list = source();
        var alive = list.Select(x => x.hwnd).ToHashSet();

        foreach (var gone in tiles.Keys.Where(h => !alive.Contains(h)).ToList())
        {
            if (hot == tiles[gone]) hot = null;
            if (surfaced == gone) surfaced = IntPtr.Zero;
            Dwm.Unregister(tiles[gone].Thumb);
            grid.Controls.Remove(tiles[gone]);
            tiles[gone].Dispose();
            tiles.Remove(gone);
            parked.Remove(gone);
            homeRects.Remove(gone);
        }

        foreach (var (pid, h) in list)
        {
            if (tiles.ContainsKey(h)) continue;
            IntPtr thumb = Dwm.Register(Handle, h);
            if (thumb == IntPtr.Zero) continue;
            var t = new Tile { Src = h, Thumb = thumb, Pid = pid, Size = new Size(tileW + Pad * 2, tileH + Pad + CaptionH), Margin = new Padding(5) };
            t.Screen.Height = tileH;
            t.Controls.Add(t.Screen);
            MouseEventHandler click = (_, e) =>
            {
                if (dragging) return;   // that was the end of a drag, not a click
                if (e.Button == MouseButtons.Left) BringUp(h);
                else if (e.Button == MouseButtons.Right) { Park(h); RefreshCaptions(); }
            };
            t.MouseClick += click;
            t.Screen.MouseClick += click;
            HookDrag(t, t);
            HookDrag(t, t.Screen);
            tiles[h] = t;
            grid.Controls.Add(t);
            if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_SHOWNOACTIVATE);   // minimized windows can't be previewed
            if (ParkEnabled) Park(h);
        }

        if (dragTile == null) ApplyOrder(list);

        empty.Visible = tiles.Count == 0;
        grid.Visible = tiles.Count > 0;
        count.Text = tiles.Count == 0 ? "" : $"●  {tiles.Count} running";
        RefreshCaptions();
        if (tiles.Count != lastCount) { lastCount = tiles.Count; FitToTiles(); }
        PositionThumbs();
    }

    void RefreshCaptions()
    {
        foreach (var t in tiles.Values)
        {
            string title = nameOf(t.Pid);
            bool isParked = parked.Contains(t.Src);
            if (t.Title != title || t.Parked != isParked) { t.Title = title; t.Parked = isParked; t.Invalidate(); }
        }
    }

    /// <summary>Resizes the viewer so all tiles fit in a neat grid.</summary>
    void FitToTiles()
    {
        int n = Math.Max(1, tiles.Count);
        int cols = columns > 0 ? columns : (int)Math.Ceiling(Math.Sqrt(n));
        cols = Math.Min(cols, n);
        int rows = (int)Math.Ceiling(n / (double)cols);
        int cellW = tileW + Pad * 2 + 10, cellH = tileH + Pad + CaptionH + 10;
        var area = Screen.FromControl(this).WorkingArea;
        // Leave room for a scrollbar so it can never squeeze the grid down a column.
        int w = Math.Min(area.Width - 40, Math.Max(MinimumSize.Width, cols * cellW + 16 + 4 + SystemInformation.VerticalScrollBarWidth));
        int h = Math.Min(area.Height - 40, rows * cellH + 16 + 41 + 12);
        var outer = SizeFromClientSize(new Size(w, h));
        // Keep the bottom-right corner anchored so the window grows up and left.
        var br = new Point(Right, Bottom);
        Bounds = new Rectangle(Math.Max(area.Left, br.X - outer.Width), Math.Max(area.Top, br.Y - outer.Height), outer.Width, outer.Height);
    }

    void PositionThumbs()
    {
        if (tiles.Count == 0 || !IsHandleCreated) return;
        var gridRect = RectangleToClient(grid.RectangleToScreen(grid.ClientRectangle));
        foreach (var t in tiles.Values)
        {
            var r = RectangleToClient(t.Screen.RectangleToScreen(t.Screen.ClientRectangle));
            Dwm.Place(t.Thumb, r, gridRect.Contains(r));   // hide tiles scrolled partly out of view
        }
    }

    // ───────────────────────────── drag to reorder ─────────────────────────────

    Tile dragTile;
    Point dragStart;
    bool dragging;

    /// <summary>Puts the tiles in the same order as the source list (which follows the saved order).</summary>
    void ApplyOrder(List<(int pid, IntPtr hwnd)> list)
    {
        int i = 0;
        foreach (var (_, h) in list)
            if (tiles.TryGetValue(h, out var t))
            {
                if (grid.Controls.GetChildIndex(t) != i) grid.Controls.SetChildIndex(t, i);
                i++;
            }
    }

    void HookDrag(Tile t, Control c)
    {
        c.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            dragTile = t; dragStart = Cursor.Position; dragging = false;
        };
        c.MouseMove += (_, _) =>
        {
            if (dragTile != t || (MouseButtons & MouseButtons.Left) == 0) return;
            var cur = Cursor.Position;
            if (!dragging)
            {
                if (Math.Abs(cur.X - dragStart.X) + Math.Abs(cur.Y - dragStart.Y) < 8) return;   // still just a click
                dragging = true;
                TuckSurfaced();
                if (hot != null) { hot.Hot = false; hot.Invalidate(); hot = null; }
                t.Dragging = true; t.Invalidate();
            }
            Cursor.Current = Cursors.SizeAll;
            var target = tiles.Values.FirstOrDefault(x => x != t && x.RectangleToScreen(x.ClientRectangle).Contains(cur));
            if (target != null)
            {
                grid.Controls.SetChildIndex(t, grid.Controls.GetChildIndex(target));
                PositionThumbs();
            }
            else AutoScrollWhileDragging(cur);
        };
        c.MouseUp += (_, _) =>
        {
            if (dragTile != t) return;
            bool moved = dragging;
            dragTile = null; dragging = false;
            t.Dragging = false; t.Invalidate();
            if (moved) onReorder?.Invoke(grid.Controls.OfType<Tile>().Select(x => x.Pid).ToList());
        };
    }

    /// <summary>Dragging above or below the visible tiles scrolls the grid so you can reach the rest.</summary>
    void AutoScrollWhileDragging(Point cur)
    {
        var r = grid.RectangleToScreen(grid.ClientRectangle);
        int step = cur.Y < r.Top + 20 ? -20 : cur.Y > r.Bottom - 20 ? 20 : 0;
        if (step == 0) return;
        var pos = grid.AutoScrollPosition;
        grid.AutoScrollPosition = new Point(-pos.X, Math.Max(0, -pos.Y + step));
        PositionThumbs();
    }

    // ───────────────────────────── hover: bring the real window up ─────────────────────────────

    IntPtr surfaced;            // real Roblox window currently popped up because you're hovering its tile
    bool surfacedWasParked;     // was it tucked away before? (then it goes back when you move off)
    IntPtr focusBefore;         // whatever you were using before, so focus can be handed back
    DateTime lastOnSurface;

    void TrackHover()
    {
        if (dragging) return;   // no popping windows up while you're moving tiles around
        var cursor = Cursor.Position;
        IntPtr root = GetAncestor(WindowFromPoint(cursor), 2 /*GA_ROOT*/);
        Tile hit = null;
        // Only count it if the viewer itself is under the mouse (not a window on top of it).
        if (root == Handle)
            foreach (var t in tiles.Values)
                if (t.Visible && t.RectangleToScreen(t.ClientRectangle).Contains(cursor)) { hit = t; break; }

        if (hit != hot)
        {
            if (hot != null) { hot.Hot = false; hot.Invalidate(); }
            hot = hit;
            hotSince = DateTime.Now;
            if (hot != null) { hot.Hot = true; hot.Invalidate(); }
        }

        if (surfaced != IntPtr.Zero)
        {
            // Stays up while the mouse is on its tile or on the real window itself, so you can watch or use it.
            bool onIt = root == surfaced || (hot != null && hot.Src == surfaced);
            if (onIt) lastOnSurface = DateTime.Now;
            else if ((DateTime.Now - lastOnSurface).TotalMilliseconds > 450) TuckSurfaced();
        }

        if (hot != null && hot.Src != surfaced && chkPeek.Checked && (DateTime.Now - hotSince).TotalMilliseconds > 300)
            Surface(hot);
    }

    /// <summary>Brings the real Roblox window up beside the viewer and makes it the active window.</summary>
    void Surface(Tile t)
    {
        TuckSurfaced();
        IntPtr h = t.Src;
        if (!Native.IsWindow(h)) return;
        focusBefore = Native.GetForegroundWindow();
        surfacedWasParked = parked.Remove(h);
        if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
        if (surfacedWasParked)
        {
            var home = homeRects.TryGetValue(h, out var hr) ? hr : new Native.RECT { Right = 1280, Bottom = 720 };
            int w = home.Right - home.Left, ht = home.Bottom - home.Top;
            var area = Screen.FromControl(this).WorkingArea;
            var tileRect = t.RectangleToScreen(t.ClientRectangle);
            int x = Left - w - 8;                                               // left of the viewer...
            if (x < area.Left) x = Right + 8;                                   // ...or right of it...
            if (x + w > area.Right) x = Math.Max(area.Left, area.Right - w);    // ...or overlapping (viewer stays on top)
            int y = Math.Clamp(tileRect.Top + tileRect.Height / 2 - ht / 2, area.Top, Math.Max(area.Top, area.Bottom - ht));
            Native.SetWindowPos(h, IntPtr.Zero /*top*/, x, y, w, ht, Native.SWP_NOACTIVATE);
        }
        Native.ForceForeground(h);
        surfaced = h;
        lastOnSurface = DateTime.Now;
        RefreshCaptions();
    }

    /// <summary>Tucks the hovered-up window away again and gives focus back to what you were using.</summary>
    void TuckSurfaced()
    {
        if (surfaced == IntPtr.Zero) return;
        IntPtr h = surfaced;
        bool wasParked = surfacedWasParked;
        surfaced = IntPtr.Zero;
        if (wasParked && ParkEnabled && Native.IsWindow(h)) Park(h, rememberPosition: false);
        else if (wasParked) { parked.Add(h); Unpark(h); }   // hiding turned off meanwhile: put it back home
        if (focusBefore != IntPtr.Zero && focusBefore != h && Native.IsWindow(focusBefore) && Native.GetForegroundWindow() == h)
            Native.ForceForeground(focusBefore);
        RefreshCaptions();
    }

    // ───────────────────────────── parking ─────────────────────────────

    /// <summary>Moves a Roblox window just off-screen (it keeps running at normal size).</summary>
    public void Park(IntPtr h, bool rememberPosition = true)
    {
        if (h == surfaced)
        {
            // It's only up because of hover - its real "home" is where it was before that.
            surfaced = IntPtr.Zero;
            if (surfacedWasParked) rememberPosition = false;
        }
        if (!Native.IsWindow(h)) return;
        if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_SHOWNOACTIVATE);
        if (rememberPosition && !parked.Contains(h) && Native.GetWindowRect(h, out var r)) homeRects[h] = r;
        var home = homeRects.TryGetValue(h, out var hr) ? hr : new Native.RECT { Right = 1280, Bottom = 720 };
        var vs = SystemInformation.VirtualScreen;
        Native.SetWindowPos(h, IntPtr.Zero, vs.Right + 100, vs.Top + 100, home.Right - home.Left, home.Bottom - home.Top,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        parked.Add(h);
    }

    /// <summary>Puts a parked window back where it was.</summary>
    public void Unpark(IntPtr h)
    {
        if (!parked.Remove(h) || !Native.IsWindow(h)) return;
        var area = Screen.PrimaryScreen.WorkingArea;
        var r = homeRects.TryGetValue(h, out var hr) && hr.Left > -10000
            ? hr
            : new Native.RECT { Left = area.Left + 60, Top = area.Top + 60, Right = area.Left + 60 + 1280, Bottom = area.Top + 60 + 720 };
        Native.SetWindowPos(h, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    void ParkAll() { foreach (var h in tiles.Keys) Park(h); }

    void UnparkAll()
    {
        // A hovered-up window counts as tucked away, so it also goes back to its real spot.
        if (surfaced != IntPtr.Zero && surfacedWasParked) parked.Add(surfaced);
        surfaced = IntPtr.Zero;
        foreach (var h in parked.ToList()) Unpark(h);
    }

    /// <summary>Click a tile: bring that Roblox window back to its spot and keep it there. Right-click hides it again.</summary>
    void BringUp(IntPtr h)
    {
        if (surfaced == h)
        {
            if (surfacedWasParked) parked.Add(h);   // so Unpark moves it from beside the viewer to its normal spot
            surfaced = IntPtr.Zero;
        }
        Unpark(h);
        Native.ShowWindow(h, Native.SW_RESTORE);
        Native.ForceForeground(h);
        RefreshCaptions();
    }
}
