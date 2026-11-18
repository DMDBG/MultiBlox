using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace MultiBlox;

/// <summary>
/// Checks this app's GitHub releases for a newer MultiBlox.exe, downloads it next to the running exe,
/// and swaps it in. Windows lets a running exe be renamed (just not overwritten), so the swap is:
/// MultiBlox.exe → MultiBlox.exe.old, MultiBlox.exe.new → MultiBlox.exe. The .old file is deleted next start.
/// </summary>
static class Updater
{
    const string Repo = "DMDBG/MultiBlox";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";

    public record Release(Version Version, string Tag, string Name, string ExeUrl, long ExeSize, string Sha256);

    static readonly HttpClient http = MakeClient();
    static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MultiBlox-Updater", Current.ToString()));
        return c;
    }

    public static Version Current
    {
        get
        {
            var v = typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    static string ExePath => Environment.ProcessPath;
    static string NewPath => ExePath + ".new";
    static string OldPath => ExePath + ".old";

    /// <summary>A build from source (MultiBlox.dll beside the exe) - don't overwrite it, just say an update exists.</summary>
    public static bool IsDevBuild => File.Exists(Path.Combine(Path.GetDirectoryName(ExePath)!, "MultiBlox.dll"));

    /// <summary>Returns the latest release if it's newer than this one, otherwise null.</summary>
    public static async Task<Release> CheckAsync()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var res = await http.SendAsync(req);
        if (!res.IsSuccessStatusCode) throw new Exception($"GitHub said {(int)res.StatusCode} {res.ReasonPhrase}");
        var j = JsonNode.Parse(await res.Content.ReadAsStringAsync());

        string tag = (string)j?["tag_name"] ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return null;
        v = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        if (v <= Current) return null;

        var exe = (j["assets"] as JsonArray)?.FirstOrDefault(a => string.Equals((string)a?["name"], "MultiBlox.exe", StringComparison.OrdinalIgnoreCase));
        if (exe == null) return null;   // release without an exe yet (still uploading) - try again later
        string url = (string)exe["browser_download_url"];
        if (url == null || !url.StartsWith("https://github.com/" + Repo + "/releases/download/", StringComparison.OrdinalIgnoreCase)) return null;
        string digest = (string)exe["digest"];   // "sha256:..." on newer GitHub releases
        return new Release(v, tag, (string)j["name"] ?? tag, url, (long?)exe["size"] ?? 0,
            digest != null && digest.StartsWith("sha256:") ? digest[7..].ToLowerInvariant() : null);
    }

    /// <summary>Downloads the new exe next to the current one and checks it arrived whole.</summary>
    public static async Task DownloadAsync(Release r, IProgress<int> progress)
    {
        string tmp = NewPath + ".part";
        using (var res = await http.GetAsync(r.ExeUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            res.EnsureSuccessStatusCode();
            long total = res.Content.Headers.ContentLength ?? r.ExeSize;
            await using var src = await res.Content.ReadAsStreamAsync();
            await using var dst = File.Create(tmp);
            var buf = new byte[81920];
            long done = 0; int last = -1, n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                int pct = total > 0 ? (int)(done * 100 / total) : 0;
                if (pct != last) { last = pct; progress?.Report(pct); }
            }
        }

        var info = new FileInfo(tmp);
        if (r.ExeSize > 0 && info.Length != r.ExeSize) { File.Delete(tmp); throw new Exception("download was incomplete"); }
        if (r.Sha256 != null)
        {
            await using var f = File.OpenRead(tmp);
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(f)).ToLowerInvariant();
            if (hash != r.Sha256) { f.Close(); File.Delete(tmp); throw new Exception("download didn't match GitHub's checksum"); }
        }
        File.Move(tmp, NewPath, overwrite: true);
    }

    public static bool Downloaded => File.Exists(NewPath);

    /// <summary>Swaps the downloaded exe in. Optionally starts it (call after this app has let go of everything).</summary>
    public static void Install(bool start)
    {
        if (!Downloaded) return;
        File.Move(ExePath, OldPath, overwrite: true);
        try { File.Move(NewPath, ExePath); }
        catch { File.Move(OldPath, ExePath); throw; }   // put the working one back
        if (start) Process.Start(new ProcessStartInfo(ExePath, "--updated") { UseShellExecute = false });
    }

    /// <summary>Removes the previous version left behind by an update, and any half-finished download.</summary>
    public static void Cleanup()
    {
        foreach (var f in new[] { OldPath, NewPath, NewPath + ".part" })
            for (int i = 0; i < 5 && File.Exists(f); i++)
            {
                try { File.Delete(f); } catch { Thread.Sleep(300); }   // the old process may still be exiting
            }
    }

    public static void OpenReleasesPage() => Process.Start(new ProcessStartInfo(ReleasesPage) { UseShellExecute = true });
}
