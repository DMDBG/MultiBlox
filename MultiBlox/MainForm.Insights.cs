using System.Media;
using System.Net.Http.Json;

namespace MultiBlox;

/// <summary>Discord Stats extras: charts, goals with time-left estimates, and rare-drop alerts.</summary>
public partial class MainForm
{
    // ───────────────────────────── charts ─────────────────────────────

    DarkSelect cmbChartMode, cmbChartAcct, cmbChartDays;
    StatsChart chart;
    const string ResultsMode = "Wins & losses", AllAccounts = "All accounts";

    Control BuildChartCard()
    {
        var card = new Card();
        card.Controls.Add(H("Charts"));
        cmbChartMode = Combo(230);
        cmbChartAcct = Combo(190);
        cmbChartDays = Combo(130);
        cmbChartDays.Items.AddRange(new[] { "Last 7 days", "Last 14 days", "Last 30 days" });
        cmbChartDays.SelectedIndex = 1;
        card.Controls.Add(Row(
            Labeled("Show", cmbChartMode),
            Labeled("Account", cmbChartAcct),
            Labeled("Range", cmbChartDays)));
        chart = new StatsChart { Height = 220, Margin = new Padding(0, 6, 0, 4) };
        chart.Data = ChartData;
        card.Controls.Add(chart);
        card.SizeChanged += (_, _) => chart.Width = Math.Max(200, card.Width - card.Padding.Horizontal);
        foreach (var c in new[] { cmbChartMode, cmbChartAcct, cmbChartDays }) c.SelectedIndexChanged += (_, _) => chart.Invalidate();
        return card;
    }

    /// <summary>One bar per day: (day, value A, value B). Results mode: A = wins, B = losses. Item mode: A = amount.</summary>
    (List<(DateTime day, double a, double b)> rows, bool twoSeries, string label) ChartData()
    {
        int days = cmbChartDays.SelectedIndex switch { 0 => 7, 2 => 30, _ => 14 };
        string acct = cmbChartAcct.SelectedItem ?? AllAccounts;
        string mode = cmbChartMode.SelectedItem ?? ResultsMode;
        var ms = (acct == AllAccounts ? stats.Users.Values : stats.Users.Values.Where(u => u.Name == acct))
                 .SelectMany(u => u.Recent).ToList();
        var rows = new List<(DateTime, double, double)>();
        for (int i = days - 1; i >= 0; i--)
        {
            var day = DateTime.Today.AddDays(-i);
            var dm = ms.Where(m => m.Time.Date == day);
            if (mode == ResultsMode) rows.Add((day, dm.Count(m => m.Win), dm.Count(m => !m.Win)));
            else rows.Add((day, dm.Sum(m => m.Drops.Where(d => d.Item.Equals(mode, StringComparison.OrdinalIgnoreCase)).Sum(d => d.Amount)), 0));
        }
        return (rows, mode == ResultsMode, mode);
    }

    /// <summary>Keeps the chart/goal pickers in sync with whatever items and accounts have been seen.</summary>
    void RefreshInsights()
    {
        if (chart == null) return;
        var items = stats.Users.Values.SelectMany(u => u.Drops.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();
        Refill(cmbChartMode, new[] { ResultsMode }.Concat(items));
        Refill(cmbChartAcct, new[] { AllAccounts }.Concat(stats.Users.Values.OrderByDescending(u => u.LastMatch).Select(u => u.Name)));
        var suggestions = new AutoCompleteStringCollection();
        suggestions.AddRange(items.ToArray());
        cmbGoalItem.AutoCompleteCustomSource = suggestions;
        chart.Invalidate();
        RebuildGoalRows();
    }

    static void Refill(DarkSelect c, IEnumerable<string> values)
    {
        string keep = c.SelectedItem;
        c.Items.Clear();
        c.Items.AddRange(values);
        int i = keep == null ? 0 : c.Items.IndexOf(keep);
        c.SelectedIndex = c.Items.Count == 0 ? -1 : Math.Max(0, i);
        c.Invalidate();
    }

    // ───────────────────────────── goals ─────────────────────────────

    TextBox cmbGoalItem;
    NumericUpDown numGoal;
    FlowLayoutPanel goalRows;

    Control BuildGoalsCard()
    {
        var card = new Card();
        card.Controls.Add(H("Goals"));
        card.Controls.Add(Note("Set a target for any drop. Progress counts every account from the moment you add the goal, " +
                               "and the time left is based on how fast your accounts are farming it right now."));
        goalRows = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
        card.Controls.Add(goalRows);
        // A text box (not a list) so you can also type an item that hasn't dropped yet; known items are suggested.
        cmbGoalItem = new TextBox { PlaceholderText = "e.g. Summer Pearls", AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.CustomSource };
        numGoal = Num(1, 1_000_000_000, 10000);
        numGoal.ThousandsSeparator = true;
        card.Controls.Add(Row(
            Labeled("Item", Field(cmbGoalItem, 230)),
            Labeled("Target", Field(numGoal, 130)),
            InlineBtn("+  Add goal", (_, _) => AddGoal(), BtnKind.Primary)));
        card.SizeChanged += (_, _) => { foreach (Control r in goalRows.Controls) r.Width = Math.Max(200, card.Width - card.Padding.Horizontal); };
        return card;
    }

    void AddGoal()
    {
        string item = cmbGoalItem.Text.Trim();
        if (item == "") { Log("Pick or type an item for the goal."); return; }
        cfg.Goals.Add(new Goal { Item = item, Target = (double)numGoal.Value, Created = DateTime.Now });
        SaveConfig();
        RebuildGoalRows();
        Log($"Goal added: {numGoal.Value:N0} {item}.");
    }

    void RebuildGoalRows()
    {
        if (goalRows == null) return;
        goalRows.SuspendLayout();
        foreach (Control c in goalRows.Controls.Cast<Control>().ToList()) { goalRows.Controls.Remove(c); c.Dispose(); }
        if (cfg.Goals.Count == 0)
            goalRows.Controls.Add(new Label { Text = "No goals yet.", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 6) });
        int w = goalRows.Parent is Control p ? Math.Max(200, p.Width - p.Padding.Horizontal) : 600;
        foreach (var g in cfg.Goals)
        {
            double have = Rates.SumSince(stats, g.Item, g.Created);
            double rate = Rates.PerHourAll(stats, g.Item);
            var row = new GoalRow(g, have, rate) { Width = w };
            row.Removed += () => { cfg.Goals.Remove(g); SaveConfig(); RebuildGoalRows(); };
            goalRows.Controls.Add(row);
        }
        goalRows.ResumeLayout();
    }

    // ───────────────────────────── rare-drop alerts ─────────────────────────────

    TextBox txtAlertItems, txtAlertHook, txtAlertMention;
    Toggle chkAlertSound;
    NotifyIcon notify;
    static readonly HttpClient AlertHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    Control BuildAlertsCard()
    {
        var card = new Card();
        card.Controls.Add(H("Rare-drop alerts"));
        card.Controls.Add(Note("When a new result contains one of these drops you get a Windows notification - and optionally a ping " +
                               "in Discord (handy on your phone). Matching is by name, so “Portal” catches every portal."));
        txtAlertItems = new TextBox { Text = cfg.AlertItems, PlaceholderText = "e.g.  Sky Portal [T9], Secret, Mythic" };
        txtAlertHook = new TextBox { Text = cfg.AlertWebhook, PlaceholderText = "https://discord.com/api/webhooks/…  (optional)", UseSystemPasswordChar = cfg.AlertWebhook != "" };
        txtAlertMention = new TextBox { Text = cfg.AlertMention, PlaceholderText = "Your Discord user ID (optional)" };
        chkAlertSound = new Toggle("Play a sound", cfg.AlertSound) { Margin = new Padding(0, 22, 0, 0) };
        card.Controls.Add(Row(Labeled("Alert me when any of these drop (comma separated)", Field(txtAlertItems, 420)), chkAlertSound));
        card.Controls.Add(Row(
            Labeled("Also post alerts to this Discord webhook", Field(txtAlertHook, 340)),
            Labeled("Ping this user ID", Field(txtAlertMention, 200)),
            InlineBtn("Test alert", async (_, _) => await FireAlert("Test alert", "This is what a rare-drop alert looks like."))));
        foreach (var t in new[] { txtAlertItems, txtAlertHook, txtAlertMention }) t.Leave += (_, _) => SaveConfig();
        chkAlertSound.CheckedChanged += (_, _) => SaveConfig();
        return card;
    }

    void CheckAlerts(string user, MatchRecord m)
    {
        var words = cfg.AlertItems.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return;
        var hits = m.Drops.Where(d => words.Any(w => d.Item.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
        if (hits.Count == 0) return;
        _ = FireAlert("Rare drop!", $"{user} got {string.Join(", ", hits.Select(d => $"+{d.Amount:N0} {d.Item}"))}" +
                                    (string.IsNullOrEmpty(m.Map) ? "" : $" ({m.Map})"));
    }

    async Task FireAlert(string title, string text)
    {
        SaveConfig();
        Log($"🔔 {title} {text}");
        if (notify == null)
        {
            notify = new NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Information, Text = "MultiBlox", Visible = true };
            notify.BalloonTipClicked += (_, _) => { Show(); Activate(); };
            FormClosed += (_, _) => notify.Dispose();
        }
        notify.ShowBalloonTip(10000, title, text, ToolTipIcon.Info);
        if (cfg.AlertSound) SystemSounds.Exclamation.Play();

        string url = cfg.AlertWebhook;
        if (url == "") return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != "https" ||
            !(u.Host.EndsWith("discord.com") || u.Host.EndsWith("discordapp.com")) || !u.AbsolutePath.StartsWith("/api/webhooks/"))
        {
            Log("Alert webhook ignored - it should look like https://discord.com/api/webhooks/…");
            return;
        }
        bool ping = ulong.TryParse(cfg.AlertMention, out _);
        try
        {
            using var resp = await AlertHttp.PostAsJsonAsync(url, new
            {
                username = "MultiBlox",
                content = (ping ? $"<@{cfg.AlertMention}> " : "") + $"🔔 **{title}** {text}",
                allowed_mentions = new { users = ping ? new[] { cfg.AlertMention } : Array.Empty<string>() }
            });
            if (!resp.IsSuccessStatusCode) Log($"Alert webhook failed (HTTP {(int)resp.StatusCode}).");
        }
        catch (Exception ex) { Log("Alert webhook failed: " + ex.Message); }
    }
}

/// <summary>Daily bar chart: stacked wins/losses, or one item's amount per day. Hover a bar for its numbers.</summary>
class StatsChart : Control
{
    public Func<(List<(DateTime day, double a, double b)> rows, bool twoSeries, string label)> Data;
    int hover = -1;

    public StatsChart()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseMove(MouseEventArgs e) { int h = IndexAt(e.X); if (h != hover) { hover = h; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

    Rectangle Plot => new(LogicalToDeviceUnits(48), LogicalToDeviceUnits(26), Width - LogicalToDeviceUnits(56), Height - LogicalToDeviceUnits(50));
    int count;
    int IndexAt(int x) { var p = Plot; if (count == 0 || x < p.Left || x > p.Right) return -1; return Math.Min(count - 1, (x - p.Left) * count / Math.Max(1, p.Width)); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Card);
        if (Data == null) return;
        var (rows, two, label) = Data();
        count = rows.Count;
        var p = Plot;
        double max = rows.Count == 0 ? 0 : rows.Max(r => r.a + r.b);
        const TextFormatFlags R = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        const TextFormatFlags C = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine;

        if (max <= 0)
        {
            TextRenderer.DrawText(g, "No data in this range yet.", Theme.Body, p, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        double nice = Nice(max);

        // Grid + y labels
        using (var grid = new Pen(Theme.CardBorder))
            for (int i = 0; i <= 4; i++)
            {
                int y = p.Bottom - (int)(p.Height * i / 4.0);
                g.DrawLine(grid, p.Left, y, p.Right, y);
                TextRenderer.DrawText(g, Rates.Short(nice * i / 4), Theme.SmallBold, new Rectangle(0, y - 10, p.Left - 8, 20), Theme.Muted, R);
            }

        // Bars
        double slot = p.Width / (double)rows.Count;
        int bw = Math.Max(3, (int)(slot * 0.62));
        int labelEvery = rows.Count <= 7 ? 1 : rows.Count <= 14 ? 2 : 5;
        for (int i = 0; i < rows.Count; i++)
        {
            var (day, a, b) = rows[i];
            int x = p.Left + (int)(slot * i + (slot - bw) / 2);
            int ha = (int)(p.Height * a / nice), hb = (int)(p.Height * b / nice);
            bool hot = i == hover;
            if (a > 0) Theme.FillRound(g, new Rectangle(x, p.Bottom - ha, bw, ha), Math.Min(4, bw / 2), Shade(two ? Theme.Success : Theme.Accent, hot), Color.Empty);
            if (b > 0) Theme.FillRound(g, new Rectangle(x, p.Bottom - ha - hb, bw, hb), Math.Min(4, bw / 2), Shade(Theme.Danger, hot), Color.Empty);
            if (i % labelEvery == 0 || i == rows.Count - 1)
                TextRenderer.DrawText(g, day.ToString("M/d"), Theme.SmallBold, new Rectangle(x - 20, p.Bottom + 6, bw + 40, 16), Theme.Muted, C);
        }

        // Legend / hover readout, top right
        string info = hover >= 0
            ? $"{rows[hover].day:ddd MMM d}:  " + (two ? $"{rows[hover].a:N0} W  ·  {rows[hover].b:N0} L" : $"{rows[hover].a:N0} {label}")
            : two ? "■ wins   ■ losses   (hover a bar)" : $"{label} per day   (hover a bar)";
        TextRenderer.DrawText(g, info, Theme.SmallBold, new Rectangle(p.Left, 0, p.Width, LogicalToDeviceUnits(20)),
            hover >= 0 ? Theme.Text : Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    static Color Shade(Color c, bool hot) => hot ? ControlPaint.Light(c, 0.35f) : c;

    /// <summary>Axis max = 4 tidy steps of 1/2/5 × 10^n, so every grid label is a round number (0, 10, 20, 30, 40).</summary>
    static double Nice(double v)
    {
        double raw = v / 4, exp = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        foreach (var m in new[] { 1.0, 2, 5, 10 }) if (raw <= m * exp) return Math.Max(4, m * exp * 4);
        return 40 * exp;
    }
}

/// <summary>"Summer Pearls  3,200 / 10,000 · 32% · ~4h 20m left" with a progress bar and a remove ✕.</summary>
class GoalRow : Control
{
    readonly Goal goal;
    readonly double have, rate;
    bool hoverX;
    public event Action Removed;

    public GoalRow(Goal goal, double have, double rate)
    {
        this.goal = goal;
        this.have = have;
        this.rate = rate;
        Height = 54;
        Margin = new Padding(0, 0, 0, 8);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    Rectangle XBox => new(Width - LogicalToDeviceUnits(26), 0, LogicalToDeviceUnits(26), LogicalToDeviceUnits(24));
    protected override void OnMouseMove(MouseEventArgs e) { bool h = XBox.Contains(e.Location); if (h != hoverX) { hoverX = h; Cursor = h ? Cursors.Hand : Cursors.Default; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { hoverX = false; Invalidate(); }
    protected override void OnMouseClick(MouseEventArgs e) { if (XBox.Contains(e.Location)) Removed?.Invoke(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Card);
        int S(int v) => LogicalToDeviceUnits(v);
        bool done = have >= goal.Target;
        double pct = goal.Target <= 0 ? 1 : Math.Min(1, have / goal.Target);
        const TextFormatFlags L = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

        TextRenderer.DrawText(g, goal.Item, Theme.Semi, new Rectangle(0, 0, Width / 2, S(24)), Theme.Text, L);
        string detail = $"{have:N0} / {goal.Target:N0}  ·  {pct * 100:0}%  ·  " +
                        (done ? "Done! 🎉" : rate > 0 ? $"~{Rates.Duration(TimeSpan.FromHours((goal.Target - have) / rate))} left" : "waiting for drops");
        TextRenderer.DrawText(g, detail, Theme.Body, new Rectangle(Width / 2, 0, Width / 2 - S(32), S(24)), done ? Theme.Success : Theme.TextDim,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(g, "✕", Theme.Semi, XBox, hoverX ? Theme.Danger : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        var track = new Rectangle(0, S(32), Width - 1, S(10));
        Theme.FillRound(g, track, track.Height / 2, Theme.Input, Color.Empty);
        if (pct > 0)
            Theme.FillRound(g, new Rectangle(track.X, track.Y, Math.Max(track.Height, (int)(track.Width * pct)), track.Height), track.Height / 2,
                done ? Theme.Success : Theme.Accent, Color.Empty);
    }
}
