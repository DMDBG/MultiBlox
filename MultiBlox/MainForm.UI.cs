using System.Diagnostics;

namespace MultiBlox;

/// <summary>Layout and styling of the main window. Behaviour lives in MainForm.cs.</summary>
public partial class MainForm
{
    readonly Dictionary<string, (Control page, string title, string sub)> pages = new();
    readonly List<NavButton> navButtons = new();
    FlowLayoutPanel nav;
    Panel content;
    Label lblPageTitle, lblPageSub, lblNoAccounts;
    StatTile statRunning, statRam, statAfk, statSaver;

    public MainForm()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "MultiBlox";
        Size = new Size(1100, 740);
        MinimumSize = new Size(940, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Theme.DarkTitleBar(this);

        // Right side: header, page area, activity log
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 20, 28, 20) };
        var header = new Panel { Dock = DockStyle.Top, Height = 74 };
        lblPageTitle = new Label { AutoSize = true, Font = Theme.Title, Location = new Point(0, 0), UseMnemonic = false };
        lblPageSub = new Label { AutoSize = true, ForeColor = Theme.Muted, Location = new Point(2, 42) };
        header.Controls.AddRange(new Control[] { lblPageTitle, lblPageSub });
        content = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(content);
        right.Controls.Add(Spacer(14, DockStyle.Bottom));
        right.Controls.Add(BuildLog());
        right.Controls.Add(header);

        Controls.Add(right);
        Controls.Add(BuildSidebar());

        AddPage("accounts", "Accounts", "Add your accounts and launch them into a game", "", BuildAccountsPage());
        AddPage("instances", "Instances & Viewer", "Every Roblox window that's open right now", "", BuildInstancesPage());
        AddPage("discord", "Discord Stats", "Wins, losses and drops from your macro's webhooks", "", BuildDiscordPage());
        AddPage("afk", "Anti-AFK & Saver", "Stay in game while away, and keep your PC cool", "", BuildAfkSaverPage());
        AddPage("fps", "FPS Editor", "Set the frame-rate cap Roblox launches with", "", BuildFpsPage());
        ResumeLayout(false);
        PerformLayout();

        refreshTimer.Tick += (_, _) => { RefreshInstances(); UpdateStats(); };
        afkTimer.Tick += async (_, _) => await AfkTick();
        saverTimer.Tick += (_, _) => ApplySaver();

        Load += (_, _) =>
        {
            // Optional "--page=fps" style argument opens a specific page.
            var arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--page="));
            ShowPage(arg != null && pages.ContainsKey(arg[7..]) ? arg[7..] : "accounts");
            RefreshAccounts();
            StartDiscord();
            UpdateStats();
            refreshTimer.Start();
            if (cfg.SaverEnabled) chkSaver.Checked = true;
            Log("Ready. Add accounts, set a Place ID, then hit Launch.");
            if (Environment.GetCommandLineArgs().Contains("--login")) AddAccount(true);
            if (Environment.GetCommandLineArgs().Contains("--viewer")) BeginInvoke(OpenViewer);
        };
        // Once the window is visible, turn on Multi-Roblox (asks first if Roblox is already open).
        Shown += async (_, _) => await EnableMulti();
        FormClosing += (_, _) =>
        {
            if (viewer != null && !viewer.IsDisposed) viewer.Close();   // brings hidden Roblox windows back on screen
            SaveConfig(); RestoreSaver(); ReleaseMulti();
        };
    }

    // ───────────────────────────── building blocks ─────────────────────────────

    static Panel Spacer(int h, DockStyle dock = DockStyle.Top) => new() { Dock = dock, Height = h };

    static Label H(string text) => new() { Text = text, AutoSize = true, Font = Theme.H2, Margin = new Padding(0, 0, 0, 4) };

    static Label Note(string text) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = Theme.Muted,
        Margin = new Padding(0, 2, 0, 10), Tag = "note"
    };

    static Label Status(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = Theme.Accent, Font = Theme.Semi, Margin = new Padding(0, 6, 0, 2)
    };

    static ModernButton Btn(string text, EventHandler onClick, BtnKind kind = BtnKind.Secondary)
    {
        var b = new ModernButton(text, kind);
        b.Click += onClick;
        return b;
    }

    /// <summary>Button that lines up with the input boxes next to it (which have a caption above).</summary>
    static ModernButton InlineBtn(string text, EventHandler onClick, BtnKind kind = BtnKind.Secondary)
    {
        var b = Btn(text, onClick, kind);
        b.Margin = new Padding(0, 19, 8, 0);
        return b;
    }

    static NumericUpDown Num(int min, int max, int value) => new()
    {
        Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max)
    };

    static FlowLayoutPanel Row(params Control[] items)
    {
        var r = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 2, 0, 4) };
        r.Controls.AddRange(items);
        return r;
    }

    /// <summary>Wraps a TextBox/NumericUpDown in a rounded dark input with a focus ring.</summary>
    static Control Field(Control inner, int width)
    {
        var box = new RoundPanel(Theme.Input, 8) { Size = new Size(width, 34), Padding = new Padding(10, 8, 8, 4), Margin = new Padding(0) };
        inner.BackColor = Theme.Input;
        inner.ForeColor = Theme.Text;
        inner.Font = Theme.Body;
        inner.GotFocus += (_, _) => box.BorderColor = Theme.Accent;
        inner.LostFocus += (_, _) => box.BorderColor = Theme.CardBorder;
        if (inner is NumericUpDown n)
        {
            // The spin arrows can't be themed, so push them outside a clipping panel.
            // Arrow keys and the mouse wheel still change the value.
            n.BorderStyle = BorderStyle.None;
            var clip = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Input };
            n.Location = Point.Empty;
            clip.Resize += (_, _) => n.Width = clip.Width + SystemInformation.VerticalScrollBarWidth + 4;
            clip.Controls.Add(n);
            box.Controls.Add(clip);
            return box;
        }
        if (inner is TextBox tb) tb.BorderStyle = BorderStyle.None;
        inner.Dock = DockStyle.Fill;
        box.Controls.Add(inner);
        return box;
    }

    static Control Labeled(string caption, Control field)
    {
        var f = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 16, 6) };
        f.Controls.Add(new Label { Text = caption.ToUpperInvariant(), AutoSize = true, Font = Theme.SmallBold, ForeColor = Theme.Muted, Margin = new Padding(2, 0, 0, 4) });
        f.Controls.Add(field);
        return f;
    }

    /// <summary>Vertical, scrolling page whose cards stretch to the full width.</summary>
    static FlowLayoutPanel ScrollPage()
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        Theme.DarkScroll(p);
        void Fit()
        {
            int w = p.ClientSize.Width - 4;
            foreach (Control c in p.Controls)
            {
                int cw = Math.Max(200, w - c.Margin.Horizontal);
                if (c.AutoSize)
                {
                    // Auto-sizing cards: pin the width, let the height follow the content.
                    var s = new Size(cw, 0);
                    c.MinimumSize = s;
                    c.MaximumSize = s;
                }
                else c.Width = cw;   // fixed-height items (e.g. stat tiles) keep their height
            }
        }
        p.ClientSizeChanged += (_, _) => Fit();
        p.ControlAdded += (_, _) => Fit();
        return p;
    }

    static Panel Toolbar(Control[] left, Control[] rightSide)
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 36 };
        var l = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        var r = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        l.Controls.AddRange(left);
        r.Controls.AddRange(rightSide);
        if (rightSide.Length > 0) rightSide[^1].Margin = new Padding(0);
        bar.Controls.Add(l);
        bar.Controls.Add(r);
        return bar;
    }

    /// <summary>"+ Add account" opens a small menu: log in with a browser, or paste a cookie.</summary>
    ModernButton AddAccountButton()
    {
        var b = new ModernButton("+  Add account  ▾");
        var menu = DarkMenu(
            ("Log in with browser (username + password)", () => AddAccount(true)),
            ("Paste cookie (advanced)", () => AddAccount(false)));
        b.Click += (_, _) => menu.Show(b, new Point(0, b.Height + 4));
        return b;
    }

    static ContextMenuStrip DarkMenu(params (string text, Action act)[] items)
    {
        var m = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()), BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.Body, ShowImageMargin = false };
        foreach (var (text, act) in items)
            m.Items.Add(new ToolStripMenuItem(text, null, (_, _) => act()) { ForeColor = text == "Remove" ? Theme.Danger : Theme.Text, Padding = new Padding(4, 4, 4, 4) });
        return m;
    }

    class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Card;
        public override Color MenuBorder => Theme.CardBorder;
        public override Color MenuItemBorder => Theme.Hover;
        public override Color MenuItemSelected => Theme.Hover;
        public override Color MenuItemSelectedGradientBegin => Theme.Hover;
        public override Color MenuItemSelectedGradientEnd => Theme.Hover;
    }

    static RoundPanel ListCard(ListView lv)
    {
        var card = new RoundPanel(Theme.Card, 12) { Dock = DockStyle.Fill, Padding = new Padding(2, 8, 2, 8) };
        lv.Dock = DockStyle.Fill;
        card.Controls.Add(lv);
        return card;
    }

    // ───────────────────────────── shell ─────────────────────────────

    Control BuildSidebar()
    {
        var side = new Panel { Dock = DockStyle.Left, Width = 224, BackColor = Theme.Sidebar, Padding = new Padding(16, 18, 16, 18) };

        var brand = new Panel { Dock = DockStyle.Top, Height = 64 };
        brand.Paint += (_, e) =>
        {
            int S(int v) => brand.LogicalToDeviceUnits(v);
            var g = e.Graphics;
            var logo = new Rectangle(S(4), S(6), S(36), S(36));
            Theme.FillRound(g, logo, S(10), Theme.Accent, Color.Empty);
            TextRenderer.DrawText(g, "M", new Font("Segoe UI Black", 13f), logo, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "MultiBlox", new Font("Segoe UI Semibold", 13f), new Point(S(50), S(4)), Theme.Text);
            TextRenderer.DrawText(g, "Multi-account launcher", Theme.SmallBold, new Point(S(52), S(28)), Theme.Muted);
        };

        var menuLbl = new Label { Text = "MENU", Dock = DockStyle.Top, Height = 26, Font = Theme.SmallBold, ForeColor = Theme.Muted, Padding = new Padding(4, 6, 0, 0) };
        nav = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };

        // Multi-Roblox status pinned to the bottom
        var statusCard = new RoundPanel(Theme.Card, 10) { Dock = DockStyle.Bottom, Height = 112, Padding = new Padding(14, 12, 12, 12) };
        lblMulti = new Label { Text = "●  Multi-Roblox ...", Dock = DockStyle.Top, Height = 46, Font = Theme.Semi, ForeColor = Theme.Muted };
        btnMulti = Btn("Enable Multi-Roblox", async (_, _) => await EnableMulti(), BtnKind.Primary);
        btnMulti.Dock = DockStyle.Top;
        btnMulti.VisibleChanged += (_, _) => statusCard.Height = statusCard.LogicalToDeviceUnits(btnMulti.Visible ? 112 : 72);
        statusCard.Controls.Add(btnMulti);
        statusCard.Controls.Add(lblMulti);

        // Later-added controls dock first: brand ends up at the very top, status card at the bottom.
        side.Controls.Add(nav);
        side.Controls.Add(menuLbl);
        side.Controls.Add(Spacer(10));
        side.Controls.Add(brand);
        side.Controls.Add(statusCard);
        return side;
    }

    Control BuildLog()
    {
        var card = new RoundPanel(Theme.Card, 12) { Dock = DockStyle.Bottom, Height = 128, Padding = new Padding(16, 10, 12, 10) };
        var title = new Label { Text = "ACTIVITY", Dock = DockStyle.Top, Height = 22, Font = Theme.SmallBold, ForeColor = Theme.Muted };
        txtLog = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.None,   // mouse wheel still scrolls
            BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.TextDim, Font = Theme.Mono
        };
        Theme.DarkScroll(txtLog);
        card.Controls.Add(txtLog);
        card.Controls.Add(title);
        return card;
    }

    void AddPage(string key, string title, string sub, string glyph, Control page)
    {
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        content.Controls.Add(page);
        pages[key] = (page, title, sub);
        var nb = new NavButton(title, glyph) { Tag = key };
        nb.Click += (_, _) => ShowPage(key);
        nav.Controls.Add(nb);
        navButtons.Add(nb);
    }

    void ShowPage(string key)
    {
        foreach (var kv in pages) kv.Value.page.Visible = kv.Key == key;
        lblPageTitle.Text = pages[key].title;
        lblPageSub.Text = pages[key].sub;
        foreach (var nb in navButtons) nb.Active = (string)nb.Tag == key;
    }

    void UpdateStats()
    {
        int count = 0;
        long ram = 0;
        foreach (var p in Process.GetProcessesByName(ProcName))
        {
            count++;
            try { ram += p.WorkingSet64; } catch { }
            p.Dispose();
        }
        statRunning.Value = RobloxWindowMap().Count.ToString();   // games with a window; leftovers don't count
        _ = count;
        statRam.Value = ram >= 1L << 30 ? $"{ram / (double)(1L << 30):0.0} GB" : $"{ram >> 20} MB";
        statAfk.Value = chkAfk.Checked ? "On" : "Off";
        statAfk.ValueColor = chkAfk.Checked ? Theme.Success : Theme.Muted;
        statSaver.Value = chkSaver.Checked ? "On" : "Off";
        statSaver.ValueColor = chkSaver.Checked ? Theme.Success : Theme.Muted;
        lblNoAccounts.Visible = cfg.Accounts.Count == 0;
    }

    // ───────────────────────────── pages ─────────────────────────────

    Control BuildAccountsPage()
    {
        var page = new Panel();

        // Launch settings
        var settings = new Card(FlowDirection.LeftToRight) { Dock = DockStyle.Top, Margin = new Padding(0) };
        txtPlace = new TextBox { Text = cfg.PlaceId, PlaceholderText = "e.g. 920587237" };
        txtJob = new TextBox { Text = cfg.JobId, PlaceholderText = "Optional - join a specific server" };
        numDelay = Num(3, 120, cfg.LaunchDelaySec);
        settings.Controls.Add(Labeled("Place ID", Field(txtPlace, 180)));
        settings.Controls.Add(Labeled("Job ID", Field(txtJob, 300)));
        settings.Controls.Add(Labeled("Seconds between launches", Field(numDelay, 110)));

        // Toolbar
        var bLaunchSel = Btn("▶  Launch selected", async (_, _) => await LaunchMany(SelectedAccounts()));
        var bLaunchChk = Btn("▶  Launch all checked", async (_, _) => await LaunchMany(cfg.Accounts.Where(a => a.Checked).ToList()), BtnKind.Primary);
        launchButtons.Add(bLaunchSel);
        launchButtons.Add(bLaunchChk);
        var toolbar = Toolbar(
            new Control[]
            {
                AddAccountButton(),
                Btn("Set place", (_, _) => SetPlaceOverride()),
                Btn("Remove", (_, _) => RemoveAccounts(), BtnKind.Danger)
            },
            new Control[] { bLaunchSel, bLaunchChk });

        // List
        lvAccounts = new ListView { View = View.Details, FullRowSelect = true, CheckBoxes = true, HideSelection = false };
        lvAccounts.Columns.Add("Account", 230);
        lvAccounts.Columns.Add("User ID", 140);
        lvAccounts.Columns.Add("Place override", 150);
        lvAccounts.Columns.Add("Status", 200);
        Theme.StyleList(lvAccounts, statusCol: 3);
        lvAccounts.ItemChecked += (_, e) => { if (e.Item.Tag is Account a) { a.Checked = e.Item.Checked; SaveConfig(); } };
        lvAccounts.DoubleClick += (_, _) => SetPlaceOverride();
        lvAccounts.ContextMenuStrip = DarkMenu(
            ("▶  Launch", async () => await LaunchMany(SelectedAccounts())),
            ("Set place override", SetPlaceOverride),
            ("Check all cookies", async () => await CheckCookies()),
            ("Remove", RemoveAccounts));
        var listCard = ListCard(lvAccounts);
        lblNoAccounts = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted, BackColor = Theme.Card,
            Text = "No accounts yet\n\nClick  “+ Add account”  and log in like normal.\nRight-click an account for more options."
        };
        listCard.Controls.Add(lblNoAccounts);
        lblNoAccounts.BringToFront();

        page.Controls.Add(listCard);
        page.Controls.Add(Spacer(12));
        page.Controls.Add(toolbar);
        page.Controls.Add(Spacer(14));
        page.Controls.Add(settings);
        return page;
    }

    Control BuildInstancesPage()
    {
        var page = new Panel();

        // Stat tiles
        var stats = new TableLayoutPanel { Dock = DockStyle.Top, Height = 84, ColumnCount = 4, RowCount = 1, Margin = new Padding(0) };
        for (int i = 0; i < 4; i++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        statRunning = new StatTile("Running", Theme.Accent);
        statRam = new StatTile("Total RAM", Color.FromArgb(170, 120, 255));
        statAfk = new StatTile("Anti-AFK", Theme.Warn);
        statSaver = new StatTile("Power saver", Theme.Success) { Margin = new Padding(0) };
        stats.Controls.AddRange(new Control[] { statRunning, statRam, statAfk, statSaver });

        // Viewer card
        var viewer = new Card(FlowDirection.LeftToRight) { Dock = DockStyle.Top, Margin = new Padding(0) };
        numTileW = Num(48, 1920, cfg.TileWidth);
        numTileH = Num(27, 1080, cfg.TileHeight);
        numCols = Num(0, 20, cfg.Columns);
        var head = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = new Padding(0, 0, 18, 6) };
        head.Controls.Add(H("Viewer"));
        head.Controls.Add(new Label { Text = "Tiny live previews of every window", AutoSize = true, ForeColor = Theme.Muted });
        viewer.Controls.Add(head);
        viewer.Controls.Add(Labeled("Tile width", Field(numTileW, 76)));
        viewer.Controls.Add(Labeled("Tile height", Field(numTileH, 76)));
        viewer.Controls.Add(Labeled("Columns", Field(numCols, 76)));
        viewer.Controls.Add(InlineBtn("▦  Open mini viewer", (_, _) => OpenViewer(), BtnKind.Primary));
        viewer.Controls.Add(InlineBtn("Tile real windows", (_, _) => TileAll()));
        viewer.Controls.Add(InlineBtn("Restore sizes", (_, _) => RestoreSizes()));
        viewer.Controls.Add(Note("Any size works. Hover a preview to pop that game up and make it active; click to keep it out, right-click to tuck it away. " +
                                 "Closing the viewer puts every window back."));

        var toolbar = Toolbar(
            new Control[]
            {
                Btn("Minimize all", (_, _) => ForEachWindow(h => Native.ShowWindow(h, Native.SW_MINIMIZE))),
                Btn("Show all", (_, _) => ForEachWindow(h => Native.ShowWindow(h, Native.SW_RESTORE))),
                Btn("Focus selected", (_, _) => FocusSelected()),
                Btn("Clean leftovers", (_, _) => CloseLeftovers())
            },
            new Control[]
            {
                Btn("Close selected", (_, _) => CloseInstances(SelectedPids()), BtnKind.Danger),
                Btn("Close all", (_, _) => { if (Confirm("Close every Roblox window?")) CloseInstances(RobloxPids()); }, BtnKind.Danger)
            });

        lvInstances = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false };
        lvInstances.Columns.Add("Account", 220);
        lvInstances.Columns.Add("PID", 90);
        lvInstances.Columns.Add("Window", 120);
        lvInstances.Columns.Add("RAM (MB)", 100);
        lvInstances.Columns.Add("Priority", 120);
        Theme.StyleList(lvInstances, statusCol: 2);
        lvInstances.DoubleClick += (_, _) => FocusSelected();

        page.Controls.Add(ListCard(lvInstances));
        page.Controls.Add(Spacer(12));
        page.Controls.Add(toolbar);
        page.Controls.Add(Spacer(14));
        page.Controls.Add(viewer);
        page.Controls.Add(Spacer(14));
        page.Controls.Add(stats);
        return page;
    }

    Control BuildAfkSaverPage()
    {
        var page = ScrollPage();

        // Anti-AFK
        var afk = new Card();
        afk.Controls.Add(H("Anti-AFK"));
        afk.Controls.Add(Note("Roblox kicks you after 20 minutes of no input. With this on, every few minutes MultiBlox un-minimizes each " +
                              "Roblox window, clicks the middle of it, then minimizes it again. Your mouse and the app you were using are put back afterwards."));
        chkAfk = new Toggle("Enable anti-AFK");
        chkAfkMin = new Toggle("Keep windows minimized", cfg.AfkMinimize);
        numAfk = Num(1, 19, cfg.AfkIntervalMin);
        afk.Controls.Add(chkAfk);
        afk.Controls.Add(Row(
            Labeled("Every (minutes)", Field(numAfk, 90)),
            InlineBtn("Pulse now", async (_, _) => await AfkPulse())));
        afk.Controls.Add(chkAfkMin);
        lblAfk = Status("Anti-AFK is off.");
        afk.Controls.Add(lblAfk);
        afk.Controls.Add(Note("Tip: avoid typing during a pulse - it takes about a second per window."));
        chkAfk.CheckedChanged += (_, _) => { SetAfk(chkAfk.Checked); UpdateStats(); };
        numAfk.ValueChanged += (_, _) => { if (chkAfk.Checked) nextAfk = DateTime.Now.AddMinutes((double)numAfk.Value); SaveConfig(); };
        chkAfkMin.CheckedChanged += (_, _) => SaveConfig();

        // Power saver
        var saver = new Card();
        saver.Controls.Add(H("Save computer usage"));
        saver.Controls.Add(Note("Makes every Roblox instance lighter on your PC. Re-applied every 15 seconds so new windows are covered too. " +
                                "Turning it off puts priority and CPU cores back to normal."));
        chkSaver = new Toggle("Enable power saver");
        chkLowPri = new Toggle("Lower Roblox CPU priority (keeps your other apps snappy)", cfg.SaverLowPriority);
        chkTrim = new Toggle("Trim RAM usage (pages out memory Roblox isn't using)", cfg.SaverTrimMemory);
        chkCores = new Toggle("Limit each instance to 2 CPU cores", cfg.SaverLimitCores);
        chkSaverFps = new Toggle("Launch new instances at low FPS", cfg.SaverLowFps);
        numSaverFps = Num(5, 60, cfg.SaverFps);
        var sub = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(14, 6, 0, 0) };
        sub.Controls.AddRange(new Control[] { chkLowPri, chkTrim, chkCores, chkSaverFps, Row(Labeled("Low FPS value", Field(numSaverFps, 90))) });
        saver.Controls.Add(chkSaver);
        saver.Controls.Add(sub);
        lblSaver = Status("Power saver is off.");
        saver.Controls.Add(lblSaver);
        saver.Controls.Add(Note("Minimized Roblox windows use far less GPU than visible ones, so anti-AFK with “Keep windows minimized” saves a lot by itself."));

        chkSaver.CheckedChanged += (_, _) => { SetSaver(chkSaver.Checked); UpdateStats(); };
        foreach (var c in new CheckBox[] { chkLowPri, chkTrim, chkCores, chkSaverFps })
            c.CheckedChanged += (_, _) => { SaveConfig(); if (chkSaver.Checked) { RestoreSaver(); ApplySaver(); } };
        numSaverFps.ValueChanged += (_, _) => SaveConfig();

        page.Controls.Add(afk);
        page.Controls.Add(saver);
        return page;
    }

    Control BuildFpsPage()
    {
        var page = ScrollPage();

        var cap = new Card();
        cap.Controls.Add(H("Frame-rate cap"));
        cap.Controls.Add(Note("Pick a target, then Apply. Every Roblox window you launch afterwards uses it."));
        numFps = Num(5, 9999, cfg.FpsCap);
        cap.Controls.Add(Row(
            Labeled("Target FPS", Field(numFps, 110)),
            InlineBtn("Apply", (_, _) => ApplyFps((int)numFps.Value, false), BtnKind.Primary),
            InlineBtn("Reset to default", (_, _) => ResetFps())));

        var presets = Row();
        presets.Margin = new Padding(0, 8, 0, 6);
        foreach (var (label, v) in new[] { ("30", 30), ("60", 60), ("120", 120), ("144", 144), ("165", 165), ("240", 240), ("Unlimited", 9999) })
        {
            var b = Btn(label, (_, _) => { numFps.Value = v; ApplyFps(v, false); });
            b.Margin = new Padding(0, 0, 6, 6);
            presets.Controls.Add(b);
        }
        cap.Controls.Add(new Label { Text = "PRESETS", AutoSize = true, Font = Theme.SmallBold, ForeColor = Theme.Muted, Margin = new Padding(2, 10, 0, 4) });
        cap.Controls.Add(presets);
        chkFpsAuto = new Toggle("Re-apply before every launch (needed after Roblox updates)", cfg.FpsAutoApply);
        chkFpsAuto.CheckedChanged += (_, _) => SaveConfig();
        numFps.ValueChanged += (_, _) => SaveConfig();
        cap.Controls.Add(chkFpsAuto);

        var live = new Card();
        live.Controls.Add(H("Changing FPS while in game"));
        live.Controls.Add(Note(
            "Roblox reads this setting when a window starts, so windows you launch after Apply get the new FPS. " +
            "Windows already running keep their FPS until you rejoin."));
        live.Controls.Add(Note(
            "For a live change inside a running game, use Roblox's own setting: Esc → Settings → “Maximum Frame Rate”. " +
            "If FPS seems stuck at 60 after applying a higher value, raise that setting too."));
        live.Controls.Add(Note(
            "MultiBlox deliberately doesn't edit Roblox's memory to change FPS live - old FPS unlockers did that, " +
            "and Roblox's anti-cheat flags it, which could get your accounts banned."));

        page.Controls.Add(cap);
        page.Controls.Add(live);
        return page;
    }
}
