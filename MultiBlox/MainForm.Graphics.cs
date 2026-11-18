namespace MultiBlox;

/// <summary>
/// Low-end mode: makes every new game as light as possible using only what Roblox still allows -
/// Fast Flags from Roblox's official allowlist (Sept 2025) plus Roblox's own settings file (graphics level, volume).
/// </summary>
public partial class MainForm
{
    Toggle chkLowGfx;

    // Each group: flags from Roblox's allowlist, with the value that makes it lightest.
    static readonly (string key, string value)[] QualityFlags = { ("DFIntDebugFRMQualityLevelOverride", "1"), ("FIntDebugForceMSAASamples", "0") };
    static readonly (string key, string value)[] TextureFlags = { ("DFFlagTextureQualityOverrideEnabled", "True"), ("DFIntTextureQualityOverride", "0") };
    static readonly (string key, string value)[] GrassFlags = { ("FIntFRMMaxGrassDistance", "0"), ("FIntFRMMinGrassDistance", "0"), ("FIntGrassMovementReducedMotionFactor", "0") };
    static readonly (string key, string value)[] SkyFlags = { ("FFlagDebugSkyGray", "True") };
    static readonly (string key, string value)[] DetailFlags =
    {
        ("DFIntCSGLevelOfDetailSwitchingDistance", "0"), ("DFIntCSGLevelOfDetailSwitchingDistanceL12", "0"),
        ("DFIntCSGLevelOfDetailSwitchingDistanceL23", "0"), ("DFIntCSGLevelOfDetailSwitchingDistanceL34", "0"),
    };
    static readonly (string key, string value)[] LightingFlags = { ("DFFlagDebugPauseVoxelizer", "True") };

    Control BuildLowGfxCard()
    {
        var card = new Card();
        card.Controls.Add(H("Low-end mode"));
        card.Controls.Add(Note("Makes every game you launch as light as possible, so more accounts fit on one PC. " +
                               "It only uses settings Roblox officially allows, so it's safe for your accounts. " +
                               "Applied before each launch - games already running change after a rejoin."));
        chkLowGfx = new Toggle("Use low-end mode for new launches", cfg.LowGfx);
        var opts = new (string text, Func<bool> get, Action<bool> set)[]
        {
            ("Lowest graphics quality (1 bar) and no anti-aliasing", () => cfg.LowGfxQuality, v => cfg.LowGfxQuality = v),
            ("Lowest texture quality", () => cfg.LowGfxTextures, v => cfg.LowGfxTextures = v),
            ("No grass", () => cfg.LowGfxGrass, v => cfg.LowGfxGrass = v),
            ("Plain gray sky", () => cfg.LowGfxSky, v => cfg.LowGfxSky = v),
            ("Low-detail models at every distance", () => cfg.LowGfxDetail, v => cfg.LowGfxDetail = v),
            ("Freeze lighting updates (lightest, but lighting can look wrong)", () => cfg.LowGfxLighting, v => cfg.LowGfxLighting = v),
            ("Mute sound (Roblox's master volume to 0)", () => cfg.LowGfxMute, v => cfg.LowGfxMute = v),
        };
        var sub = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(14, 6, 0, 0) };
        foreach (var (text, get, set) in opts)
        {
            var t = new Toggle(text, get());
            t.CheckedChanged += (_, _) => { set(t.Checked); SaveConfig(); if (cfg.LowGfx) ApplyLowGfx(true); };
            sub.Controls.Add(t);
        }
        chkLowGfx.CheckedChanged += (_, _) =>
        {
            cfg.LowGfx = chkLowGfx.Checked;
            SaveConfig();
            ApplyLowGfx(false);
        };
        card.Controls.Add(chkLowGfx);
        card.Controls.Add(sub);
        card.Controls.Add(Row(InlineBtnTop("Apply now", (_, _) => ApplyLowGfx(false))));
        card.Controls.Add(Note("Turning it off removes these settings and puts your graphics level and volume back the way they were."));
        return card;
    }

    static ModernButton InlineBtnTop(string text, EventHandler onClick)
    {
        var b = Btn(text, onClick);
        b.Margin = new Padding(0, 8, 8, 0);
        return b;
    }

    void ApplyLowGfx(bool quiet)
    {
        bool on = cfg.LowGfx;
        var groups = new (bool on, (string key, string value)[] flags)[]
        {
            (on && cfg.LowGfxQuality, QualityFlags), (on && cfg.LowGfxTextures, TextureFlags), (on && cfg.LowGfxGrass, GrassFlags),
            (on && cfg.LowGfxSky, SkyFlags), (on && cfg.LowGfxDetail, DetailFlags), (on && cfg.LowGfxLighting, LightingFlags),
        };
        int n = 0;
        foreach (var f in FlagFiles())
        {
            if (!on && !File.Exists(f)) continue;
            try
            {
                EditFlags(f, o =>
                {
                    foreach (var (enabled, flags) in groups)
                        foreach (var (key, value) in flags)
                            if (enabled) o[key] = value; else o.Remove(key);
                });
                n++;
            }
            catch (Exception ex) { if (!quiet) Log($"Couldn't write {f}: {ex.Message}"); }
        }

        bool quality = on && cfg.LowGfxQuality, mute = on && cfg.LowGfxMute;
        string settingsError = null;
        try
        {
            SetRobloxSettings(new Dictionary<string, string>
            {
                ["SavedQualityLevel"] = quality ? "1" : null,
                ["GraphicsQualityLevel"] = quality ? "1" : null,
                ["MasterVolume"] = mute ? "0" : null,
            });
        }
        catch (Exception ex) { settingsError = ex.Message; }

        if (quiet) return;
        if (settingsError != null) Log("Low-end mode: couldn't change Roblox's settings file - " + settingsError);
        if (on) Log($"Low-end mode on for new launches ({n} flag location(s){(settingsError == null ? " + Roblox settings" : "")}). Running games change after a rejoin.");
        else Log("Low-end mode off - graphics settings removed and your graphics level/volume put back.");
    }

    /// <summary>
    /// Changes Roblox's own settings. A null value means "put back what it was before MultiBlox changed it".
    /// The original values are kept in config.json, so turning a feature off always restores them.
    /// </summary>
    void SetRobloxSettings(Dictionary<string, string> want)
    {
        var apply = new Dictionary<string, string>();
        foreach (var (k, v) in want)
        {
            if (v != null) apply[k] = v;
            else if (cfg.RobloxSettingsBackup.TryGetValue(k, out var orig)) apply[k] = orig;   // orig null = it wasn't there: remove it
        }
        if (apply.Count == 0) return;
        var old = RobloxSettings.Set(apply);
        foreach (var (k, v) in want)
        {
            if (v != null) cfg.RobloxSettingsBackup.TryAdd(k, old[k]);
            else cfg.RobloxSettingsBackup.Remove(k);
        }
        SaveConfig();
    }
}
