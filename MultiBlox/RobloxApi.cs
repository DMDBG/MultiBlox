using System.Net;
using System.Text;
using System.Text.Json;

namespace MultiBlox;

/// <summary>Where to send an account: a game, optionally a specific server (Job ID) or a private server.</summary>
public class ServerTarget
{
    public long PlaceId { get; set; }
    public string JobId { get; set; } = "";
    public string LinkCode { get; set; } = "";
    public long FollowUserId { get; set; }          // "join this user" - re-checked on every rejoin, since they may move
    public string FollowName { get; set; } = "";
    public override string ToString() =>
        FollowUserId > 0 ? $"{FollowName}'s game" + (PlaceId > 0 ? $" (place {PlaceId})" : "") :
        !string.IsNullOrEmpty(LinkCode) ? $"place {PlaceId} (private server)" :
        !string.IsNullOrEmpty(JobId) ? $"place {PlaceId} (server {JobId[..8]}…)" : $"place {PlaceId}";
}

static class RobloxApi
{
    static readonly HttpClient Http = new(new HttpClientHandler
    {
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All
    }) { Timeout = TimeSpan.FromSeconds(20) };

    public static string CleanCookie(string raw)
    {
        raw = raw.Trim().Trim('"');
        const string prefix = ".ROBLOSECURITY=";
        if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) raw = raw[prefix.Length..];
        int semi = raw.IndexOf(';');
        if (semi >= 0) raw = raw[..semi];
        return raw.Trim();
    }

    static HttpRequestMessage Request(HttpMethod method, string url, string cookie)
    {
        var r = new HttpRequestMessage(method, url);
        r.Headers.Add("Cookie", ".ROBLOSECURITY=" + cookie);
        r.Headers.Referrer = new Uri("https://www.roblox.com/");
        r.Headers.TryAddWithoutValidation("Origin", "https://www.roblox.com");
        r.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) MultiBlox/1.0");
        return r;
    }

    /// <summary>Checks a cookie and returns the account it belongs to.</summary>
    public static async Task<(long id, string name)> GetUserAsync(string cookie)
    {
        using var req = Request(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated", cookie);
        using var resp = await Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Roblox rejected the cookie (HTTP {(int)resp.StatusCode}). It may be expired or copied wrong.");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return (doc.RootElement.GetProperty("id").GetInt64(), doc.RootElement.GetProperty("name").GetString());
    }

    /// <summary>Gets a one-time ticket the Roblox player uses to sign in as this account.</summary>
    public static async Task<string> GetAuthTicketAsync(string cookie)
    {
        string csrf = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var req = Request(HttpMethod.Post, "https://auth.roblox.com/v1/authentication-ticket", cookie);
            req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            if (csrf != null) req.Headers.Add("x-csrf-token", csrf);

            using var resp = await Http.SendAsync(req);
            if (resp.Headers.TryGetValues("rbx-authentication-ticket", out var ticket))
                return ticket.First();
            if (resp.StatusCode == HttpStatusCode.Forbidden && resp.Headers.TryGetValues("x-csrf-token", out var token))
            {
                csrf = token.First();
                continue;
            }
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                throw new Exception("Cookie expired - remove and re-add this account.");
            throw new Exception($"Couldn't get a launch ticket (HTTP {(int)resp.StatusCode}).");
        }
        throw new Exception("Couldn't get a launch ticket (CSRF check failed).");
    }

    // ───────────────────────────── servers ─────────────────────────────

    static readonly System.Text.RegularExpressions.Regex GuidRx = new(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$");
    static readonly System.Text.RegularExpressions.Regex OldLinkRx = new(@"privateServerLinkCode=([0-9A-Za-z_-]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static readonly System.Text.RegularExpressions.Regex ShareRx = new(@"share\?code=([0-9A-Za-z]+).*type=Server", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static readonly System.Text.RegularExpressions.Regex GamePathRx = new(@"/games/(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Place ID written inside a game/private-server link, if any (so the Place ID box can be left empty).</summary>
    public static long PlaceFromLink(string server)
    {
        var m = GamePathRx.Match(server ?? "");
        return m.Success && long.TryParse(m.Groups[1].Value, out var id) ? id : 0;
    }

    /// <summary>
    /// Turns what's in the "Server" box into something launchable: empty = any server,
    /// a Job ID = that exact server, a private server link (old "privateServerLinkCode" or new "share?code=…&type=Server").
    /// </summary>
    public static async Task<ServerTarget> ResolveServerAsync(string cookie, string server, long placeId)
    {
        server = (server ?? "").Trim();
        var t = new ServerTarget { PlaceId = placeId };
        if (server == "") return t;
        if (GuidRx.IsMatch(server)) { t.JobId = server; return t; }
        if (IsUserRef(server))
        {
            var (id, name) = await ResolveUserAsync(cookie, server);
            return await FollowAsync(cookie, id, name);
        }

        var old = OldLinkRx.Match(server);
        if (old.Success)
        {
            t.LinkCode = old.Groups[1].Value;
            long p = PlaceFromLink(server);
            if (p > 0) t.PlaceId = p;
            return t;
        }

        var share = ShareRx.Match(server);
        if (share.Success)
        {
            string body = await PostJsonAsync("https://apis.roblox.com/sharelinks/v1/resolve-link", cookie,
                JsonSerializer.Serialize(new { linkId = share.Groups[1].Value, linkType = "Server" }));
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("privateServerInviteData", out var inv) || inv.ValueKind != JsonValueKind.Object)
                throw new Exception("That private server link didn't work - it may be expired or this account can't use it.");
            if (inv.TryGetProperty("status", out var st) && st.GetString() is string s && !s.Equals("Valid", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"Private server link is {s.ToLowerInvariant()}.");
            t.PlaceId = inv.GetProperty("placeId").GetInt64();
            t.LinkCode = inv.GetProperty("linkCode").GetString();
            return t;
        }
        throw new Exception("The Server box should be a Job ID, a private server link, or @username.");
    }

    // ───────────────────────────── joining a user ─────────────────────────────

    static readonly System.Text.RegularExpressions.Regex ProfileRx = new(@"roblox\.com/users/(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static readonly System.Text.RegularExpressions.Regex UsernameRx = new(@"^[A-Za-z0-9_]{3,20}$");

    /// <summary>"@username" or a roblox.com/users/123/profile link.</summary>
    public static bool IsUserRef(string s)
    {
        s = (s ?? "").Trim();
        return (s.StartsWith('@') && UsernameRx.IsMatch(s[1..])) || ProfileRx.IsMatch(s);
    }

    public static async Task<(long id, string name)> ResolveUserAsync(string cookie, string reference)
    {
        reference = reference.Trim();
        var m = ProfileRx.Match(reference);
        if (m.Success)
        {
            long id = long.Parse(m.Groups[1].Value);
            using var req = Request(HttpMethod.Get, $"https://users.roblox.com/v1/users/{id}", cookie);
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) throw new Exception($"Couldn't find Roblox user {id}.");
            using var d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return (id, d.RootElement.GetProperty("name").GetString());
        }
        string name = reference.TrimStart('@');
        string body = await PostJsonAsync("https://users.roblox.com/v1/usernames/users", cookie,
            JsonSerializer.Serialize(new { usernames = new[] { name }, excludeBannedUsers = true }));
        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");
        if (data.GetArrayLength() == 0) throw new Exception($"There's no Roblox user called “{name}”.");
        return (data[0].GetProperty("id").GetInt64(), data[0].GetProperty("name").GetString());
    }

    /// <summary>
    /// Where a user is right now, as seen by this account. If their exact server is visible it's used directly;
    /// otherwise Roblox's own "follow user" join is used, which obeys their privacy settings.
    /// </summary>
    public static async Task<ServerTarget> FollowAsync(string cookie, long userId, string name)
    {
        string body = await PostJsonAsync("https://presence.roblox.com/v1/presence/users", cookie,
            JsonSerializer.Serialize(new { userIds = new[] { userId } }));
        using var doc = JsonDocument.Parse(body);
        var list = doc.RootElement.GetProperty("userPresences");
        if (list.GetArrayLength() == 0) throw new Exception($"Couldn't see where {name} is.");
        var p = list[0];
        int type = p.TryGetProperty("userPresenceType", out var tp) ? tp.GetInt32() : 0;
        if (type == 3) throw new Exception($"{name} is in Roblox Studio, not a game.");
        if (type != 2) throw new Exception($"{name} isn't in a game right now (or hides it with their privacy settings).");

        var t = new ServerTarget { FollowUserId = userId, FollowName = name };
        if (p.TryGetProperty("placeId", out var pl) && pl.ValueKind == JsonValueKind.Number) t.PlaceId = pl.GetInt64();
        if (p.TryGetProperty("gameId", out var g) && g.ValueKind == JsonValueKind.String && GuidRx.IsMatch(g.GetString())) t.JobId = g.GetString();
        return t;
    }

    /// <summary>POST with Roblox's CSRF handshake (first try returns a token to retry with).</summary>
    static async Task<string> PostJsonAsync(string url, string cookie, string json)
    {
        string csrf = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var req = Request(HttpMethod.Post, url, cookie);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            if (csrf != null) req.Headers.Add("x-csrf-token", csrf);
            using var resp = await Http.SendAsync(req);
            if (resp.StatusCode == HttpStatusCode.Forbidden && csrf == null && resp.Headers.TryGetValues("x-csrf-token", out var token))
            {
                csrf = token.First();
                continue;
            }
            string body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) throw new Exception($"Roblox said no (HTTP {(int)resp.StatusCode}).");
            return body;
        }
        throw new Exception("Roblox security check failed - try again.");
    }

    public static string BuildLaunchUri(string ticket, ServerTarget target)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string tracker = Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString();
        long placeId = target.PlaceId;
        const string Base = "https://www.roblox.com/Game/PlaceLauncher.ashx?";
        string launcher =
            !string.IsNullOrEmpty(target.LinkCode)
                ? $"{Base}request=RequestPrivateGame&browserTrackerId={tracker}&placeId={placeId}&linkCode={Uri.EscapeDataString(target.LinkCode)}&isPlayTogetherGame=false"
            : !string.IsNullOrEmpty(target.JobId) && placeId > 0
                ? $"{Base}request=RequestGameJob&browserTrackerId={tracker}&placeId={placeId}&gameId={Uri.EscapeDataString(target.JobId)}&isPlayTogetherGame=false"
            : target.FollowUserId > 0
                ? $"{Base}request=RequestFollowUser&browserTrackerId={tracker}&userId={target.FollowUserId}&isPlayTogetherGame=false"
                : $"{Base}request=RequestGame&browserTrackerId={tracker}&placeId={placeId}&isPlayTogetherGame=false";

        return "roblox-player:1+launchmode:play" +
               $"+gameinfo:{ticket}" +
               $"+launchtime:{now}" +
               $"+placelauncherurl:{Uri.EscapeDataString(launcher)}" +
               $"+browsertrackerid:{tracker}" +
               "+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";
    }
}
