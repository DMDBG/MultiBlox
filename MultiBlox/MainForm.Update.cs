namespace MultiBlox;

/// <summary>Auto-update: checks GitHub on start and every 6 hours, downloads in the background, installs on restart or close.</summary>
public partial class MainForm
{
    RoundPanel updateCard;
    Label lblUpdate;
    ModernButton btnUpdate;
    Toggle chkAutoUpdate;
    Updater.Release pendingUpdate;
    bool updateReady, updateBusy, restartAfterClose;
    readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 6 * 60 * 60 * 1000 };

    Control BuildUpdateCard()
    {
        updateCard = new RoundPanel(Color.FromArgb(24, 38, 66), 10, Theme.Accent) { Dock = DockStyle.Bottom, Height = 104, Padding = new Padding(14, 10, 12, 12), Visible = false };
        lblUpdate = new Label { Dock = DockStyle.Top, Height = 46, Font = Theme.Semi, ForeColor = Theme.Text, UseMnemonic = false };
        btnUpdate = Btn("Restart & update", async (_, _) => await UpdateButtonClicked(), BtnKind.Primary);
        btnUpdate.Dock = DockStyle.Top;
        updateCard.Controls.Add(btnUpdate);
        updateCard.Controls.Add(lblUpdate);
        return updateCard;
    }

    Control BuildVersionFooter()
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(2, 10, 0, 0) };
        var ver = new LinkLabel
        {
            Text = "v" + Updater.Current.ToString(3), Dock = DockStyle.Left, AutoSize = true, Font = Theme.SmallBold,
            LinkColor = Theme.Muted, ActiveLinkColor = Theme.Accent, LinkBehavior = LinkBehavior.HoverUnderline, Padding = new Padding(0, 3, 0, 0)
        };
        ver.LinkClicked += async (_, _) => await CheckForUpdate(silent: false);
        new ToolTip().SetToolTip(ver, "Check for updates now");
        chkAutoUpdate = new Toggle("Auto-update", cfg.AutoUpdate) { Dock = DockStyle.Right };
        chkAutoUpdate.CheckedChanged += (_, _) =>
        {
            cfg.AutoUpdate = chkAutoUpdate.Checked;
            SaveConfig();
            if (cfg.AutoUpdate) _ = CheckForUpdate(silent: true);
        };
        footer.Controls.Add(ver);
        footer.Controls.Add(chkAutoUpdate);
        return footer;
    }

    void StartUpdater()
    {
        Task.Run(Updater.Cleanup);   // removes the old version left over from the last update
        if (Environment.GetCommandLineArgs().Contains("--updated")) Log($"Updated to MultiBlox v{Updater.Current.ToString(3)}.");
        updateTimer.Tick += async (_, _) => { if (cfg.AutoUpdate) await CheckForUpdate(silent: true); };
        updateTimer.Start();
        if (cfg.AutoUpdate)
        {
            var once = new System.Windows.Forms.Timer { Interval = 4000 };
            once.Tick += async (_, _) => { once.Dispose(); await CheckForUpdate(silent: true); };
            once.Start();
        }
        // An update that's downloaded but not installed yet goes in when MultiBlox closes.
        FormClosed += (_, _) =>
        {
            if (!updateReady) return;
            try { Updater.Install(start: restartAfterClose); }
            catch (Exception ex)
            {
                MessageBox.Show("MultiBlox couldn't install the update:\n\n" + ex.Message +
                    "\n\nYou can download it yourself from GitHub.", "MultiBlox", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
    }

    async Task CheckForUpdate(bool silent)
    {
        if (updateBusy || updateReady) { if (!silent && updateReady) Log("An update is already downloaded - restart MultiBlox to install it."); return; }
        updateBusy = true;
        try
        {
            if (!silent) Log("Checking for updates...");
            var r = await Updater.CheckAsync();
            if (r == null) { if (!silent) Log($"You're on the latest version (v{Updater.Current.ToString(3)})."); return; }
            pendingUpdate = r;
            Log($"MultiBlox {r.Tag} is out (you have v{Updater.Current.ToString(3)}).");

            if (Updater.IsDevBuild)
            {
                ShowUpdateCard($"{r.Tag} is out\nYou're running from source", "Open on GitHub");
                return;
            }

            ShowUpdateCard($"Downloading {r.Tag}...", null);
            await Updater.DownloadAsync(r, new Progress<int>(p => lblUpdate.Text = $"Downloading {r.Tag}...  {p}%\nKeep playing, this is quick"));
            updateReady = true;
            ShowUpdateCard($"{r.Tag} is ready\nOr installs on close", "Restart & update");
            Log($"Update {r.Tag} downloaded. Click 'Restart & update', or it installs by itself next time you close MultiBlox.");
        }
        catch (Exception ex)
        {
            if (pendingUpdate != null && !updateReady)
                ShowUpdateCard($"{pendingUpdate.Tag} is out\nAuto-download failed", "Open on GitHub");
            Log("Update check failed: " + ex.Message);
        }
        finally { updateBusy = false; }
    }

    void ShowUpdateCard(string text, string button)
    {
        lblUpdate.Text = text;
        btnUpdate.Visible = button != null;
        if (button != null) btnUpdate.Text = button;
        updateCard.Height = updateCard.LogicalToDeviceUnits(button != null ? 104 : 68);
        updateCard.Visible = true;
    }

    async Task UpdateButtonClicked()
    {
        if (!updateReady) { Updater.OpenReleasesPage(); return; }

        int open = RobloxPids().Count;
        if (open > 0)
        {
            // Restarting gives up Roblox's lock; the new MultiBlox can only take it back once Roblox is closed.
            bool now = ConfirmDialog.Ask(this, "Games are still open",
                $"{open} Roblox game{(open == 1 ? " is" : "s are")} running. Your games keep running through the update, " +
                "but the restarted MultiBlox can't launch more accounts until Roblox has been closed once.\n\n" +
                "Update now, or let it install by itself when you close MultiBlox?",
                "Update now", "When I close it");
            if (!now) { Log("OK - the update will install when you close MultiBlox."); return; }
        }
        restartAfterClose = true;
        Log($"Restarting into {pendingUpdate?.Tag}...");
        await Task.Yield();
        Close();
    }
}
