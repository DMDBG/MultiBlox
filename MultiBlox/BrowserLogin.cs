using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MultiBlox;

/// <summary>
/// Opens roblox.com/login in an embedded Edge browser with a brand-new, empty profile.
/// You log in normally (username, password, 2-step, captcha); as soon as Roblox gives the
/// browser its login cookie, the window grabs it and closes. Your password is typed into
/// Roblox's own page and never touches MultiBlox.
/// </summary>
class BrowserLoginDialog : Form
{
    readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Theme.Bg };
    readonly System.Windows.Forms.Timer poll = new() { Interval = 1000 };
    readonly string profileDir = Path.Combine(Path.GetTempPath(), "MultiBlox-login-" + Guid.NewGuid().ToString("N"));
    readonly Label status;
    bool checking;

    public string Cookie { get; private set; }

    public BrowserLoginDialog()
    {
        DialogKit.Setup(this, "Log in to Roblox", new Size(1040, 800));
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimumSize = new Size(700, 560);

        var bar = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Theme.Sidebar, Padding = new Padding(18, 0, 14, 0) };
        status = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.TextDim, UseMnemonic = false,
            Text = "Log in with your username and password. This window closes by itself once you're in."
        };
        var cancel = new ModernButton("Cancel") { Dock = DockStyle.Right, DialogResult = DialogResult.Cancel };
        var cancelWrap = new Panel { Dock = DockStyle.Right, Width = 100, Padding = new Padding(0, 9, 0, 9) };
        cancelWrap.Controls.Add(cancel);
        bar.Controls.Add(status);
        bar.Controls.Add(cancelWrap);

        Controls.Add(web);
        Controls.Add(bar);
        CancelButton = cancel;

        Load += async (_, _) => await StartAsync();
        poll.Tick += async (_, _) => await CheckCookieAsync();
        FormClosed += (_, _) => Cleanup();
    }

    async Task StartAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, profileDir);
            await web.EnsureCoreWebView2Async(env);
            var core = web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            // Keep pop-ups (e.g. "log in with another device") inside this window.
            core.NewWindowRequested += (_, e) => { e.Handled = true; core.Navigate(e.Uri); };
            core.NavigationCompleted += async (_, _) => await CheckCookieAsync();
            core.Navigate("https://www.roblox.com/login");
            poll.Start();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this, "This needs the Microsoft Edge WebView2 Runtime, which isn't installed.\n\n" +
                                  "Get it free from Microsoft (search \"WebView2 Runtime download\"), or use \"Paste cookie\" instead.",
                "MultiBlox", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.Cancel;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't open the login browser: " + ex.Message, "MultiBlox");
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    async Task CheckCookieAsync()
    {
        if (checking || web.CoreWebView2 == null || Cookie != null) return;
        checking = true;
        try
        {
            var cookies = await web.CoreWebView2.CookieManager.GetCookiesAsync("https://www.roblox.com");
            var c = cookies.FirstOrDefault(x => x.Name == ".ROBLOSECURITY");
            if (c != null && c.Value.Length > 100)
            {
                poll.Stop();
                Cookie = c.Value;
                status.Text = "Logged in! Saving account...";
                status.ForeColor = Theme.Success;
                await Task.Delay(400);
                // Close WITHOUT logging out, so the session stays valid for MultiBlox.
                DialogResult = DialogResult.OK;
                Close();
            }
        }
        catch { /* browser busy or closing; try again next tick */ }
        finally { checking = false; }
    }

    void Cleanup()
    {
        poll.Stop();
        web.Dispose();
        // The throwaway profile still holds the login cookie - remove it once Edge lets go of the files.
        string dir = profileDir;
        Task.Run(async () =>
        {
            for (int i = 0; i < 10; i++)
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); return; }
                catch { await Task.Delay(1000); }
            }
        });
    }
}
