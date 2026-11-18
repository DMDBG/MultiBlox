using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MultiBlox;

public partial class MainForm : Form
{
    // "--proc=notepad" lets you try the viewer/AFK features on other apps without Roblox running.
    static readonly string ProcName = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--proc="))?[7..] ?? "RobloxPlayerBeta";
    MiniViewer viewer;

    void OpenViewer()
    {
        SaveConfig();
        if (viewer != null && !viewer.IsDisposed) { viewer.Activate(); return; }
        viewer = new MiniViewer(
            () => Ordered(RobloxPids()).Select(p => (p, Native.FindRobloxWindow(p))).Where(x => x.Item2 != IntPtr.Zero).ToList(),
            pid => pidOwner.TryGetValue(pid, out var n) ? n : $"Roblox ({pid})",
            SetOrder,
            (int)numTileW.Value, (int)numTileH.Value, (int)numCols.Value);
        viewer.FormClosed += (_, _) => { viewer = null; Log("Mini viewer closed - Roblox windows put back."); };
        viewer.Show();
        Log($"Mini viewer opened ({numTileW.Value}x{numTileH.Value} tiles). Click a tile to play that window, right-click to hide it again, drag to reorder.");
    }

    bool ViewerParking => viewer != null && !viewer.IsDisposed && viewer.ParkEnabled;

    /// <summary>Gets a window out of the way: parked off-screen if the mini viewer is open (so its preview stays live), otherwise minimized.</summary>
    void HideWindow(IntPtr h)
    {
        if (ViewerParking) viewer.Park(h);
        else Native.ShowWindow(h, Native.SW_MINIMIZE);
    }
    const string FpsFlag = "DFIntTaskSchedulerTargetFps";

    readonly AppConfig cfg = AppConfig.Load();
    readonly Dictionary<int, string> pidOwner = new();               // Roblox PID -> account name
    readonly Dictionary<IntPtr, Native.RECT> savedRects = new();     // window sizes before the viewer shrank them
    Mutex mutexOld, mutexNew;
    bool multiOn, launching, afkBusy;
    DateTime nextAfk;

    ListView lvAccounts, lvInstances;
    TextBox txtPlace, txtJob, txtLog;
    NumericUpDown numDelay, numTileW, numTileH, numCols, numAfk, numFps, numSaverFps;
    CheckBox chkAfk, chkAfkMin, chkSaver, chkLowPri, chkTrim, chkCores, chkSaverFps, chkFpsAuto;
    Label lblMulti, lblAfk, lblSaver;
    Button btnMulti;
    readonly List<Button> launchButtons = new();

    readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 2000 };
    readonly System.Windows.Forms.Timer afkTimer = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer saverTimer = new() { Interval = 15000 };

    // ───────────────────────────── helpers ─────────────────────────────

    void Log(string msg)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(msg)); return; }
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
    }

    bool Confirm(string q) => MessageBox.Show(this, q, "MultiBlox", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    void SaveConfig()
    {
        if (txtPlace == null || chkFpsAuto == null || chkSaver == null) return; // still building UI
        cfg.PlaceId = txtPlace.Text.Trim();
        cfg.JobId = txtJob.Text.Trim();
        cfg.LaunchDelaySec = (int)numDelay.Value;
        cfg.TileWidth = (int)numTileW.Value;
        cfg.TileHeight = (int)numTileH.Value;
        cfg.Columns = (int)numCols.Value;
        cfg.AfkIntervalMin = (int)numAfk.Value;
        cfg.AfkMinimize = chkAfkMin.Checked;
        cfg.SaverEnabled = chkSaver.Checked;
        cfg.SaverLowPriority = chkLowPri.Checked;
        cfg.SaverTrimMemory = chkTrim.Checked;
        cfg.SaverLimitCores = chkCores.Checked;
        cfg.SaverLowFps = chkSaverFps.Checked;
        cfg.SaverFps = (int)numSaverFps.Value;
        cfg.FpsCap = (int)numFps.Value;
        cfg.FpsAutoApply = chkFpsAuto.Checked;
        if (chkDiscord != null) cfg.DiscordEnabled = chkDiscord.Checked;
        if (chkRejoin != null) cfg.AutoRejoin = chkRejoin.Checked;
        if (txtAlertItems != null)
        {
            cfg.AlertItems = txtAlertItems.Text.Trim();
            cfg.AlertWebhook = txtAlertHook.Text.Trim();
            cfg.AlertMention = txtAlertMention.Text.Trim();
            cfg.AlertSound = chkAlertSound.Checked;
        }
        try { cfg.Save(); saveWarned = false; }
        catch (Exception ex)
        {
            Log("Couldn't save settings: " + ex.Message);
            if (!saveWarned)
            {
                saveWarned = true;   // tell the user once, loudly - otherwise accounts silently vanish on restart
                MessageBox.Show(this, "MultiBlox couldn't save your settings/accounts:\n\n" + ex.Message +
                    "\n\nThey'll be lost when you close the app until this is fixed.", "MultiBlox", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    static HashSet<int> RobloxPids()
    {
        var set = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName(ProcName)) { set.Add(p.Id); p.Dispose(); }
        return set;
    }

    /// <summary>Every open Roblox game window → the process that owns it.</summary>
    static Dictionary<IntPtr, int> RobloxWindowMap()
    {
        var map = new Dictionary<IntPtr, int>();
        foreach (int pid in RobloxPids())
        {
            var h = Native.FindRobloxWindow(pid);
            if (h != IntPtr.Zero) map[h] = pid;
        }
        return map;
    }

    List<IntPtr> RobloxWindows() => Ordered(RobloxPids()).Select(Native.FindRobloxWindow).Where(h => h != IntPtr.Zero).ToList();

    void ForEachWindow(Action<IntPtr> act) { foreach (var h in RobloxWindows()) act(h); }

    // ───────────────────────────── Multi-Roblox ─────────────────────────────

    bool multiStarting, saveWarned;

    /// <summary>
    /// Turns Multi-Roblox on. It only works if MultiBlox grabs Roblox's "one instance" lock before Roblox does,
    /// so if Roblox is already open we offer to close it first.
    /// </summary>
    async Task EnableMulti()
    {
        if (multiOn || multiStarting) return;
        multiStarting = true;
        try
        {
            var running = RobloxPids();
            if (running.Count > 0)
            {
                bool close = ConfirmDialog.Ask(this,
                    "Roblox is already open",
                    $"{running.Count} Roblox process{(running.Count == 1 ? " is" : "es are")} running right now " +
                    "(this includes leftovers that keep running in the background after you close a game).\n\n" +
                    "Multi-Roblox has to start before Roblox, otherwise extra windows will glitch or close each other.\n\n" +
                    "Close Roblox and turn on Multi-Roblox? You'll leave any game you're in - just relaunch it from here afterwards.",
                    "Close Roblox & enable", "Not now");
                if (!close)
                {
                    SetMultiStatus(false);
                    Log("Multi-Roblox is OFF because Roblox was already open. Click 'Enable Multi-Roblox' when you're ready.");
                    return;
                }
                Log($"Closing {running.Count} Roblox window(s)...");
                await CloseAllRobloxAsync();
            }

            // Right after Roblox exits Windows can take a moment to free the lock, so retry briefly.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                if (TryTakeLocks())
                {
                    SetMultiStatus(true);
                    Log("Multi-Roblox enabled.");
                    return;
                }
                await Task.Delay(500);
            }
            SetMultiStatus(false);
            Log("Couldn't turn on Multi-Roblox - something is still holding Roblox's lock. " +
                "Check Task Manager for leftover 'Roblox' processes, end them, then click 'Enable Multi-Roblox'.");
        }
        finally { multiStarting = false; }
    }

    /// <summary>Claims both of Roblox's single-instance names. Returns false (and holds nothing) if Roblox still owns either.</summary>
    bool TryTakeLocks()
    {
        try
        {
            // Roblox only allows one client by checking for these named objects. Owning them first lets every client start.
            mutexOld = new Mutex(true, "ROBLOX_singletonMutex", out bool createdOld);
            mutexNew = new Mutex(true, "ROBLOX_singletonEvent", out bool createdNew);
            if (createdOld && createdNew) return true;
        }
        catch
        {
            // Roblox created the name as a different kind of object (an event) - it's still running or just closed.
        }
        ReleaseMulti();
        return false;
    }

    void SetMultiStatus(bool on)
    {
        multiOn = on;
        lblMulti.Text = on ? "●  Multi-Roblox ON\nUnlimited windows allowed" : "●  Multi-Roblox OFF\nRoblox was already open";
        lblMulti.ForeColor = on ? Theme.Success : Theme.Danger;
        btnMulti.Visible = !on;
    }

    async Task CloseAllRobloxAsync()
    {
        foreach (var p in Process.GetProcessesByName(ProcName))
        {
            try
            {
                StopSession(p.Id);
                p.Kill();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await p.WaitForExitAsync(cts.Token);
            }
            catch (Exception ex) { Log($"Couldn't close Roblox (PID {p.Id}): {ex.Message}"); }
            finally { p.Dispose(); }
        }
        pidOwner.Clear();
    }

    void ReleaseMulti()
    {
        foreach (var m in new[] { mutexOld, mutexNew })
        {
            if (m == null) continue;
            try { m.ReleaseMutex(); } catch { }
            m.Dispose();
        }
        mutexOld = mutexNew = null;
        multiOn = false;
    }

    // ───────────────────────────── Accounts ─────────────────────────────

    void RefreshAccounts()
    {
        lvAccounts.BeginUpdate();
        lvAccounts.Items.Clear();
        foreach (var a in cfg.Accounts)
        {
            var it = new ListViewItem(new[] { a.Name, a.UserId.ToString(), a.PlaceId, "" }) { Tag = a, Checked = a.Checked };
            lvAccounts.Items.Add(it);
        }
        lvAccounts.EndUpdate();
        RebuildAccountCards();   // keep the Discord Stats page in sync with the account list
    }

    void SetStatus(Account a, string status)
    {
        foreach (ListViewItem it in lvAccounts.Items)
            if (it.Tag == a) it.SubItems[3].Text = status;
    }

    List<Account> SelectedAccounts() => lvAccounts.SelectedItems.Cast<ListViewItem>().Select(i => (Account)i.Tag).ToList();

    async void AddAccount(bool viaBrowser)
    {
        string raw;
        if (viaBrowser)
        {
            using var dlg = new BrowserLoginDialog();
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            raw = dlg.Cookie;
        }
        else
        {
            using var dlg = new AddAccountDialog();
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            raw = dlg.Cookie;
        }
        string cookie = RobloxApi.CleanCookie(raw);
        if (cookie.Length < 100) { MessageBox.Show(this, "That doesn't look like a full .ROBLOSECURITY cookie - it should be a very long string.", "MultiBlox"); return; }
        try
        {
            Log("Checking cookie...");
            var (id, name) = await RobloxApi.GetUserAsync(cookie);
            var existing = cfg.Accounts.FirstOrDefault(a => a.UserId == id);
            if (existing != null) { existing.EncCookie = Dpapi.Protect(cookie); existing.Name = name; Log($"Updated cookie for {name}."); }
            else { cfg.Accounts.Add(new Account { Name = name, UserId = id, EncCookie = Dpapi.Protect(cookie) }); Log($"Added account {name} ({id})."); }
            SaveConfig();
            RefreshAccounts();
        }
        catch (Exception ex) { Log("Add failed: " + ex.Message); MessageBox.Show(this, ex.Message, "Couldn't add account"); }
    }

    void RemoveAccounts()
    {
        var sel = SelectedAccounts();
        if (sel.Count == 0 || !Confirm($"Remove {sel.Count} account(s) from MultiBlox?")) return;
        foreach (var a in sel) cfg.Accounts.Remove(a);
        SaveConfig();
        RefreshAccounts();
    }

    void SetPlaceOverride()
    {
        var sel = SelectedAccounts();
        if (sel.Count == 0) { Log("Select an account first."); return; }
        string v = Prompt.Show(this, "Place ID for the selected account(s).\nLeave empty to use the main Place ID.", sel[0].PlaceId);
        if (v == null) return;
        v = v.Trim();
        if (v != "" && !long.TryParse(v, out _)) { MessageBox.Show(this, "Place ID must be a number.", "MultiBlox"); return; }
        foreach (var a in sel) a.PlaceId = v;
        SaveConfig();
        RefreshAccounts();
    }

    async Task CheckCookies()
    {
        foreach (var a in cfg.Accounts.ToList())
        {
            try
            {
                var (_, name) = await RobloxApi.GetUserAsync(Dpapi.Unprotect(a.EncCookie));
                a.Name = name;
                SetStatus(a, "Cookie OK");
            }
            catch (Exception ex) { SetStatus(a, "✖ " + ex.Message); }
        }
        SaveConfig();
    }

    // ───────────────────────────── Launching ─────────────────────────────

    async Task LaunchMany(List<Account> accounts)
    {
        if (launching) return;
        if (accounts.Count == 0) { Log("Nothing to launch - select or check some accounts."); return; }
        if (!multiOn && RobloxPids().Count > 0) Log("Warning: Multi-Roblox is OFF, so a second window may close the first.");
        SaveConfig();
        launching = true;
        launchButtons.ForEach(b => b.Enabled = false);
        try
        {
            for (int i = 0; i < accounts.Count; i++)
            {
                await LaunchAccount(accounts[i]);
                if (i < accounts.Count - 1) await Task.Delay((int)numDelay.Value * 1000);
            }
        }
        finally
        {
            launching = false;
            launchButtons.ForEach(b => b.Enabled = true);
        }
    }

    /// <param name="fixedTarget">Used by auto-rejoin to go back to exactly the same game/server; otherwise the boxes on screen are used.</param>
    async Task LaunchAccount(Account a, ServerTarget fixedTarget = null)
    {
        ServerTarget target = fixedTarget;
        string cookie;
        try { cookie = Dpapi.Unprotect(a.EncCookie); }
        catch { SetStatus(a, "✖ Saved login unreadable - re-add this account"); return; }

        if (target == null)
        {
            // Account's own place override wins; otherwise the Place ID box, or the place inside a pasted server link.
            bool overridden = !string.IsNullOrWhiteSpace(a.PlaceId);
            string server = overridden ? "" : txtJob.Text.Trim();
            long.TryParse(overridden ? a.PlaceId.Trim() : txtPlace.Text.Trim(), out long placeId);
            if (placeId == 0) placeId = RobloxApi.PlaceFromLink(server);
            bool shareLink = server.Contains("share?code=", StringComparison.OrdinalIgnoreCase);
            if (placeId == 0 && !shareLink)
            {
                Log("Enter a Place ID first (the number in the game's URL: roblox.com/games/<PlaceID>/...).");
                return;
            }
            try { target = await RobloxApi.ResolveServerAsync(cookie, server, placeId); }
            catch (Exception ex) { SetStatus(a, "✖ " + ex.Message); Log($"[{a.Name}] {ex.Message}"); return; }
        }

        int fps = chkSaver.Checked && chkSaverFps.Checked ? (int)numSaverFps.Value
                : chkFpsAuto.Checked ? (int)numFps.Value : 0;
        if (fps > 0) ApplyFps(fps, true);

        try
        {
            SetStatus(a, "Getting ticket...");
            string ticket = await RobloxApi.GetAuthTicketAsync(cookie);
            // Remember which Roblox game windows already exist. The new game is whichever window appears next -
            // even if Roblox reuses a leftover background process (it often keeps one alive after you close a game).
            var windowsBefore = RobloxWindowMap().Keys.ToHashSet();
            Process.Start(new ProcessStartInfo(RobloxApi.BuildLaunchUri(ticket, target)) { UseShellExecute = true });
            SetStatus(a, "Launching...");
            Log($"[{a.Name}] launching {target}" + (fps > 0 ? $" at {fps} FPS" : ""));

            for (int i = 0; i < 90; i++)
            {
                await Task.Delay(1000);
                var fresh = RobloxWindowMap().Where(kv => !windowsBefore.Contains(kv.Key)).Select(kv => kv.Value).FirstOrDefault();
                if (fresh != 0)
                {
                    pidOwner[fresh] = a.Name;   // overwrite: a reused process now belongs to this account
                    TrackSession(fresh, a, target);   // for auto-rejoin
                    SetStatus(a, $"Running (PID {fresh})");
                    Log($"[{a.Name}] window opened (PID {fresh}).");
                    if (chkAfk.Checked && chkAfkMin.Checked)
                    {
                        await Task.Delay(4000);
                        var h = Native.FindRobloxWindow(fresh);
                        if (h != IntPtr.Zero) HideWindow(h);
                    }
                    return;
                }
            }
            SetStatus(a, "Launched (window not seen)");
            Log($"[{a.Name}] launched, but no new Roblox window appeared within 90s. If Roblox was updating, it may still open.");
        }
        catch (Exception ex)
        {
            SetStatus(a, "✖ " + ex.Message);
            Log($"[{a.Name}] launch failed: {ex.Message}");
        }
    }

    // ───────────────────────────── Instances & viewer ─────────────────────────────

    void RefreshInstances()
    {
        var pids = RobloxPids();
        foreach (var dead in pidOwner.Keys.Where(p => !pids.Contains(p)).ToList()) pidOwner.Remove(dead);
        foreach (var dead in savedRects.Keys.Where(h => !Native.IsWindow(h)).ToList()) savedRects.Remove(dead);

        var selected = SelectedPids();
        lvInstances.BeginUpdate();
        lvInstances.Items.Clear();
        foreach (int pid in Ordered(pids))
        {
            string ram = "?", pri = "?";
            bool old = false;
            try
            {
                using var p = Process.GetProcessById(pid);
                ram = (p.WorkingSet64 / 1024 / 1024).ToString();
                try { old = DateTime.Now - p.StartTime > TimeSpan.FromSeconds(60); } catch { }
                try { pri = p.PriorityClass.ToString(); } catch { pri = "(protected)"; }
            }
            catch { continue; }
            var h = Native.FindRobloxWindow(pid);
            // No window after a minute = a leftover Roblox keeps running after you close a game.
            string win = h == IntPtr.Zero ? (old ? "leftover (no window)" : "loading") : Native.IsIconic(h) ? "minimized" : "visible";
            var it = new ListViewItem(new[] { pidOwner.TryGetValue(pid, out var n) ? n : "(opened outside MultiBlox)", pid.ToString(), win, ram, pri }) { Tag = pid };
            if (selected.Contains(pid)) it.Selected = true;
            lvInstances.Items.Add(it);
        }
        lvInstances.EndUpdate();

        foreach (ListViewItem it in lvAccounts.Items)
            if (it.SubItems[3].Text.StartsWith("Running") && !pidOwner.ContainsValue(((Account)it.Tag).Name))
                it.SubItems[3].Text = "Closed";
    }

    List<int> SelectedPids() => lvInstances.SelectedItems.Cast<ListViewItem>().Select(i => (int)i.Tag).ToList();

    void TileAll()
    {
        SaveConfig();
        var wins = RobloxWindows();
        if (wins.Count == 0) { Log("No Roblox windows to tile."); return; }
        var area = Screen.PrimaryScreen.WorkingArea;
        int w = (int)numTileW.Value, h = (int)numTileH.Value;
        int cols = (int)numCols.Value > 0 ? (int)numCols.Value : Math.Max(1, area.Width / w);
        for (int i = 0; i < wins.Count; i++)
        {
            var hw = wins[i];
            if (!savedRects.ContainsKey(hw) && !Native.IsIconic(hw) && Native.GetWindowRect(hw, out var r)) savedRects[hw] = r;
            Native.ShowWindow(hw, Native.SW_RESTORE);
            int x = area.Left + (i % cols) * w, y = area.Top + (i / cols) * h;
            Native.SetWindowPos(hw, IntPtr.Zero, x, y, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }
        Log($"Viewer: tiled {wins.Count} window(s) at {w}x{h}.");
    }

    void RestoreSizes()
    {
        var wins = RobloxWindows();
        var area = Screen.PrimaryScreen.WorkingArea;
        for (int i = 0; i < wins.Count; i++)
        {
            var hw = wins[i];
            Native.ShowWindow(hw, Native.SW_RESTORE);
            if (savedRects.TryGetValue(hw, out var r))
                Native.SetWindowPos(hw, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            else
                Native.SetWindowPos(hw, IntPtr.Zero, area.Left + 40 * i, area.Top + 40 * i, 1280, 720, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }
        savedRects.Clear();
        Log("Restored window sizes.");
    }

    void FocusSelected()
    {
        foreach (int pid in SelectedPids())
        {
            var h = Native.FindRobloxWindow(pid);
            if (h == IntPtr.Zero) continue;
            Native.ShowWindow(h, Native.SW_RESTORE);
            Native.ForceForeground(h);
        }
    }

    /// <summary>Ends Roblox processes that have had no game window for over a minute (they just use RAM).</summary>
    void CloseLeftovers()
    {
        var leftovers = new List<int>();
        foreach (var p in Process.GetProcessesByName(ProcName))
        {
            try
            {
                if (Native.FindRobloxWindow(p.Id) == IntPtr.Zero && DateTime.Now - p.StartTime > TimeSpan.FromSeconds(60))
                    leftovers.Add(p.Id);
            }
            catch { }
            finally { p.Dispose(); }
        }
        if (leftovers.Count == 0) { Log("No leftover Roblox processes found."); return; }
        CloseInstances(leftovers);
        Log($"Cleaned up {leftovers.Count} leftover Roblox process(es).");
    }

    void CloseInstances(IEnumerable<int> pids)
    {
        foreach (int pid in pids)
        {
            StopSession(pid);   // closed on purpose - don't auto-rejoin it
            try { using var p = Process.GetProcessById(pid); p.Kill(); Log($"Closed PID {pid}."); }
            catch (Exception ex) { Log($"Couldn't close {pid}: {ex.Message}"); }
        }
    }

    // ───────────────────────────── Anti-AFK ─────────────────────────────

    void SetAfk(bool on)
    {
        if (on)
        {
            nextAfk = DateTime.Now.AddMinutes((double)numAfk.Value);
            afkTimer.Start();
            if (chkAfkMin.Checked) ForEachWindow(HideWindow);
            Log($"Anti-AFK on: pulsing every {numAfk.Value} min.");
        }
        else
        {
            afkTimer.Stop();
            lblAfk.Text = "Anti-AFK is off.";
            Log("Anti-AFK off.");
        }
    }

    async Task AfkTick()
    {
        var left = nextAfk - DateTime.Now;
        lblAfk.Text = afkBusy ? "Pulsing now..." : $"Next pulse in {Math.Max(0, (int)left.TotalMinutes)}:{Math.Max(0, left.Seconds):00}";
        if (left <= TimeSpan.Zero && !afkBusy)
        {
            await AfkPulse();
            nextAfk = DateTime.Now.AddMinutes((double)numAfk.Value);
        }
    }

    async Task AfkPulse()
    {
        if (afkBusy) return;
        var wins = RobloxWindows();
        if (wins.Count == 0) { Log("Anti-AFK: no Roblox windows open."); return; }
        afkBusy = true;
        try
        {
            IntPtr previous = Native.GetForegroundWindow();
            Native.GetCursorPos(out var cursor);
            foreach (var h in wins)
            {
                bool wasMin = Native.IsIconic(h);
                bool wasParked = viewer != null && !viewer.IsDisposed && viewer.IsParked(h);
                if (wasParked) viewer.Unpark(h);  // bring it on screen so the click lands inside it
                Native.ShowWindow(h, Native.SW_RESTORE);
                Native.ForceForeground(h);
                await Task.Delay(700);           // let Roblox redraw and take focus
                Native.ClickCenter(h);
                await Task.Delay(300);
                if (wasParked) viewer.Park(h);
                else if (chkAfkMin.Checked || wasMin) HideWindow(h);
            }
            Native.SetCursorPos(cursor.X, cursor.Y);
            if (previous != IntPtr.Zero && !wins.Contains(previous)) Native.ForceForeground(previous);
            Log($"Anti-AFK: clicked {wins.Count} window(s).");
        }
        finally { afkBusy = false; }
    }

    // ───────────────────────────── Power saver ─────────────────────────────

    void SetSaver(bool on)
    {
        SaveConfig();
        if (on)
        {
            ApplySaver();
            saverTimer.Start();
            Log("Power saver on.");
        }
        else
        {
            saverTimer.Stop();
            RestoreSaver();
            lblSaver.Text = "Power saver is off.";
            Log("Power saver off - priority and cores restored.");
        }
    }

    void ApplySaver()
    {
        var pids = RobloxPids().OrderBy(p => p).ToList();
        int cores = Math.Min(Environment.ProcessorCount, 64);
        int per = cores >= 4 ? 2 : 1;
        long savedMb = 0;
        for (int i = 0; i < pids.Count; i++)
        {
            try
            {
                using var p = Process.GetProcessById(pids[i]);
                if (chkLowPri.Checked && p.PriorityClass != ProcessPriorityClass.BelowNormal)
                    p.PriorityClass = ProcessPriorityClass.BelowNormal;
                if (chkCores.Checked && cores > 1)
                {
                    long mask = 0;
                    for (int k = 0; k < per; k++) mask |= 1L << ((i * per + k) % cores);
                    p.ProcessorAffinity = (IntPtr)mask;
                }
                if (chkTrim.Checked)
                {
                    long before = p.WorkingSet64;
                    if (Native.TrimMemory(p.Id)) { p.Refresh(); savedMb += Math.Max(0, before - p.WorkingSet64) / 1024 / 1024; }
                }
            }
            catch (Exception ex) { Log($"Power saver couldn't adjust PID {pids[i]}: {ex.Message}"); }
        }
        lblSaver.Text = $"Power saver on - {pids.Count} instance(s) managed" + (chkTrim.Checked ? $", trimmed {savedMb} MB last pass" : "") + ".";
    }

    void RestoreSaver()
    {
        long all = Environment.ProcessorCount >= 64 ? -1L : (1L << Environment.ProcessorCount) - 1;
        foreach (int pid in RobloxPids())
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.PriorityClass = ProcessPriorityClass.Normal;
                p.ProcessorAffinity = (IntPtr)all;
            }
            catch { }
        }
    }

    // ───────────────────────────── FPS ─────────────────────────────

    static IEnumerable<string> FlagFiles()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string versions = Path.Combine(local, "Roblox", "Versions");
        if (Directory.Exists(versions))
            foreach (var dir in Directory.GetDirectories(versions))
                if (File.Exists(Path.Combine(dir, ProcName + ".exe")))
                    yield return Path.Combine(dir, "ClientSettings", "ClientAppSettings.json");
        // Custom launchers keep their own copy that they copy over Roblox's on every launch.
        foreach (var launcher in new[] { "Bloxstrap", "Fishstrap", "Voidstrap" })
        {
            string root = Path.Combine(local, launcher);
            if (Directory.Exists(root))
                yield return Path.Combine(root, "Modifications", "ClientSettings", "ClientAppSettings.json");
        }
    }

    static void EditFlags(string path, Action<JsonObject> edit)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        JsonObject obj = null;
        try { if (File.Exists(path)) obj = JsonNode.Parse(File.ReadAllText(path)) as JsonObject; } catch { }
        obj ??= new JsonObject();
        edit(obj);
        File.WriteAllText(path, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    void ApplyFps(int fps, bool quiet)
    {
        SaveConfig();
        int n = 0;
        foreach (var f in FlagFiles())
        {
            try { EditFlags(f, o => o[FpsFlag] = fps); n++; }
            catch (Exception ex) { Log($"Couldn't write {f}: {ex.Message}"); }
        }
        if (n == 0) { Log("Couldn't find a Roblox install to apply FPS to."); return; }
        if (!quiet) Log($"FPS set to {(fps >= 9999 ? "unlimited" : fps.ToString())} for new launches ({n} location(s)). Running games keep their FPS until you rejoin.");
    }

    void ResetFps()
    {
        int n = 0;
        foreach (var f in FlagFiles().Where(File.Exists))
        {
            try { EditFlags(f, o => o.Remove(FpsFlag)); n++; } catch { }
        }
        chkFpsAuto.Checked = false;
        Log($"Removed the FPS setting from {n} location(s); auto-apply turned off.");
    }
}
