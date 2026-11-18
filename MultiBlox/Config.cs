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

/// <summary>A saved setup you can launch in one click.</summary>
public class Profile
{
    public string Name { get; set; } = "";
    public List<string> Accounts { get; set; } = new();
    public string PlaceId { get; set; } = "";
    public string Server { get; set; } = "";
    public int Fps { get; set; }               // 0 = leave FPS alone
    public bool AntiAfk { get; set; }
    public bool PowerSaver { get; set; }
    public bool AutoRejoin { get; set; }
    public bool OpenViewer { get; set; }
}

/// <summary>"Farm 10,000 Summer Pearls" - counts drops from when the goal was made.</summary>
public class Goal
{
    public string Item { get; set; } = "";
    public double Target { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
}

public class AppConfig
{
    public List<Profile> Profiles { get; set; } = new();
    public List<Goal> Goals { get; set; } = new();
    public bool AutoRejoin { get; set; }

    public string AlertItems { get; set; } = "";        // comma separated, e.g. "Sky Portal, Secret"
    public bool AlertSound { get; set; } = true;
    public string AlertWebhook { get; set; } = "";
    public string AlertMention { get; set; } = "";

    public List<string> InstanceOrder { get; set; } = new();   // account names, in the order you dragged them
    public bool AutoUpdate { get; set; } = true;

    // Phone control: commands typed in Discord
    public bool DiscordCommands { get; set; }
    public string DiscordCommandUsers { get; set; } = "";       // Discord user IDs allowed to send commands
    public string DiscordCommandChannelId { get; set; } = "";   // empty = the webhook channel

    // Low-end mode: allowlisted graphics flags + Roblox's own settings file, applied before each launch
    public bool LowGfx { get; set; }
    public bool LowGfxQuality { get; set; } = true;
    public bool LowGfxTextures { get; set; } = true;
    public bool LowGfxGrass { get; set; } = true;
    public bool LowGfxSky { get; set; } = true;
    public bool LowGfxDetail { get; set; } = true;
    public bool LowGfxLighting { get; set; }
    public bool LowGfxMute { get; set; } = true;
    public Dictionary<string, string> RobloxSettingsBackup { get; set; } = new();   // values from before MultiBlox changed them

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

    public string DiscordTokenEnc { get; set; } = "";   // DPAPI-encrypted bot token
    public string DiscordChannelId { get; set; } = "";
    public string DiscordChannelName { get; set; } = "";
    public bool DiscordEnabled { get; set; }

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
