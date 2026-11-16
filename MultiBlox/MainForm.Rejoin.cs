using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MultiBlox;

/// <summary>
/// Auto-rejoin: watches every game MultiBlox launched. If its Roblox crashes, its window closes, or it stops
/// responding for a minute, that account is relaunched into the same game/server. Closing from MultiBlox
/// doesn't count, and each account gets at most 5 rejoins per 30 minutes so a broken game can't loop forever.
/// </summary>
public partial class MainForm
{
    [DllImport("user32.dll")] static extern bool IsHungAppWindow(IntPtr h);

    class Session
    {
        public Account Account;
        public ServerTarget Target;
        public DateTime Started;
        public DateTime? HungSince;
        public DateTime? NoWindowSince;
    }

    const int MaxRejoins = 5;
    static readonly TimeSpan RejoinWindow = TimeSpan.FromMinutes(30);
    static readonly TimeSpan HungLimit = TimeSpan.FromSeconds(60);

    readonly Dictionary<int, Session> sessions = new();
    readonly Dictionary<string, List<DateTime>> rejoinHistory = new(StringComparer.OrdinalIgnoreCase);
    readonly Queue<Session> rejoinQueue = new();
    readonly System.Windows.Forms.Timer rejoinTimer = new() { Interval = 5000 };
    bool rejoinBusy;
    Toggle chkRejoin;

    void TrackSession(int pid, Account a, ServerTarget target)
    {
        // A process can be reused by Roblox; drop any older session that pointed at the same account or pid.
        foreach (var old in sessions.Where(kv => kv.Value.Account == a).Select(kv => kv.Key).ToList()) sessions.Remove(old);
        sessions[pid] = new Session { Account = a, Target = target, Started = DateTime.Now };
        if (!rejoinTimer.Enabled) { rejoinTimer.Tick += async (_, _) => await CheckSessions(); rejoinTimer.Start(); }
    }

    void StopSession(int pid) => sessions.Remove(pid);

    async Task CheckSessions()
    {
        var now = DateTime.Now;
        foreach (var (pid, s) in sessions.ToList())
        {
            bool alive;
            try { using var p = Process.GetProcessById(pid); alive = !p.HasExited; } catch { alive = false; }
            IntPtr h = alive ? Native.FindRobloxWindow(pid) : IntPtr.Zero;

            string why = null;
            if (!alive) why = "Roblox closed or crashed";
            else if (h == IntPtr.Zero)
            {
                // Window gone but process still around = game closed, Roblox lingering in the background.
                s.NoWindowSince ??= now;
                if (now - s.NoWindowSince > TimeSpan.FromSeconds(10)) why = "game window closed";
            }
            else
            {
                s.NoWindowSince = null;
                if (IsHungAppWindow(h)) { s.HungSince ??= now; if (now - s.HungSince > HungLimit) why = "Roblox froze (not responding)"; }
                else s.HungSince = null;
            }
            if (why == null) continue;

            sessions.Remove(pid);
            if (!chkRejoin.Checked) continue;
            Log($"[{s.Account.Name}] {why}.");
            if (why.Contains("froze")) { try { using var p = Process.GetProcessById(pid); p.Kill(); } catch { } }
            if (!cfg.Accounts.Contains(s.Account)) continue;   // account was removed meanwhile

            var hist = rejoinHistory.TryGetValue(s.Account.Name, out var l) ? l : rejoinHistory[s.Account.Name] = new List<DateTime>();
            hist.RemoveAll(t => now - t > RejoinWindow);
            if (hist.Count >= MaxRejoins)
            {
                SetStatus(s.Account, "✖ Stopped rejoining (too many crashes)");
                Log($"[{s.Account.Name}] crashed {MaxRejoins} times in 30 minutes - not rejoining again for now.");
                continue;
            }
            hist.Add(now);
            SetStatus(s.Account, "Rejoining...");
            rejoinQueue.Enqueue(s);
        }
        await DrainRejoins();
    }

    async Task DrainRejoins()
    {
        if (rejoinBusy) return;
        rejoinBusy = true;
        try
        {
            while (rejoinQueue.Count > 0)
            {
                while (launching) await Task.Delay(1000);   // don't overlap a manual launch
                var s = rejoinQueue.Dequeue();
                await Task.Delay(5000);                      // give Roblox a moment to fully close
                if (!chkRejoin.Checked) break;
                Log($"[{s.Account.Name}] rejoining {s.Target}...");
                await LaunchAccount(s.Account, s.Target);
            }
        }
        finally { rejoinBusy = false; }
    }
}
