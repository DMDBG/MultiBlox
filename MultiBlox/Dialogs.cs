namespace MultiBlox;

static class DialogKit
{
    public static void Setup(Form f, string title, Size size)
    {
        f.AutoScaleDimensions = new SizeF(96f, 96f);
        f.AutoScaleMode = AutoScaleMode.Dpi;
        f.Text = title;
        f.Size = size;
        f.FormBorderStyle = FormBorderStyle.FixedDialog;
        f.MaximizeBox = f.MinimizeBox = false;
        f.StartPosition = FormStartPosition.CenterParent;
        f.BackColor = Theme.Bg;
        f.ForeColor = Theme.Text;
        f.Font = Theme.Body;
        Theme.DarkTitleBar(f);
    }

    public static Control Input(TextBox tb)
    {
        var wrap = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(20, 6, 20, 6) };
        var box = new RoundPanel(Theme.Input, 8) { Dock = DockStyle.Fill, Padding = new Padding(10, 9, 8, 4) };
        tb.Dock = DockStyle.Fill;
        tb.BorderStyle = BorderStyle.None;
        tb.BackColor = Theme.Input;
        tb.ForeColor = Theme.Text;
        tb.GotFocus += (_, _) => box.BorderColor = Theme.Accent;
        tb.LostFocus += (_, _) => box.BorderColor = Theme.CardBorder;
        box.Controls.Add(tb);
        wrap.Controls.Add(box);
        return wrap;
    }

    public static FlowLayoutPanel Buttons(params Control[] right)
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(20, 10, 20, 16) };
        p.Controls.AddRange(right);
        return p;
    }
}

class AddAccountDialog : Form
{
    readonly TextBox box = new() { UseSystemPasswordChar = true };
    public string Cookie => box.Text;

    public AddAccountDialog()
    {
        DialogKit.Setup(this, "Add account", new Size(640, 470));

        var title = new Label { Text = "Add a Roblox account", Dock = DockStyle.Top, Height = 44, Font = Theme.H2, Padding = new Padding(20, 16, 0, 0) };
        var steps = new Label
        {
            Dock = DockStyle.Top, Height = 230, Padding = new Padding(20, 8, 20, 0), ForeColor = Theme.TextDim,
            Text =
                "1.  Open a private / incognito browser window and log in to roblox.com with the account.\n\n" +
                "2.  Press F12 → Application tab (Chrome/Edge) or Storage tab (Firefox) → Cookies → https://www.roblox.com\n\n" +
                "3.  Copy the Value of  .ROBLOSECURITY  and paste it below.\n\n" +
                "4.  Close the private window WITHOUT clicking Log Out (logging out kills the cookie).\n\n" +
                "⚠  This cookie works like your password. MultiBlox keeps it encrypted to your Windows user and only sends it to roblox.com. " +
                "Never paste it anywhere else or give it to anyone."
        };
        var caption = new Label { Text = ".ROBLOSECURITY COOKIE", Dock = DockStyle.Top, Height = 22, Font = Theme.SmallBold, ForeColor = Theme.Muted, Padding = new Padding(22, 6, 0, 0) };

        var ok = new ModernButton("Add account", BtnKind.Primary) { DialogResult = DialogResult.OK, Margin = new Padding(0) };
        var cancel = new ModernButton("Cancel") { DialogResult = DialogResult.Cancel, Margin = new Padding(8, 0, 0, 0) };
        var show = new Toggle("Show") { Margin = new Padding(0, 3, 0, 0) };
        show.CheckedChanged += (_, _) => box.UseSystemPasswordChar = !show.Checked;
        var showWrap = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = new Padding(0, 0, 180, 0) };
        showWrap.Controls.Add(show);

        Controls.Add(DialogKit.Input(box));
        Controls.Add(caption);
        Controls.Add(steps);
        Controls.Add(title);
        Controls.Add(DialogKit.Buttons(ok, cancel, showWrap));
        AcceptButton = ok;
        CancelButton = cancel;
    }
}

/// <summary>Dark yes/no dialog that matches the rest of the app.</summary>
static class ConfirmDialog
{
    public static bool Ask(IWin32Window owner, string title, string message, string yes, string no)
    {
        using var f = new Form();
        DialogKit.Setup(f, "MultiBlox", new Size(520, 330));

        var icon = new Panel { Dock = DockStyle.Left, Width = 70 };
        icon.Paint += (_, e) =>
        {
            int S(int v) => icon.LogicalToDeviceUnits(v);
            var r = new Rectangle(S(24), S(22), S(34), S(34));
            Theme.FillRound(e.Graphics, r, S(17), Color.FromArgb(60, 48, 20), Color.Empty);
            TextRenderer.DrawText(e.Graphics, "!", new Font("Segoe UI Black", 14f), r, Theme.Warn,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 18, 24, 0) };
        var msg = new Label { Text = message, Dock = DockStyle.Fill, ForeColor = Theme.TextDim, UseMnemonic = false };
        var head = new Label { Text = title, Dock = DockStyle.Top, Height = 36, Font = Theme.H2, ForeColor = Theme.Text, UseMnemonic = false };
        body.Controls.Add(msg);
        body.Controls.Add(head);

        var ok = new ModernButton(yes, BtnKind.Primary) { DialogResult = DialogResult.Yes, Margin = new Padding(0) };
        var cancel = new ModernButton(no) { DialogResult = DialogResult.No, Margin = new Padding(8, 0, 0, 0) };
        f.Controls.Add(body);
        f.Controls.Add(icon);
        f.Controls.Add(DialogKit.Buttons(ok, cancel));
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        f.Shown += (_, _) => ok.Focus();
        return f.ShowDialog(owner) == DialogResult.Yes;
    }
}

static class Prompt
{
    /// <summary>Simple dark text input dialog. Returns null if cancelled.</summary>
    public static string Show(IWin32Window owner, string message, string initial)
    {
        using var f = new Form();
        DialogKit.Setup(f, "MultiBlox", new Size(460, 230));
        var lbl = new Label { Text = message, Dock = DockStyle.Top, Height = 64, Padding = new Padding(20, 16, 20, 0), ForeColor = Theme.TextDim };
        var tb = new TextBox { Text = initial ?? "" };
        var ok = new ModernButton("OK", BtnKind.Primary) { DialogResult = DialogResult.OK, Margin = new Padding(0) };
        var cancel = new ModernButton("Cancel") { DialogResult = DialogResult.Cancel, Margin = new Padding(8, 0, 0, 0) };
        f.Controls.Add(DialogKit.Input(tb));
        f.Controls.Add(lbl);
        f.Controls.Add(DialogKit.Buttons(ok, cancel));
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        return f.ShowDialog(owner) == DialogResult.OK ? tb.Text : null;
    }
}
