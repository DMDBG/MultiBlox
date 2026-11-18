namespace MultiBlox;

/// <summary>
/// The order running games are shown in. You set it by dragging tiles in the mini viewer or rows on the
/// Instances page; it's saved by account name, so it sticks across relaunches. Accounts you never dragged
/// follow the order of the Accounts list.
/// </summary>
public partial class MainForm
{
    /// <summary>Account name, or "#PID" for a Roblox window opened outside MultiBlox.</summary>
    string OrderKey(int pid) => pidOwner.TryGetValue(pid, out var name) ? name : "#" + pid;

    int OrderRank(int pid)
    {
        string key = OrderKey(pid);
        int i = cfg.InstanceOrder.IndexOf(key);
        if (i >= 0) return i;
        int a = cfg.Accounts.FindIndex(x => x.Name == key);
        return a >= 0 ? 100_000 + a : int.MaxValue;
    }

    IEnumerable<int> Ordered(IEnumerable<int> pids) => pids.OrderBy(OrderRank).ThenBy(p => p);

    /// <summary>Saves a new order from a drag (PIDs, first to last).</summary>
    void SetOrder(List<int> pids)
    {
        var keys = pids.Select(OrderKey).Distinct().ToList();
        if (keys.Count == 0) return;
        var running = RobloxPids().Select(p => "#" + p).ToHashSet();
        // Dragged ones go first in their new order; accounts not running right now keep their place after them.
        // "#PID" entries for games that have since closed are dropped.
        cfg.InstanceOrder = keys.Concat(cfg.InstanceOrder.Where(k => !keys.Contains(k) && (!k.StartsWith('#') || running.Contains(k)))).ToList();
        SaveConfig();
        RefreshInstances();
    }

    // ───────────── drag rows on the Instances page ─────────────

    int dragPid = -1;

    void HookInstanceDrag()
    {
        lvInstances.AllowDrop = true;
        lvInstances.ItemDrag += (_, e) =>
        {
            if (e.Item is not ListViewItem it || it.Tag is not int pid) return;
            dragPid = pid;
            try { lvInstances.DoDragDrop(pid, DragDropEffects.Move); }
            finally { dragPid = -1; }
        };
        lvInstances.DragEnter += (_, e) => e.Effect = dragPid >= 0 ? DragDropEffects.Move : DragDropEffects.None;
        lvInstances.DragOver += (_, e) =>
        {
            if (dragPid < 0) { e.Effect = DragDropEffects.None; return; }
            e.Effect = DragDropEffects.Move;
            var target = lvInstances.GetItemAt(5, lvInstances.PointToClient(new Point(e.X, e.Y)).Y);
            if (target?.Tag is not int targetPid || targetPid == dragPid) return;
            // Move the dragged row to where the mouse is, live, so you see it slide into place.
            var order = lvInstances.Items.Cast<ListViewItem>().Select(i => (int)i.Tag).ToList();
            int to = order.IndexOf(targetPid);
            order.Remove(dragPid);
            order.Insert(to, dragPid);
            SetOrder(order);
        };
    }
}
