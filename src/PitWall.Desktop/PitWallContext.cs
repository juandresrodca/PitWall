using System.Diagnostics;
using System.Text.Json;
using PitWall.Server;

namespace PitWall.Desktop;

/// <summary>
/// Owns the two screens and the tray icon for as long as PitWall runs.
/// </summary>
internal sealed class PitWallContext : ApplicationContext
{
    private readonly PitWallServer _server;
    private readonly DesktopSettings _settings;
    private readonly VideoWindow _video = new();
    private readonly NotifyIcon _tray;
    private DashboardForm? _dashboard;

    public PitWallContext(PitWallServer server, DesktopSettings settings)
    {
        _server = server;
        _settings = settings;

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Load(),
            Text = "PitWall",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _tray.DoubleClick += (_, _) => ShowDashboard();

        _server.Tokens.Changed += _ =>
        {
            if (_dashboard is { IsHandleCreated: true, IsDisposed: false } dashboard)
            {
                dashboard.BeginInvoke(() => dashboard.Notify("auth-changed"));
            }
        };

        ShowDashboard();
        if (_settings.Video != VideoMode.Off)
        {
            _ = _video.OpenAsync(_settings);
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show dashboard", null, (_, _) => ShowDashboard());
        menu.Items.Add("Open live video", null, async (_, _) => await OpenVideoAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Connect F1 TV account…", null, (_, _) => ConnectF1Tv());
        menu.Items.Add("Sign out of F1 TV", null, (_, _) => _server.Tokens.Clear());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Swap screens", null, async (_, _) => await SwapScreensAsync());
        menu.Items.Add("Screen setup…", null, async (_, _) => await ScreenSetupAsync());
        menu.Items.Add("Open dashboard in browser", null, (_, _) => WebViewHost.OpenExternal(_server.Url));
        menu.Items.Add("Open data folder", null, (_, _) =>
            Process.Start(new ProcessStartInfo(PitWallOptions.DefaultDataDirectory) { UseShellExecute = true }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit PitWall", null, (_, _) => ExitThread());
        return menu;
    }

    private void ShowDashboard()
    {
        if (_dashboard is null || _dashboard.IsDisposed)
        {
            _dashboard = new DashboardForm(_server.Url, OnDashboardCommand);
            _dashboard.FormClosed += (_, _) => _dashboard = null;
            _dashboard.PlaceOn(_settings.DashboardScreen);
            _dashboard.Show();
        }
        _dashboard.Activate();
    }

    /// <summary>Commands the web dashboard sends through <c>chrome.webview.postMessage</c>.</summary>
    private async Task OnDashboardCommand(string command, JsonElement message)
    {
        switch (command)
        {
            case "connect-f1tv":
                ConnectF1Tv();
                break;
            case "signout-f1tv":
                _server.Tokens.Clear();
                break;
            case "open-video":
                await OpenVideoAsync();
                break;
            case "swap-screens":
                await SwapScreensAsync();
                break;
            case "screen-setup":
                await ScreenSetupAsync();
                break;
            case "open-external" when message.TryGetProperty("url", out var url):
                WebViewHost.OpenExternal(url.GetString() ?? "");
                break;
            case "reload":
                _dashboard?.Reload();
                break;
            case "quit":
                ExitThread();
                break;
        }
    }

    private void ConnectF1Tv()
    {
        using var login = new LoginForm(_server.Tokens);
        login.ShowDialog(_dashboard);
    }

    private async Task OpenVideoAsync()
    {
        if (_settings.Video == VideoMode.Off)
        {
            _settings.Video = VideoMode.Edge;
        }
        await _video.OpenAsync(_settings);
    }

    private async Task SwapScreensAsync()
    {
        if (Screen.AllScreens.Length < 2)
        {
            return;
        }
        (_settings.DashboardDisplay, _settings.VideoDisplay) = (_settings.VideoScreen.DeviceName, _settings.DashboardScreen.DeviceName);
        _settings.Save();
        _dashboard?.PlaceOn(_settings.DashboardScreen);
        if (_video.IsOpen)
        {
            await _video.OpenAsync(_settings);
        }
    }

    private async Task ScreenSetupAsync()
    {
        using var setup = new ScreenSetupForm(_settings);
        if (setup.ShowDialog(_dashboard) != DialogResult.OK)
        {
            return;
        }
        _dashboard?.PlaceOn(_settings.DashboardScreen);
        await _video.OpenAsync(_settings);
    }

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _video.Dispose();
        _dashboard?.Close();
        base.ExitThreadCore();
    }
}
