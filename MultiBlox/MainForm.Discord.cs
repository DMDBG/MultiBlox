namespace MultiBlox;

/// <summary>"Discord Stats" page: reads your macro's webhook channel and tallies wins, losses and drops per account.</summary>
public partial class MainForm
{
    StatsStore stats = StatsStore.Load();
    TextBox txtBotToken, txtChannel;
    Toggle chkDiscord;
    Label lblDiscord;
    FlowLayoutPanel discordPage;
    StatTile tWins, tLosses, tRate, tMatches;
    readonly HashSet<string> expanded = new(StringComparer.OrdinalIgnoreCase);
    Label accountsLabel;

    static Label SectionLabel(string t) => new() { Text = t, AutoSize = true, Font = Theme.SmallBold, ForeColor = Theme.Muted, Margin = new Padding(4, 8, 0, 8) };
    readonly List<Control> accountCards = new();
    readonly System.Windows.Forms.Timer discordTimer = new() { Interval = 20000 };
    bool polling;

    Control BuildDiscordPage()
    {
        var page = discordPage = ScrollPage();

        // Totals
        var totalsBox = new Panel { Height = 84, Margin = new Padding(0, 0, 0, 14) };   // fixed-height holder (tables shrink to 0 in a flow)
        var totals = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0) };
        totals.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        for (int i = 0; i < 4; i++) totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        totalsBox.Controls.Add(totals);
        tWins = new StatTile("Victories", Theme.Success);
        tLosses = new StatTile("Losses", Theme.Danger);
        tRate = new StatTile("Win rate", Theme.Accent);
        tMatches = new StatTile("Matches", Color.FromArgb(170, 120, 255)) { Margin = new Padding(0) };
        totals.Controls.AddRange(new Control[] { tWins, tLosses, tRate, tMatches });

        // Setup
        var setup = new Card();
        setup.Controls.Add(H("Discord drops tracker"));
        setup.Controls.Add(Note("Your macro keeps posting results to Discord like normal. MultiBlox reads that channel with your own bot, " +
                                "counts victories and losses, and adds up every drop for each account. Units are ignored."));
        bool haveToken = !string.IsNullOrEmpty(cfg.DiscordTokenEnc);
        txtBotToken = new TextBox { UseSystemPasswordChar = true, PlaceholderText = haveToken ? "Saved ✓ - paste a new one to replace" : "Paste your bot token" };
        txtChannel = new TextBox { Text = cfg.DiscordChannelId, PlaceholderText = "e.g. 1234567890123456789" };
        setup.Controls.Add(Row(
            Labeled("Bot token", Field(txtBotToken, 240)),
            Labeled("Webhook channel ID", Field(txtChannel, 200)),
            InlineBtn("Connect", async (_, _) => await ConnectDiscord(), BtnKind.Primary),
            InlineBtn("Check now", async (_, _) => await PollDiscord(true))));
        chkDiscord = new Toggle("Keep checking for new results (every 20 seconds)", cfg.DiscordEnabled);
        chkDiscord.CheckedChanged += (_, _) =>
        {
            SaveConfig();
            if (chkDiscord.Checked) { discordTimer.Start(); _ = PollDiscord(false); }
            else { discordTimer.Stop(); SetDiscordStatus("Paused.", Theme.Muted); }
        };
        setup.Controls.Add(chkDiscord);
        lblDiscord = Status(haveToken ? "Not checked yet." : "Not connected yet - follow the setup steps below.");
        setup.Controls.Add(lblDiscord);

        var help = Note(
            "1.  Go to discord.com/developers/applications → New Application → name it (e.g. MultiBlox).\n" +
            "2.  Open Bot → Reset Token → copy it and paste it above.  On the same page turn ON “Message Content Intent” and Save.\n" +
            "3.  Open OAuth2 → URL Generator → tick “bot”, then tick “View Channels” and “Read Message History”. " +
            "Open the link it makes and add the bot to your server.\n" +
            "4.  In Discord: Settings → Advanced → turn on Developer Mode. Right-click the channel your macro posts to → Copy Channel ID → paste above.\n" +
            "5.  Click Connect. The last 100 results are imported, then new ones are picked up automatically.\n\n" +
            "The token is stored encrypted for your Windows user only and is only sent to discord.com. The bot only reads - it only posts if you turn on Phone control below.");
        help.Visible = !haveToken;
        var helpBtn = Btn(help.Visible ? "Hide setup steps" : "Show setup steps", null);
        helpBtn.Click += (_, _) => { help.Visible = !help.Visible; helpBtn.Text = help.Visible ? "Hide setup steps" : "Show setup steps"; };
        helpBtn.Margin = new Padding(0, 4, 0, 8);
        setup.Controls.Add(helpBtn);
        setup.Controls.Add(help);
        setup.Controls.Add(Row(
            Btn("Reset stats", (_, _) =>
            {
                if (!Confirm("Clear all counted wins, losses and drops? (Discord messages aren't touched; Check now will re-import the last 100.)")) return;
                stats = new StatsStore { ChannelId = cfg.DiscordChannelId };
                SaveStats();
                RebuildAccountCards();
            }, BtnKind.Danger)));

        // Order: totals · chart · goals · ACCOUNTS (cards inserted here) · SETTINGS · setup · alerts
        page.Controls.Add(totalsBox);
        page.Controls.Add(BuildChartCard());
        page.Controls.Add(BuildGoalsCard());
        accountsLabel = SectionLabel("ACCOUNTS  ·  click one to see its drops");
        page.Controls.Add(accountsLabel);
        page.Controls.Add(SectionLabel("SETTINGS"));
        page.Controls.Add(setup);
        page.Controls.Add(BuildAlertsCard());
        page.Controls.Add(BuildCommandsCard());
        if (!haveToken) page.Controls.SetChildIndex(setup, 1);   // first time: setup goes right at the top

        discordTimer.Tick += async (_, _) => await PollDiscord(false);
        return page;
    }

    /// <summary>Called on startup once the window is ready.</summary>
    void StartDiscord()
    {
        RebuildAccountCards();
        if (cfg.DiscordEnabled && !string.IsNullOrEmpty(cfg.DiscordTokenEnc) && !string.IsNullOrEmpty(cfg.DiscordChannelId))
        {
            discordTimer.Start();
            _ = PollDiscord(false);
        }
        if (cfg.DiscordCommands) SetCommands(true);
    }

    void SetDiscordStatus(string text, Color color) { lblDiscord.Text = text; lblDiscord.ForeColor = color; }

    void SaveStats()
    {
        try { stats.Save(); } catch (Exception ex) { Log("Couldn't save Discord stats: " + ex.Message); }
    }

    string BotToken()
    {
        try { return string.IsNullOrEmpty(cfg.DiscordTokenEnc) ? "" : Dpapi.Unprotect(cfg.DiscordTokenEnc); } catch { return ""; }
    }

    async Task ConnectDiscord()
    {
        string token = txtBotToken.Text.Trim();
        if (token == "") token = BotToken();
        string channel = txtChannel.Text.Trim();
        if (token == "") { SetDiscordStatus("Paste your bot token first.", Theme.Danger); return; }
        if (!ulong.TryParse(channel, out _)) { SetDiscordStatus("The channel ID should be a long number (right-click the channel → Copy Channel ID).", Theme.Danger); return; }

        SetDiscordStatus("Connecting...", Theme.Warn);
        try
        {
            string name = await new DiscordReader(token, channel).TestAsync();
            cfg.DiscordTokenEnc = Dpapi.Protect(token);
            cfg.DiscordChannelId = channel;
            cfg.DiscordChannelName = name;
            txtBotToken.Text = "";
            txtBotToken.PlaceholderText = "Saved ✓ - paste a new one to replace";
            Log($"Discord: connected to {name}.");
            if (!chkDiscord.Checked) chkDiscord.Checked = true;   // starts the timer and the first read
            else await PollDiscord(true);
            SaveConfig();
        }
        catch (Exception ex) { SetDiscordStatus("✖ " + ex.Message, Theme.Danger); }
    }

    async Task PollDiscord(bool manual)
    {
        if (polling) return;
        string token = BotToken(), channel = cfg.DiscordChannelId;
        if (token == "" || channel == "") { if (manual) SetDiscordStatus("Connect first (bot token + channel ID).", Theme.Danger); return; }
        polling = true;
        try
        {
            if (stats.ChannelId != channel) stats = new StatsStore { ChannelId = channel };   // different channel = fresh tally
            bool firstImport = string.IsNullOrEmpty(stats.LastMessageId);   // don't fire alerts for old history
            var msgs = await new DiscordReader(token, channel).FetchNewAsync(stats.LastMessageId);
            int added = 0, blank = 0;
            foreach (var m in msgs)
            {
                var r = WebhookParser.Parse(m);
                if (r != null)
                {
                    var (user, match) = r.Value;
                    int before = stats.User(user).Matches;
                    stats.Add(user, match);
                    if (stats.User(user).Matches > before)
                    {
                        added++;
                        if (!firstImport) CheckAlerts(user, match);
                    }
                }
                else if (IsBlank(m)) blank++;
            }
            if (msgs.Count > 0) stats.LastMessageId = msgs.Select(DiscordReader.Id).Max().ToString();
            SaveStats();
            if (added > 0)
            {
                Log($"Discord: counted {added} new match result{(added == 1 ? "" : "s")}.");
                RebuildAccountCards();
            }
            if (blank > 0 && added == 0)
                SetDiscordStatus("Messages are arriving empty - turn ON “Message Content Intent” on your bot's page in the Developer Portal.", Theme.Warn);
            else
                SetDiscordStatus($"Connected to {cfg.DiscordChannelName}  ·  checked {DateTime.Now:h:mm:ss tt}" +
                                 (added > 0 ? $"  ·  {added} new" : ""), Theme.Success);
        }
        catch (Exception ex) { SetDiscordStatus("✖ " + ex.Message, Theme.Danger); }
        finally { polling = false; }
    }

    /// <summary>A webhook/bot message with no text and no embeds = Discord is hiding content (missing intent).</summary>
    static bool IsBlank(System.Text.Json.JsonElement m)
    {
        bool hasEmbeds = m.TryGetProperty("embeds", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.Array && e.GetArrayLength() > 0;
        bool hasText = m.TryGetProperty("content", out var c) && !string.IsNullOrEmpty(c.GetString());
        bool fromBot = m.TryGetProperty("webhook_id", out _) ||
                       (m.TryGetProperty("author", out var a) && a.TryGetProperty("bot", out var b) && b.ValueKind == System.Text.Json.JsonValueKind.True);
        return fromBot && !hasEmbeds && !hasText;
    }

    // ───────────────────────────── account cards ─────────────────────────────

    void RebuildAccountCards()
    {
        if (discordPage == null) return;
        discordPage.SuspendLayout();
        foreach (var c in accountCards) { discordPage.Controls.Remove(c); c.Dispose(); }
        accountCards.Clear();

        // Your MultiBlox accounts first (in order), then anyone else the webhooks mention.
        var names = cfg.Accounts.Select(a => a.Name).ToList();
        names.AddRange(stats.Users.Values.Where(u => !names.Contains(u.Name, StringComparer.OrdinalIgnoreCase))
                                          .OrderByDescending(u => u.LastMatch).Select(u => u.Name));
        foreach (var name in names)
        {
            var u = stats.Users.TryGetValue(name, out var s) ? s : new UserStats { Name = name };
            var card = new AccountStatsCard(u, expanded.Contains(name), cfg.Accounts.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
            card.Toggled += open => { if (open) expanded.Add(name); else expanded.Remove(name); };
            accountCards.Add(card);
        }
        if (names.Count == 0)
        {
            var empty = new Card();
            empty.Controls.Add(Note("No results yet. Once your macro posts a match result to the channel, each account shows up here."));
            accountCards.Add(empty);
        }
        // Insert the cards right under the ACCOUNTS heading (settings stay below them).
        int at = discordPage.Controls.GetChildIndex(accountsLabel) + 1;
        for (int i = 0; i < accountCards.Count; i++)
        {
            discordPage.Controls.Add(accountCards[i]);
            discordPage.Controls.SetChildIndex(accountCards[i], at + i);
        }
        discordPage.ResumeLayout();
        RefreshInsights();

        int w = stats.Users.Values.Sum(u => u.Wins), l = stats.Users.Values.Sum(u => u.Losses);
        tWins.Value = w.ToString("N0");
        tLosses.Value = l.ToString("N0");
        tMatches.Value = (w + l).ToString("N0");
        tRate.Value = w + l == 0 ? "-" : $"{100.0 * w / (w + l):0}%";
    }

    internal static string Ago(DateTime t)
    {
        if (t == default) return "no matches yet";
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes}m ago";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours}h ago";
        return $"{(int)d.TotalDays}d ago";
    }
}

/// <summary>One account's card: a clickable summary row that expands to show drops and recent matches.</summary>
class AccountStatsCard : Card
{
    readonly UserStats u;
    readonly bool isMine;
    readonly Control header;
    readonly FlowLayoutPanel details, chips;
    bool open;
    public event Action<bool> Toggled;

    public AccountStatsCard(UserStats u, bool open, bool isMine)
    {
        this.u = u;
        this.open = open;
        this.isMine = isMine;
        Padding = new Padding(16, 6, 16, 6);
        Margin = new Padding(0, 0, 0, 10);
        Cursor = Cursors.Hand;

        header = new HeaderRow(this) { Height = 50, Margin = new Padding(0) };
        header.Click += (_, _) => Toggle();
        Click += (_, _) => Toggle();   // the card's padding counts too
        Controls.Add(header);

        details = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, 8), Visible = open, Cursor = Cursors.Default };
        details.Controls.Add(Caption("DROPS (TOTAL)"));
        chips = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 6) };
        if (u.Drops.Count == 0) chips.Controls.Add(new Label { Text = "No drops recorded yet.", AutoSize = true, ForeColor = Theme.Muted });
        foreach (var kv in u.Drops.OrderByDescending(kv => kv.Value))
            chips.Controls.Add(new Chip(kv.Key, kv.Value, Rates.PerHour(u.Recent, kv.Key)));
        details.Controls.Add(chips);

        details.Controls.Add(Caption("RECENT MATCHES"));
        if (u.Recent.Count == 0) details.Controls.Add(new Label { Text = "None yet.", AutoSize = true, ForeColor = Theme.Muted });
        foreach (var m in u.Recent.Take(10))
        {
            string bits = string.Join("  ·  ", new[]
            {
                m.Win ? "✔ Victory" : "✖ Loss",
                m.Map,
                m.Wave > 0 ? $"Wave {m.Wave}" : "",
                m.Duration,
                m.Time.ToString("MMM d, h:mm tt")
            }.Where(s => !string.IsNullOrEmpty(s)));
            string drops = m.Drops.Count == 0 ? "" : "     " + string.Join(", ", m.Drops.Select(d => $"+{d.Amount:N0} {d.Item}"));
            details.Controls.Add(new Label
            {
                Text = bits + drops, AutoSize = true, UseMnemonic = false, Tag = "note",
                ForeColor = m.Win ? Theme.TextDim : Color.FromArgb(220, 150, 150), Margin = new Padding(0, 2, 0, 2)
            });
        }
        Controls.Add(details);
    }

    static Label Caption(string t) => new() { Text = t, AutoSize = true, Font = Theme.SmallBold, ForeColor = Theme.Muted, Margin = new Padding(0, 6, 0, 6) };

    void Toggle()
    {
        open = !open;
        details.Visible = open;
        header.Invalidate();
        Toggled?.Invoke(open);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        int w = Width - Padding.Horizontal;
        if (w > 50 && header.Width != w)
        {
            header.Width = w;
            chips.MaximumSize = new Size(w, 0);
        }
    }

    /// <summary>Chevron · name · W/L pills · win rate · last played.</summary>
    class HeaderRow : Control
    {
        readonly AccountStatsCard card;
        public HeaderRow(AccountStatsCard card)
        {
            this.card = card;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            var u = card.u;
            int S(int v) => LogicalToDeviceUnits(v);
            const TextFormatFlags L = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

            TextRenderer.DrawText(g, card.open ? "▾" : "▸", Theme.H2, new Rectangle(0, 0, S(24), Height), Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
            int x = S(24);
            var nameSize = TextRenderer.MeasureText(u.Name, Theme.H2);
            TextRenderer.DrawText(g, u.Name, Theme.H2, new Rectangle(x, 0, Math.Min(nameSize.Width + 4, Width / 2), Height), Theme.Text, L);
            x += Math.Min(nameSize.Width + 4, Width / 2) + S(8);
            if (!card.isMine)
            {
                var tag = "not in MultiBlox";
                TextRenderer.DrawText(g, tag, Theme.SmallBold, new Rectangle(x, 0, S(120), Height), Theme.Muted, L);
            }

            // Right side, drawn right-to-left: last played · win rate · L pill · W pill
            int right = Width;
            string ago = MainForm.Ago(u.LastMatch);
            int aw = TextRenderer.MeasureText(ago, Theme.Body).Width;
            TextRenderer.DrawText(g, ago, Theme.Body, new Rectangle(right - aw, 0, aw, Height), Theme.Muted, L);
            right -= aw + S(16);

            string rate = u.Matches == 0 ? "-" : $"{100.0 * u.Wins / u.Matches:0}%";
            int rw = Math.Max(S(42), TextRenderer.MeasureText(rate, Theme.Semi).Width);
            TextRenderer.DrawText(g, rate, Theme.Semi, new Rectangle(right - rw, 0, rw, Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine);
            right -= rw + S(14);

            right = Pill(g, right, $"{u.Losses} L", Theme.Danger, Color.FromArgb(52, 28, 32)) - S(6);
            Pill(g, right, $"{u.Wins} W", Theme.Success, Color.FromArgb(22, 50, 38));
        }

        int Pill(Graphics g, int right, string text, Color fg, Color bg)
        {
            int S(int v) => LogicalToDeviceUnits(v);
            int w = TextRenderer.MeasureText(text, Theme.Semi).Width + S(18), h = S(24);
            var r = new Rectangle(right - w, (Height - h) / 2, w, h);
            Theme.FillRound(g, r, h / 2, bg, Color.Empty);
            TextRenderer.DrawText(g, text, Theme.Semi, r, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            return r.Left;
        }
    }

    /// <summary>"Summer Pearls  27,390  5.4k/hr" badge (rate based on the last 24h).</summary>
    class Chip : Control
    {
        readonly string item, amount, rate;
        public Chip(string item, double amount, double perHour)
        {
            this.item = item;
            this.amount = amount >= 1_000_000 ? $"{amount / 1_000_000:0.##}M" : amount.ToString("N0");
            rate = perHour > 0 ? Rates.Short(perHour) + "/hr" : "";
            Margin = new Padding(0, 0, 8, 8);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            int w = TextRenderer.MeasureText(item, Theme.Body).Width + TextRenderer.MeasureText(this.amount, Theme.Semi).Width + 34
                    + (rate == "" ? 0 : TextRenderer.MeasureText(rate, Theme.SmallBold).Width + 10);
            Size = new Size(w, 32);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            Theme.FillRound(g, new Rectangle(0, 0, Width - 1, Height - 1), 8, Theme.Input, Theme.CardBorder);
            int iw = TextRenderer.MeasureText(item, Theme.Body).Width;
            int aw = TextRenderer.MeasureText(amount, Theme.Semi).Width;
            const TextFormatFlags L = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, item, Theme.Body, new Rectangle(12, 0, iw, Height), Theme.TextDim, L);
            TextRenderer.DrawText(g, amount, Theme.Semi, new Rectangle(12 + iw + 8, 0, aw, Height), Theme.Text, L);
            if (rate != "") TextRenderer.DrawText(g, rate, Theme.SmallBold, new Rectangle(12 + iw + 8 + aw + 6, 0, Width, Height), Theme.Success, L);
        }
    }
}
