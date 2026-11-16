namespace MultiBlox;

/// <summary>Profiles: save which accounts, game, server and options you use, then launch it all in one click.</summary>
public partial class MainForm
{
    DarkSelect cmbProfile;

    static DarkSelect Combo(int width) => new(width);

    void BuildProfileRow(FlowLayoutPanel host)
    {
        cmbProfile = Combo(230);
        RefreshProfiles();
        host.Controls.Add(Labeled("Profile", cmbProfile));
        host.Controls.Add(InlineBtn("▶  Launch profile", async (_, _) => await LaunchProfile(), BtnKind.Primary));
        host.Controls.Add(InlineBtn("Save current as profile", (_, _) => SaveProfile()));
        host.Controls.Add(InlineBtn("Delete", (_, _) => DeleteProfile(), BtnKind.Danger));
    }

    void RefreshProfiles(string select = null)
    {
        cmbProfile.Items.Clear();
        cmbProfile.SelectedIndex = -1;
        foreach (var p in cfg.Profiles) cmbProfile.Items.Add(p.Name);
        if (cmbProfile.Items.Count == 0) { cmbProfile.Items.Add("(no profiles yet)"); cmbProfile.SelectedIndex = 0; return; }
        int i = select == null ? 0 : cfg.Profiles.FindIndex(p => p.Name == select);
        cmbProfile.SelectedIndex = Math.Max(0, i);
        cmbProfile.Invalidate();
    }

    Profile SelectedProfile() =>
        cmbProfile.SelectedIndex >= 0 && cmbProfile.SelectedIndex < cfg.Profiles.Count ? cfg.Profiles[cmbProfile.SelectedIndex] : null;

    void SaveProfile()
    {
        var checkedAccounts = cfg.Accounts.Where(a => a.Checked).Select(a => a.Name).ToList();
        if (checkedAccounts.Count == 0) { Log("Tick the accounts you want in the profile first."); return; }
        string name = Prompt.Show(this, $"Name this profile ({checkedAccounts.Count} account{(checkedAccounts.Count == 1 ? "" : "s")}, " +
                                        "current game, server and options):", SelectedProfile()?.Name ?? "");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        var p = cfg.Profiles.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (p == null) cfg.Profiles.Add(p = new Profile());
        p.Name = name;
        p.Accounts = checkedAccounts;
        p.PlaceId = txtPlace.Text.Trim();
        p.Server = txtJob.Text.Trim();
        p.Fps = chkFpsAuto.Checked ? (int)numFps.Value : 0;
        p.AntiAfk = chkAfk.Checked;
        p.PowerSaver = chkSaver.Checked;
        p.AutoRejoin = chkRejoin.Checked;
        p.OpenViewer = viewer != null && !viewer.IsDisposed;
        SaveConfig();
        RefreshProfiles(name);
        Log($"Saved profile “{name}”.");
    }

    void DeleteProfile()
    {
        var p = SelectedProfile();
        if (p == null || !Confirm($"Delete profile “{p.Name}”? (Accounts aren't removed.)")) return;
        cfg.Profiles.Remove(p);
        SaveConfig();
        RefreshProfiles();
    }

    /// <summary>Puts every setting from the profile on screen, then launches its accounts.</summary>
    async Task LaunchProfile()
    {
        var p = SelectedProfile();
        if (p == null) { Log("Save a profile first: tick accounts, set the game, then 'Save current as profile'."); return; }

        var missing = p.Accounts.Where(n => !cfg.Accounts.Any(a => a.Name.Equals(n, StringComparison.OrdinalIgnoreCase))).ToList();
        if (missing.Count > 0) Log($"Profile “{p.Name}”: {string.Join(", ", missing)} not in your accounts anymore - skipping.");
        foreach (var a in cfg.Accounts) a.Checked = p.Accounts.Contains(a.Name, StringComparer.OrdinalIgnoreCase);
        RefreshAccounts();

        txtPlace.Text = p.PlaceId;
        txtJob.Text = p.Server;
        if (p.Fps > 0) { numFps.Value = Math.Clamp(p.Fps, (int)numFps.Minimum, (int)numFps.Maximum); chkFpsAuto.Checked = true; }
        chkSaver.Checked = p.PowerSaver;
        chkAfk.Checked = p.AntiAfk;
        chkRejoin.Checked = p.AutoRejoin;
        SaveConfig();

        Log($"Launching profile “{p.Name}”...");
        if (p.OpenViewer) OpenViewer();
        await LaunchMany(cfg.Accounts.Where(a => a.Checked).ToList());
    }
}
