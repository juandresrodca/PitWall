using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PitWall.Desktop;

/// <summary>
/// The command-centre screen: a borderless window covering one display, showing the dashboard.
/// </summary>
/// <remarks>
/// The page talks to the host through <c>chrome.webview.postMessage({ cmd })</c> so the web UI
/// can offer "Connect F1 TV", "Swap screens" and so on. Messages are accepted only from the
/// dashboard's own origin; anything else (a news link that somehow navigated here) is ignored.
/// </remarks>
internal sealed class DashboardForm : Form
{
    private readonly WebView2 _view = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(11, 12, 15) };
    private readonly string _url;
    private readonly Func<string, JsonElement, Task> _onCommand;
    private bool _fullscreen = true;
    private Rectangle _windowedBounds;

    public DashboardForm(string url, Func<string, JsonElement, Task> onCommand)
    {
        _url = url;
        _onCommand = onCommand;
        Text = "PitWall";
        Icon = AppIcon.Load();
        BackColor = Color.FromArgb(11, 12, 15);
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        Controls.Add(_view);
        KeyPreview = true;
    }

    public void PlaceOn(Screen screen)
    {
        _windowedBounds = new Rectangle(screen.WorkingArea.X + 80, screen.WorkingArea.Y + 60,
            Math.Max(1280, screen.WorkingArea.Width - 160), Math.Max(720, screen.WorkingArea.Height - 120));
        ApplyFullscreen(_fullscreen, screen);
    }

    public void ToggleFullscreen() => ApplyFullscreen(!_fullscreen, Screen.FromControl(this));

    public void Reload() => _view.CoreWebView2?.Reload();

    /// <summary>Tells the page something changed on the host side (e.g. the sign-in pop-up opened).</summary>
    public void Notify(string eventName, object? detail = null)
    {
        if (_view.CoreWebView2 is { } core)
        {
            core.PostWebMessageAsJson(JsonSerializer.Serialize(new { @event = eventName, detail }, JsonSerializerOptions.Web));
        }
    }

    /// <summary>
    /// Forgets the Formula 1 session in PitWall's WebView2 profile, so the next sign-in starts fresh
    /// instead of silently reusing the old cookie.
    /// </summary>
    public async Task ClearFormula1CookiesAsync()
    {
        if (_view.CoreWebView2 is not { } core)
        {
            return;
        }
        foreach (var host in new[] { "https://account.formula1.com", "https://www.formula1.com", "https://f1tv.formula1.com" })
        {
            foreach (var cookie in await core.CookieManager.GetCookiesAsync(host))
            {
                core.CookieManager.DeleteCookie(cookie);
            }
        }
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await WebViewHost.InitializeAsync(_view);
        var core = _view.CoreWebView2;
        await core.AddScriptToExecuteOnDocumentCreatedAsync("window.__PITWALL_HOST__ = true;");
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            WebViewHost.OpenExternal(args.Uri);
        };
        core.NavigationStarting += (_, args) =>
        {
            // The dashboard never leaves its own origin; external links go to the real browser.
            if (!args.Uri.StartsWith(_url, StringComparison.OrdinalIgnoreCase))
            {
                args.Cancel = true;
                WebViewHost.OpenExternal(args.Uri);
            }
        };
        core.WebMessageReceived += OnWebMessage;
        core.Navigate(_url);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(_url, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            if (doc.RootElement.TryGetProperty("cmd", out var cmd) && cmd.GetString() is { } name)
            {
                if (name == "fullscreen")
                {
                    ToggleFullscreen();
                    return;
                }
                await _onCommand(name, doc.RootElement.Clone());
            }
        }
        catch (JsonException)
        {
        }
    }

    private void ApplyFullscreen(bool fullscreen, Screen screen)
    {
        _fullscreen = fullscreen;
        if (fullscreen)
        {
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            Bounds = screen.Bounds;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            Bounds = _windowedBounds.IsEmpty ? screen.WorkingArea : _windowedBounds;
        }
    }
}
