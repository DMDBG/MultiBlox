using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MultiBlox;

// ───────────────────────────── data ─────────────────────────────

public class MatchRecord
{
    public string MessageId { get; set; } = "";
    public DateTime Time { get; set; }
    public bool Win { get; set; }
    public string Map { get; set; } = "";
    public string Duration { get; set; } = "";
    public int Wave { get; set; }
    public List<Drop> Drops { get; set; } = new();
}

public class Drop
{
    public string Item { get; set; } = "";
    public double Amount { get; set; }
}

public class UserStats
{
    public string Name { get; set; } = "";
    public int Wins { get; set; }
    public int Losses { get; set; }
    public Dictionary<string, double> Drops { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<MatchRecord> Recent { get; set; } = new();   // newest first, capped
    public DateTime LastMatch { get; set; }
    public int Matches => Wins + Losses;
}

/// <summary>All match results read from Discord, saved in %APPDATA%\MultiBlox\stats.json.</summary>
public class StatsStore
{
    const int RecentCap = 5000;   // full history for charts/rates (≈1 MB per account at the cap)
    public Dictionary<string, UserStats> Users { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string LastMessageId { get; set; } = "";
    public string ChannelId { get; set; } = "";   // stats belong to this channel; switching channels starts fresh

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiBlox", "stats.json");

    public static StatsStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<StatsStore>(File.ReadAllText(FilePath)) ?? new StatsStore();
                // JSON loses the case-insensitive comparer; restore it.
                s.Users = new Dictionary<string, UserStats>(s.Users, StringComparer.OrdinalIgnoreCase);
                foreach (var u in s.Users.Values) u.Drops = new Dictionary<string, double>(u.Drops, StringComparer.OrdinalIgnoreCase);
                return s;
            }
        }
        catch { }
        return new StatsStore();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public UserStats User(string name)
    {
        if (!Users.TryGetValue(name, out var u)) Users[name] = u = new UserStats { Name = name };
        return u;
    }

    public void Add(string user, MatchRecord m)
    {
        var u = User(user);
        if (u.Recent.Any(r => r.MessageId == m.MessageId)) return;   // already counted
        if (m.Win) u.Wins++; else u.Losses++;
        foreach (var d in m.Drops)
            u.Drops[d.Item] = (u.Drops.TryGetValue(d.Item, out var have) ? have : 0) + d.Amount;
        u.Recent.Insert(0, m);
        u.Recent = u.Recent.OrderByDescending(r => r.Time).Take(RecentCap).ToList();
        if (m.Time > u.LastMatch) u.LastMatch = m.Time;
    }
}

// ───────────────────────────── rates ─────────────────────────────

public static class Rates
{
    /// <summary>Average match length (from "00:30:59" durations), default 15 minutes.</summary>
    static double AvgMatchHours(IEnumerable<MatchRecord> ms)
    {
        var d = ms.Select(m => TimeSpan.TryParse(m.Duration, out var t) ? t.TotalHours : 0).Where(h => h > 0).ToList();
        return d.Count == 0 ? 0.25 : d.Average();
    }

    /// <summary>How much of <paramref name="item"/> one account makes per hour, based on its last 24 hours of play.</summary>
    public static double PerHour(IEnumerable<MatchRecord> matches, string item, TimeSpan? window = null)
    {
        var since = DateTime.Now - (window ?? TimeSpan.FromHours(24));
        var ms = matches.Where(m => m.Time >= since).OrderBy(m => m.Time).ToList();
        if (ms.Count == 0) return 0;
        double total = ms.Sum(m => m.Drops.Where(d => d.Item.Equals(item, StringComparison.OrdinalIgnoreCase)).Sum(d => d.Amount));
        double hours = (ms[^1].Time - ms[0].Time).TotalHours + AvgMatchHours(ms);   // +1 match so a single result isn't "infinite/hr"
        return hours <= 0 ? 0 : total / hours;
    }

    /// <summary>Accounts farm in parallel, so the combined rate is the sum of each account's rate.</summary>
    public static double PerHourAll(StatsStore s, string item) => s.Users.Values.Sum(u => PerHour(u.Recent, item));

    public static double SumSince(StatsStore s, string item, DateTime since) =>
        s.Users.Values.SelectMany(u => u.Recent).Where(m => m.Time >= since)
         .Sum(m => m.Drops.Where(d => d.Item.Equals(item, StringComparison.OrdinalIgnoreCase)).Sum(d => d.Amount));

    public static string Short(double v) =>
        v >= 1_000_000 ? $"{v / 1_000_000:0.#}M" : v >= 10_000 ? $"{v / 1000:0.#}k" : v >= 100 ? v.ToString("N0") : v.ToString("0.#");

    public static string Duration(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h" : t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{Math.Max(1, (int)t.TotalMinutes)}m";
}

// ───────────────────────────── parsing ─────────────────────────────

/// <summary>
/// Reads match-result webhooks like the "Anime Adventures" embeds macros post: finds the user, Victory/Defeat,
/// and every "+N Item" line in the Rewards section. Units and other sections are ignored.
/// Written to be forgiving about formatting (bold, spoilers, emoji, field vs. description layout).
/// </summary>
public static class WebhookParser
{
    static readonly Regex CustomEmoji = new(@"<a?:\w+:\d+>", RegexOptions.Compiled);
    static readonly Regex UserRx = new(@"\bUser(?:name)?\s*:\s*@?([A-Za-z0-9_]{3,20})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex WinRx = new(@"\b(Victory|Won|Win|Cleared|Completed)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex LossRx = new(@"\b(Defeat|Defeated|Lost|Loss|Lose|Failed|Fail)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex DropRx = new(@"^\+\s*([\d][\d,]*(?:\.\d+)?)\s*[xX]?\s+(.+?)\s*$", RegexOptions.Compiled);
    static readonly Regex CountTag = new(@"\s*\[\d+\]\s*$", RegexOptions.Compiled);       // "[4]" = owned count, not part of the name
    static readonly Regex TimeRx = new(@"\b(\d{1,2}:\d{2}:\d{2})\b", RegexOptions.Compiled);
    static readonly Regex WaveRx = new(@"\bWave\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Removes Discord markdown, spoilers and custom emoji.</summary>
    public static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = CustomEmoji.Replace(s, "");
        s = s.Replace("||", "").Replace("**", "").Replace("__", "").Replace("`", "").Replace("~~", "");
        s = s.Replace("*", "");
        return s;
    }

    /// <summary>Returns (username, match) or null if this message isn't a match result.</summary>
    public static (string user, MatchRecord match)? Parse(JsonElement msg)
    {
        if (!msg.TryGetProperty("embeds", out var embeds) || embeds.ValueKind != JsonValueKind.Array) return null;

        var sections = new List<(string name, string value)>();
        if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            sections.Add(("", content.GetString()));
        foreach (var e in embeds.EnumerateArray())
        {
            if (e.TryGetProperty("author", out var au) && au.TryGetProperty("name", out var an)) sections.Add(("author", an.GetString()));
            if (e.TryGetProperty("title", out var t)) sections.Add(("title", t.GetString()));
            if (e.TryGetProperty("description", out var d)) sections.AddRange(SplitByHeadings("", d.GetString()));
            if (e.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
                foreach (var f in fields.EnumerateArray())
                    sections.AddRange(SplitByHeadings(
                        f.TryGetProperty("name", out var fn) ? fn.GetString() ?? "" : "",
                        f.TryGetProperty("value", out var fv) ? fv.GetString() ?? "" : ""));
        }
        sections = sections.Select(s => (Clean(s.name).Trim(), Clean(s.value))).ToList();
        string all = string.Join("\n", sections.Select(s => s.name + "\n" + s.value));

        // Victory / defeat - prefer the "Match Result" section, fall back to the whole message.
        string resultText = Section(sections, "match", "result") ?? all;
        bool win = WinRx.IsMatch(resultText), loss = LossRx.IsMatch(resultText);
        if (!win && !loss) return null;                       // not a match message (e.g. a status ping)
        if (win && loss) win = WinRx.Match(resultText).Index < LossRx.Match(resultText).Index;

        var userMatch = UserRx.Match(all);
        string user = userMatch.Success ? userMatch.Groups[1].Value : "Unknown";

        var m = new MatchRecord
        {
            MessageId = msg.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            Time = msg.TryGetProperty("timestamp", out var ts) && DateTime.TryParse(ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt)
                ? dt.ToLocalTime() : DateTime.Now,
            Win = win
        };

        var tm = TimeRx.Match(resultText); if (tm.Success) m.Duration = tm.Groups[1].Value;
        var wm = WaveRx.Match(resultText); if (wm.Success) m.Wave = int.Parse(wm.Groups[1].Value);
        // Map = first line of the result section that isn't the "Victory - time - wave" line or kill/damage stats.
        foreach (var line in Lines(resultText))
            if (!WinRx.IsMatch(line) && !LossRx.IsMatch(line) && !Regex.IsMatch(line, @"\b(kills?|dmg|damage)\b", RegexOptions.IgnoreCase))
            { m.Map = line; break; }

        // Drops: "+N Item" lines in Rewards/Drops sections.
        string rewards = Section(sections, "reward") ?? Section(sections, "drop") ?? "";
        foreach (var line in Lines(rewards))
        {
            var dm = DropRx.Match(line);
            if (!dm.Success) continue;
            if (!double.TryParse(dm.Groups[1].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var amt)) continue;
            string item = CountTag.Replace(dm.Groups[2].Value, "").Trim();
            if (item.Length > 0) m.Drops.Add(new Drop { Item = item, Amount = amt });
        }
        return (user, m);
    }

    static readonly Regex Heading = new(@"^\s*(?:\*\*|__|#{1,3}\s*)([^*_#\n]{2,40}?)(?:\*\*|__)?\s*:?\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Some macros put everything in one text block with bold headings ("**Rewards**", "## Match Result").
    /// Split those into separate sections so they read the same as embed fields.
    /// </summary>
    static IEnumerable<(string name, string value)> SplitByHeadings(string name, string raw)
    {
        string current = name;
        var buf = new List<string>();
        foreach (var line in (raw ?? "").Split('\n'))
        {
            var h = Heading.Match(line);
            bool isHeading = h.Success && (line.Contains("**") || line.Contains("__") || line.TrimStart().StartsWith("#"))
                             && !Regex.IsMatch(h.Groups[1].Value, @"\d");
            if (isHeading)
            {
                if (buf.Count > 0 || current != name) yield return (current, string.Join("\n", buf));
                current = h.Groups[1].Value.Trim();
                buf.Clear();
            }
            else buf.Add(line);
        }
        yield return (current, string.Join("\n", buf));
    }

    static string Section(List<(string name, string value)> sections, params string[] keywords)
    {
        foreach (var (name, value) in sections)
            if (keywords.All(k => name.Contains(k, StringComparison.OrdinalIgnoreCase))) return value;
        return null;
    }

    static IEnumerable<string> Lines(string s) =>
        s.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
}

// ───────────────────────────── reading the channel ─────────────────────────────

/// <summary>Reads new messages from one Discord channel using a bot token (Discord's official API).</summary>
public class DiscordReader
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    readonly string token, channelId;

    public DiscordReader(string token, string channelId)
    {
        this.token = token.Trim().Replace("Bot ", "");
        this.channelId = channelId.Trim();
    }

    /// <summary>Checks the token and channel. Returns the channel's name.</summary>
    public async Task<string> TestAsync()
    {
        using var doc = JsonDocument.Parse(await GetAsync($"https://discord.com/api/v10/channels/{channelId}"));
        return doc.RootElement.TryGetProperty("name", out var n) ? "#" + n.GetString() : "channel";
    }

    /// <summary>Messages newer than <paramref name="afterId"/> (or the latest 100 if empty), oldest first.</summary>
    public async Task<List<JsonElement>> FetchNewAsync(string afterId)
    {
        var all = new List<JsonElement>();
        string cursor = afterId;
        for (int page = 0; page < 10; page++)   // up to 1000 messages per check
        {
            string url = $"https://discord.com/api/v10/channels/{channelId}/messages?limit=100" +
                         (string.IsNullOrEmpty(cursor) ? "" : $"&after={cursor}");
            using var doc = JsonDocument.Parse(await GetAsync(url));
            var batch = doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
            all.AddRange(batch);
            if (batch.Count < 100 || string.IsNullOrEmpty(cursor)) break;   // first-ever read: just the latest 100
            cursor = batch.Select(Id).Max().ToString();
        }
        return all.GroupBy(Id).Select(g => g.First()).OrderBy(Id).ToList();
    }

    public static ulong Id(JsonElement m) => ulong.TryParse(m.GetProperty("id").GetString(), out var v) ? v : 0;

    /// <summary>Posts a reply in the channel (used by phone control). Never pings anyone.</summary>
    public async Task SendAsync(string text, string replyTo = null, byte[] jpeg = null)
    {
        if (text.Length > 1990) text = text[..1990] + "…";
        var payload = new Dictionary<string, object>
        {
            ["content"] = text,
            ["allowed_mentions"] = new { parse = Array.Empty<string>() }
        };
        if (replyTo != null) payload["message_reference"] = new { message_id = replyTo, fail_if_not_exists = false };
        if (jpeg != null) payload["attachments"] = new[] { new { id = 0, filename = "multiblox.jpg" } };
        string json = JsonSerializer.Serialize(payload);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://discord.com/api/v10/channels/{channelId}/messages");
            req.Headers.TryAddWithoutValidation("Authorization", "Bot " + token);
            req.Headers.UserAgent.ParseAdd("DiscordBot (https://github.com, 1.0) MultiBlox");
            if (jpeg == null) req.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            else
            {
                var form = new MultipartFormDataContent();
                form.Add(new StringContent(json, System.Text.Encoding.UTF8, "application/json"), "payload_json");
                var file = new ByteArrayContent(jpeg);
                file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                form.Add(file, "files[0]", "multiblox.jpg");
                req.Content = form;
            }
            using var resp = await Http.SendAsync(req);
            if (resp.IsSuccessStatusCode) return;
            string body = await resp.Content.ReadAsStringAsync();
            if ((int)resp.StatusCode == 429)
            {
                double wait = 2;
                try { wait = JsonDocument.Parse(body).RootElement.GetProperty("retry_after").GetDouble(); } catch { }
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(wait + 0.25, 30)));
                continue;
            }
            throw new Exception(resp.StatusCode == HttpStatusCode.Forbidden
                ? "The bot can't post in that channel - give it the Send Messages and Attach Files permissions there."
                : $"Discord error {(int)resp.StatusCode} while replying.");
        }
        throw new Exception("Discord is rate-limiting replies - try again in a moment.");
    }

    async Task<string> GetAsync(string url)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Authorization", "Bot " + token);
            req.Headers.UserAgent.ParseAdd("DiscordBot (https://github.com, 1.0) MultiBlox");
            using var resp = await Http.SendAsync(req);
            string body = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode) return body;
            if ((int)resp.StatusCode == 429)
            {
                double wait = 2;
                try { wait = JsonDocument.Parse(body).RootElement.GetProperty("retry_after").GetDouble(); } catch { }
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(wait + 0.25, 30)));
                continue;
            }
            throw new Exception(resp.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Discord rejected the bot token - copy it again from the Developer Portal (Bot → Reset Token).",
                HttpStatusCode.Forbidden => "The bot can't see that channel. Make sure it's in your server and has View Channel + Read Message History there.",
                HttpStatusCode.NotFound => "Channel not found - check the Channel ID (right-click the channel → Copy Channel ID).",
                _ => $"Discord error {(int)resp.StatusCode}."
            });
        }
        throw new Exception("Discord is rate-limiting - will try again shortly.");
    }
}
