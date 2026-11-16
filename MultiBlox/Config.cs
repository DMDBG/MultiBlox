using System.Text.Json;

namespace MultiBlox;

public class Account
{
    public string Name { get; set; } = "";
    public long UserId { get; set; }
    public string EncCookie { get; set; } = "";   // DPAPI-encrypted .ROBLOSECURITY
    public string PlaceId { get; set; } = "";     // optional per-account override
    public bool Checked { get; set; } = true;
}

public class AppConfig
{
    public List<Account> Accounts { get; set; } = new();
    public string PlaceId { get; set; } = "";
    public string JobId { get; set; } = "";
    public int LaunchDelaySec { get; set; } = 10;

    public int TileWidth { get; set; } = 192;
    public int TileHeight { get; set; } = 108;
    public int Columns { get; set; } = 0;           // 0 = auto

    public int AfkIntervalMin { get; set; } = 10;
    public bool AfkMinimize { get; set; } = true;

    public bool SaverEnabled { get; set; }
    public bool SaverLowPriority { get; set; } = true;
    public bool SaverTrimMemory { get; set; } = true;
    public bool SaverLimitCores { get; set; }
    public bool SaverLowFps { get; set; } = true;
    public int SaverFps { get; set; } = 20;

    public int FpsCap { get; set; } = 60;
    public bool FpsAutoApply { get; set; } = true;

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiBlox");
    static string FilePath => Path.Combine(Dir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath)) ?? new AppConfig();
        }
        catch { /* corrupt config: start fresh */ }
        return new AppConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        // Write to a temp file first, then swap it in, so a crash mid-save can never leave a half-written config.
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
