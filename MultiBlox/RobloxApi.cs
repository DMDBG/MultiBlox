using System.Net;
using System.Text;
using System.Text.Json;

namespace MultiBlox;

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

    public static string BuildLaunchUri(string ticket, long placeId, string jobId)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string tracker = Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString();
        string launcher = string.IsNullOrWhiteSpace(jobId)
            ? $"https://www.roblox.com/Game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId={tracker}&placeId={placeId}&isPlayTogetherGame=false"
            : $"https://www.roblox.com/Game/PlaceLauncher.ashx?request=RequestGameJob&browserTrackerId={tracker}&placeId={placeId}&gameId={Uri.EscapeDataString(jobId.Trim())}&isPlayTogetherGame=false";

        return "roblox-player:1+launchmode:play" +
               $"+gameinfo:{ticket}" +
               $"+launchtime:{now}" +
               $"+placelauncherurl:{Uri.EscapeDataString(launcher)}" +
               $"+browsertrackerid:{tracker}" +
               "+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";
    }
}
