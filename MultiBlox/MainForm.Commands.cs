using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MultiBlox;

/// <summary>
/// "Join a user", and phone control: the Discord bot also listens for commands like !status or !rejoin in a channel
/// and replies there. Only Discord user IDs on the allowed list can use it.
/// </summary>
public partial class MainForm
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);

    Toggle chkCommands;
    TextBox txtCmdUsers, txtCmdChannel;
    Label lblCommands;
    readonly System.Windows.Forms.Timer commandTimer = new() { Interval = 5000 };
    string commandCursor, commandCursorChannel;   // newest message already seen (null = start from "now")
    bool commandBusy;

    static readonly string[] KnownCommands = { "!help", "!status", "!stats", "!screenshot", "!ss", "!launch", "!rejoin", "!close", "!join" };

    const string CommandHelp =
        "**MultiBlox commands**\n" +
        "`!status` - what's running right now\n" +
        "`!stats` - wins, losses and drops from the last 24 hours\n" +
        "`!screenshot` - a picture of every game\n" +
        "`!launch [account|all]` - launch accounts that aren't running (all = your checked accounts)\n" +
        "`!rejoin [account|all]` - restart games back into the same server\n" +
        "`!close <account|all>` - close games\n" +
        "`!join <username>` - send your checked accounts to that player's game\n" +
        "Names can be shortened, e.g. `!rejoin ali` for AliFarm1. Several: `!rejoin ali, bob`.";

    // ───────────────────────────── join a user ─────────────────────────────

    async Task JoinUserPrompt()
    {
        var accs = SelectedAccounts();
        if (accs.Count == 0) accs = cfg.Accounts.Where(a => a.Checked).ToList();
        if (accs.Count == 0) { Log("Select or check the accounts that should join."); return; }
        string who = Prompt.Show(this, $"Roblox username to join with {accs.Count} account{(accs.Count == 1 ? "" : "s")}.\n" +
            "They must be in a game and allow joins.", "");
        if (string.IsNullOrWhiteSpace(who)) return;
        who = who.Trim();
        if (!RobloxApi.IsUserRef(who)) who = "@" + who.TrimStart('@');
        if (!RobloxApi.IsUserRef(who)) { MessageBox.Show(this, "That doesn't look like a Roblox username.", "MultiBlox"); return; }
        await JoinUser(accs, who);
    }

    /// <summary>Moves accounts into a player's game. Their current games are closed first so Roblox doesn't kick the old session.</summary>
    async Task<string> JoinUser(List<Account> accs, string who)
    {
        if (launching) return "A launch is already running - try again when it's done.";
        string name = who.TrimStart('@');
        accs = accs.Where(a => !a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();   // the player being joined stays put
        if (accs.Count == 0) return "No other accounts to send.";
        bool closed = false;
        foreach (var a in accs) closed |= CloseAccountGame(a);
        if (closed) await Task.Delay(2000);
        Log($"Joining {name} with {accs.Count} account(s)...");
        _ = LaunchMany(accs, who);
        return $"Sending {string.Join(", ", accs.Select(a => Esc(a.Name)))} to {Esc(name)}'s game...";
    }

    int PidOf(Account a) => pidOwner.FirstOrDefault(kv => kv.Value.Equals(a.Name, StringComparison.OrdinalIgnoreCase)).Key;

    bool CloseAccountGame(Account a)
    {
        int pid = PidOf(a);
        if (pid == 0) return false;
        CloseInstances(new[] { pid });
        pidOwner.Remove(pid);
        SetStatus(a, "Closed");
        return true;
    }

    // ───────────────────────────── settings card ─────────────────────────────

    Control BuildCommandsCard()
    {
        var card = new Card();
        card.Controls.Add(H("Phone control"));
        card.Controls.Add(Note("Control MultiBlox from Discord, e.g. on your phone. Type a command like !status in the channel and your bot replies. " +
                               "Only the Discord users listed here can use it - everyone else is ignored."));
        txtCmdUsers = new TextBox { Text = cfg.DiscordCommandUsers != "" ? cfg.DiscordCommandUsers : cfg.AlertMention, PlaceholderText = "Your Discord user ID (commas for several)" };
        txtCmdChannel = new TextBox { Text = cfg.DiscordCommandChannelId, PlaceholderText = "Empty = the webhook channel" };
        card.Controls.Add(Row(
            Labeled("Who can send commands (user IDs)", Field(txtCmdUsers, 300)),
            Labeled("Commands channel ID", Field(txtCmdChannel, 220))));
        chkCommands = new Toggle("Listen for commands (checks every 5 seconds)", cfg.DiscordCommands);
        chkCommands.CheckedChanged += (_, _) => { SaveConfig(); SetCommands(chkCommands.Checked); };
        card.Controls.Add(chkCommands);
        lblCommands = Status(cfg.DiscordCommands ? "Starting..." : "Off.");
        card.Controls.Add(lblCommands);
        card.Controls.Add(Note(CommandHelp.Replace("**", "").Replace("`", "")));
        card.Controls.Add(Note("Your bot needs two extra permissions in that channel: Send Messages and Attach Files " +
                               "(channel → Edit Channel → Permissions → add your bot). Your user ID: right-click your name → Copy User ID."));
        foreach (var t in new[] { txtCmdUsers, txtCmdChannel })
            t.Leave += (_, _) => { SaveConfig(); if (chkCommands.Checked) SetCommands(true); };
        commandTimer.Tick += async (_, _) => await PollCommands();
        return card;
    }

    void SetCommandStatus(string text, Color color) { lblCommands.Text = text; lblCommands.ForeColor = color; }

    string CommandChannel => cfg.DiscordCommandChannelId.Trim() != "" ? cfg.DiscordCommandChannelId.Trim() : cfg.DiscordChannelId;

    HashSet<string> CommandUsers() =>
        cfg.DiscordCommandUsers.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .Where(s => ulong.TryParse(s, out _)).ToHashSet();

    void SetCommands(bool on)
    {
        commandTimer.Stop();
        commandCursor = null;   // start from "now" - commands sent while it was off never fire
        if (!on) { SetCommandStatus("Off.", Theme.Muted); return; }
        if (BotToken() == "" || CommandChannel == "") { SetCommandStatus("Connect your bot above first (token + channel).", Theme.Danger); return; }
        if (cfg.DiscordCommandChannelId.Trim() != "" && !ulong.TryParse(cfg.DiscordCommandChannelId.Trim(), out _))
        { SetCommandStatus("The commands channel ID should be a long number.", Theme.Danger); return; }
        if (CommandUsers().Count == 0) { SetCommandStatus("Add your Discord user ID first, so only you can send commands.", Theme.Danger); return; }
        commandTimer.Start();
        _ = PollCommands();
    }

    // ───────────────────────────── polling ─────────────────────────────

    async Task PollCommands()
    {
        if (commandBusy) return;
        string token = BotToken(), channel = CommandChannel;
        if (token == "" || channel == "") return;
        commandBusy = true;
        try
        {
            var reader = new DiscordReader(token, channel);
            if (commandCursor == null || commandCursorChannel != channel)
            {
                var latest = await reader.FetchNewAsync("");
                commandCursor = latest.Count > 0 ? latest.Select(DiscordReader.Id).Max().ToString() : "0";
                commandCursorChannel = channel;
                Log("Phone control on - type !help in Discord to see the commands.");
            }
            else
            {
                var msgs = await reader.FetchNewAsync(commandCursor);
                if (msgs.Count > 0) commandCursor = msgs.Select(DiscordReader.Id).Max().ToString();   // moved past first: nothing runs twice
                var allowed = CommandUsers();
                foreach (var m in msgs)
                {
                    string text = m.TryGetProperty("content", out var c) ? (c.GetString() ?? "").Trim() : "";
                    if (!text.StartsWith('!') || m.TryGetProperty("webhook_id", out _)) continue;
                    var author = m.GetProperty("author");
                    if (author.TryGetProperty("bot", out var b) && b.ValueKind == JsonValueKind.True) continue;
                    string word = text.Split(' ', 2)[0].ToLowerInvariant();
                    if (!KnownCommands.Contains(word)) continue;
                    string uid = author.GetProperty("id").GetString();
                    if (!allowed.Contains(uid))
                    {
                        Log($"Discord: ignored {word} from {author.GetProperty("username").GetString()} - they're not on your allowed list.");
                        continue;
                    }
                    string msgId = m.GetProperty("id").GetString();
                    try { await RunCommand(reader, msgId, text); }
                    catch (Exception ex)
                    {
                        Log($"Discord command {word} failed: {ex.Message}");
                        try { await reader.SendAsync("⚠️ " + ex.Message, msgId); } catch { }
                    }
                }
            }
            SetCommandStatus($"Listening in {(channel == cfg.DiscordChannelId ? cfg.DiscordChannelName : "channel " + channel)}  ·  checked {DateTime.Now:h:mm:ss tt}", Theme.Success);
        }
        catch (Exception ex) { SetCommandStatus("✖ " + ex.Message, Theme.Danger); }
        finally { commandBusy = false; }
    }

    async Task RunCommand(DiscordReader r, string replyTo, string text)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string cmd = parts[0].ToLowerInvariant(), arg = parts.Length > 1 ? parts[1] : "";
        Log($"Discord command: {text}");
        switch (cmd)
        {
            case "!help":
                await r.SendAsync(CommandHelp, replyTo);
                break;

            case "!status":
                await r.SendAsync(StatusText(), replyTo);
                break;

            case "!stats":
                await r.SendAsync(StatsText(), replyTo);
                break;

            case "!screenshot":
            case "!ss":
            {
                var jpg = CaptureGames(out int n, out int blank);
                if (jpg == null) { await r.SendAsync("No Roblox games are open.", replyTo); break; }
                await r.SendAsync($"{n} game{(n == 1 ? "" : "s")} at {DateTime.Now:h:mm tt}" +
                                  (blank > 0 ? $" · {blank} minimized (no picture)" : ""), replyTo, jpg);
                break;
            }

            case "!launch":
            {
                if (launching) { await r.SendAsync("A launch is already running - try again when it's done.", replyTo); break; }
                var accs = PickAccounts(arg, cfg.Accounts.Where(a => a.Checked), out string err);
                if (err != null) { await r.SendAsync(err, replyTo); break; }
                accs = accs.Where(a => PidOf(a) == 0).ToList();
                if (accs.Count == 0) { await r.SendAsync("Those accounts are already running.", replyTo); break; }
                _ = LaunchMany(accs);
                await r.SendAsync($"▶ Launching {Names(accs)}...", replyTo);
                break;
            }

            case "!rejoin":
            {
                if (launching) { await r.SendAsync("A launch is already running - try again when it's done.", replyTo); break; }
                var running = cfg.Accounts.Where(a => PidOf(a) != 0).ToList();
                var accs = PickAccounts(arg, running.Count > 0 ? running : cfg.Accounts.Where(a => a.Checked), out string err);
                if (err != null) { await r.SendAsync(err, replyTo); break; }
                if (accs.Count == 0) { await r.SendAsync("No accounts to rejoin.", replyTo); break; }
                // Remember where each one was, so it goes back to the same game/server.
                var targets = new Dictionary<Account, ServerTarget>();
                foreach (var a in accs)
                {
                    int pid = PidOf(a);
                    if (pid != 0 && sessions.TryGetValue(pid, out var s)) targets[a] = s.Target;
                }
                bool closed = false;
                foreach (var a in accs) closed |= CloseAccountGame(a);
                if (closed) await Task.Delay(3000);
                _ = LaunchMany(accs, null, a => targets.TryGetValue(a, out var t) ? t : null);
                await r.SendAsync($"🔄 Rejoining {Names(accs)}...", replyTo);
                break;
            }

            case "!close":
            {
                if (arg == "") { await r.SendAsync("Say which: `!close <account>` or `!close all`.", replyTo); break; }
                if (arg.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    int n = RobloxPids().Count;
                    CloseInstances(RobloxPids());
                    await r.SendAsync($"⏹ Closed {n} game{(n == 1 ? "" : "s")}.", replyTo);
                    break;
                }
                var accs = PickAccounts(arg, cfg.Accounts, out string err);
                if (err != null) { await r.SendAsync(err, replyTo); break; }
                var done = accs.Where(CloseAccountGame).ToList();
                await r.SendAsync(done.Count == 0 ? "Those accounts weren't running." : $"⏹ Closed {Names(done)}.", replyTo);
                break;
            }

            case "!join":
            {
                string who = "@" + arg.Trim().TrimStart('@');
                if (!RobloxApi.IsUserRef(who)) { await r.SendAsync("Usage: `!join <roblox username>`", replyTo); break; }
                var accs = cfg.Accounts.Where(a => a.Checked).ToList();
                if (accs.Count == 0) { await r.SendAsync("No accounts are checked in MultiBlox.", replyTo); break; }
                await r.SendAsync(await JoinUser(accs, who), replyTo);
                break;
            }
        }
    }

    /// <summary>"" or "all" → the default set; otherwise names, matched exactly or by a unique start/part of the name.</summary>
    List<Account> PickAccounts(string arg, IEnumerable<Account> all, out string error)
    {
        error = null;
        if (arg == "" || arg.Equals("all", StringComparison.OrdinalIgnoreCase)) return all.ToList();
        var result = new List<Account>();
        foreach (var raw in arg.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var exact = cfg.Accounts.FirstOrDefault(a => a.Name.Equals(raw, StringComparison.OrdinalIgnoreCase));
            var matches = exact != null ? new List<Account> { exact }
                : cfg.Accounts.Where(a => a.Name.StartsWith(raw, StringComparison.OrdinalIgnoreCase)).ToList() is { Count: > 0 } starts ? starts
                : cfg.Accounts.Where(a => a.Name.Contains(raw, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0) { error = $"No account matches “{raw}”."; return null; }
            if (matches.Count > 1) { error = $"“{raw}” matches {Names(matches)} - type more of the name."; return null; }
            if (!result.Contains(matches[0])) result.Add(matches[0]);
        }
        return result;
    }

    // ───────────────────────────── replies ─────────────────────────────

    static string Esc(string s) => s.Replace("\\", "\\\\").Replace("_", "\\_").Replace("*", "\\*").Replace("`", "\\`").Replace("~", "\\~");
    static string Names(IEnumerable<Account> accs) => string.Join(", ", accs.Select(a => Esc(a.Name)));

    string StatusText()
    {
        var pids = RobloxPids();
        long ram = 0;
        var lines = new List<string>();
        foreach (var a in cfg.Accounts)
        {
            int pid = PidOf(a);
            if (pid == 0 || !pids.Contains(pid)) { lines.Add($"⚫ **{Esc(a.Name)}** - not running"); continue; }
            string up = "", mem = "";
            try
            {
                using var p = Process.GetProcessById(pid);
                ram += p.WorkingSet64;
                mem = $" · {p.WorkingSet64 >> 20} MB";
                var start = sessions.TryGetValue(pid, out var s) ? s.Started : p.StartTime;
                up = Rates.Duration(DateTime.Now - start);
            }
            catch { }
            bool hung = Native.FindRobloxWindow(pid) is var h && h != IntPtr.Zero && IsHungAppWindow(h);
            lines.Add($"{(hung ? "🟠" : "🟢")} **{Esc(a.Name)}** - {(hung ? "not responding" : "running")} {up}{mem}");
        }
        int outside = pids.Count(p => !pidOwner.ContainsKey(p) && Native.FindRobloxWindow(p) != IntPtr.Zero);
        if (outside > 0) lines.Add($"➕ {outside} game{(outside == 1 ? "" : "s")} opened outside MultiBlox");

        string ramText = ram >= 1L << 30 ? $"{ram / (double)(1L << 30):0.0} GB" : $"{ram >> 20} MB";
        return $"**MultiBlox** · Multi-Roblox {(multiOn ? "ON" : "OFF")} · {RobloxWindowMap().Count} running · {ramText}\n" +
               string.Join("\n", lines) + "\n" +
               $"Anti-AFK {(chkAfk.Checked ? "on" : "off")} · Power saver {(chkSaver.Checked ? "on" : "off")} · Auto-rejoin {(chkRejoin.Checked ? "on" : "off")}";
    }

    string StatsText()
    {
        var since = DateTime.Now.AddHours(-24);
        var sb = new StringBuilder("**Last 24 hours**\n");
        int tw = 0, tl = 0;
        foreach (var u in stats.Users.Values.OrderByDescending(u => u.LastMatch))
        {
            var ms = u.Recent.Where(m => m.Time >= since).ToList();
            if (ms.Count == 0) continue;
            int w = ms.Count(m => m.Win), l = ms.Count - w;
            tw += w; tl += l;
            var top = ms.SelectMany(m => m.Drops).GroupBy(d => d.Item, StringComparer.OrdinalIgnoreCase)
                        .Select(g => (item: g.Key, amt: g.Sum(d => d.Amount))).OrderByDescending(x => x.amt).Take(3)
                        .Select(x => $"{Rates.Short(x.amt)} {Esc(x.item)}");
            sb.Append($"**{Esc(u.Name)}** - {w}W / {l}L ({100.0 * w / ms.Count:0}%)");
            string drops = string.Join(", ", top);
            sb.Append(drops == "" ? "\n" : $" · {drops}\n");
        }
        if (tw + tl == 0) return "No match results in the last 24 hours" + (cfg.DiscordEnabled ? "." : " - stats checking is turned off in MultiBlox.");
        sb.Append($"**Total** - {tw}W / {tl}L ({100.0 * tw / (tw + tl):0}%)");
        return sb.ToString();
    }

    // ───────────────────────────── screenshot ─────────────────────────────

    /// <summary>One picture with every game in it, in your saved order. Minimized games can't be captured.</summary>
    byte[] CaptureGames(out int count, out int blank)
    {
        var wins = Ordered(RobloxPids()).Select(p => (pid: p, h: Native.FindRobloxWindow(p))).Where(x => x.h != IntPtr.Zero).ToList();
        count = wins.Count;
        blank = 0;
        if (count == 0) return null;

        const int TW = 480, TH = 270, LH = 28;
        int cols = count == 1 ? 1 : count <= 4 ? 2 : count <= 9 ? 3 : 4;
        int rows = (count + cols - 1) / cols;
        using var sheet = new Bitmap(cols * TW, rows * (TH + LH));
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(17, 19, 26));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using var font = new Font("Segoe UI Semibold", 11f);
            using var dim = new SolidBrush(Color.FromArgb(150, 156, 170));
            for (int i = 0; i < count; i++)
            {
                var (pid, h) = wins[i];
                int x = (i % cols) * TW, y = (i / cols) * (TH + LH);
                string name = pidOwner.TryGetValue(pid, out var n) ? n : $"Roblox ({pid})";
                TextRenderer.DrawText(g, name, font, new Rectangle(x + 8, y, TW - 16, LH), Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                var area = new Rectangle(x + 4, y + LH, TW - 8, TH - 6);
                using var shot = CaptureWindow(h);
                if (shot == null)
                {
                    blank++;
                    TextRenderer.DrawText(g, "minimized - no picture", font, area, dim.Color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    continue;
                }
                double s = Math.Min(area.Width / (double)shot.Width, area.Height / (double)shot.Height);
                int w = (int)(shot.Width * s), hh = (int)(shot.Height * s);
                g.DrawImage(shot, area.X + (area.Width - w) / 2, area.Y + (area.Height - hh) / 2, w, hh);
            }
        }
        using var ms = new MemoryStream();
        var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 82L);
        sheet.Save(ms, jpeg, ep);
        return ms.ToArray();
    }

    /// <summary>Draws a window into a bitmap even when it's covered or parked off-screen (not when minimized).</summary>
    static Bitmap CaptureWindow(IntPtr h)
    {
        if (Native.IsIconic(h) || !Native.GetClientRect(h, out var rc)) return null;
        int w = rc.Right - rc.Left, hh = rc.Bottom - rc.Top;
        if (w < 16 || hh < 16) return null;
        var bmp = new Bitmap(w, hh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            IntPtr dc = g.GetHdc();
            try { PrintWindow(h, dc, 0x1 | 0x2); }   // client area only + full content (works for GPU-drawn games)
            finally { g.ReleaseHdc(dc); }
        }
        return bmp;
    }
}
