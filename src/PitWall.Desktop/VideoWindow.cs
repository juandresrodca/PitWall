using System.Diagnostics;
using System.Globalization;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;
using PitWall.Server;

namespace PitWall.Desktop;

/// <summary>
/// The live-video screen: F1 TV opened on the chosen display.
/// </summary>
/// <remarks>
/// F1 TV streams are DRM-protected. Microsoft Edge plays them; embedded WebView2 may not,
/// depending on the stream's DRM system. So the default is Edge in app mode (no tabs or
/// address bar) with a PitWall-only profile: the user signs in to F1 TV there once and Edge
/// remembers it. PitWall never sees or stores the F1 TV password.
/// </remarks>
internal sealed class VideoWindow : IDisposable
{
    private Process? _edge;
    private Form? _embedded;

    public static string EdgeProfileFolder => Path.Combine(PitWallOptions.DefaultDataDirectory, "EdgeProfile");

    public bool IsOpen => (_edge is { HasExited: false }) || (_embedded is { IsDisposed: false });

    public async Task OpenAsync(DesktopSettings settings)
    {
        Close();
        if (settings.Video == VideoMode.Off)
        {
            return;
        }

        var screen = settings.VideoScreen;
        if (settings.Video == VideoMode.Edge && FindEdge() is { } edge)
        {
            await OpenEdgeAsync(edge, settings, screen);
        }
        else
        {
            OpenEmbedded(settings, screen);
        }
    }

    public void Close()
    {
        if (_edge is { HasExited: false })
        {
            try
            {
                _edge.CloseMainWindow();
                if (!_edge.WaitForExit(3000))
                {
                    _edge.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
        _edge?.Dispose();
        _edge = null;

        if (_embedded is { IsDisposed: false })
        {
            _embedded.Close();
        }
        _embedded = null;
    }

    private async Task OpenEdgeAsync(string edgePath, DesktopSettings settings, Screen screen)
    {
        // Edge positions windows in device-independent pixels; aim for the middle of the
        // target display so the window lands on it even when the displays' scales differ.
        var scale = NativeMethods.ScaleAt(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top + screen.Bounds.Height / 2);
        var x = (int)((screen.Bounds.Left + screen.Bounds.Width / 4) / scale);
        var y = (int)((screen.Bounds.Top + screen.Bounds.Height / 4) / scale);
        var w = (int)(screen.Bounds.Width / 2 / scale);
        var h = (int)(screen.Bounds.Height / 2 / scale);

        var start = new ProcessStartInfo(edgePath) { UseShellExecute = false };
        start.ArgumentList.Add($"--app={settings.VideoUrl}");
        start.ArgumentList.Add($"--user-data-dir={EdgeProfileFolder}");
        start.ArgumentList.Add("--no-first-run");
        start.ArgumentList.Add("--no-default-browser-check");
        // Edge may sign a new profile in with the Windows account; keep this one from syncing.
        start.ArgumentList.Add("--disable-sync");
        start.ArgumentList.Add("--autoplay-policy=no-user-gesture-required");
        start.ArgumentList.Add(string.Create(CultureInfo.InvariantCulture, $"--window-position={x},{y}"));
        start.ArgumentList.Add(string.Create(CultureInfo.InvariantCulture, $"--window-size={w},{h}"));
        if (settings.StartVideoFullscreen)
        {
            start.ArgumentList.Add("--start-fullscreen");
        }

        _edge = Process.Start(start);
        if (_edge is null)
        {
            OpenEmbedded(settings, screen);
            return;
        }

        // If Edge still landed on the wrong display, move it there and maximise instead.
        for (var i = 0; i < 40 && !_edge.HasExited; i++)
        {
            await Task.Delay(250);
            _edge.Refresh();
            var handle = _edge.MainWindowHandle;
            if (handle == IntPtr.Zero)
            {
                continue;
            }
            if (Screen.FromHandle(handle).DeviceName != screen.DeviceName)
            {
                var area = screen.WorkingArea;
                NativeMethods.SetWindowPos(handle, IntPtr.Zero, area.X, area.Y, area.Width, area.Height,
                    NativeMethods.SwpNoZOrder | NativeMethods.SwpShowWindow);
                NativeMethods.ShowWindow(handle, NativeMethods.SwMaximize);
            }
            break;
        }
    }

    private void OpenEmbedded(DesktopSettings settings, Screen screen)
    {
        var view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
        var form = new Form
        {
            Text = "PitWall · Live video",
            Icon = AppIcon.Load(),
            BackColor = Color.Black,
            StartPosition = FormStartPosition.Manual,
            FormBorderStyle = settings.StartVideoFullscreen ? FormBorderStyle.None : FormBorderStyle.Sizable,
            Bounds = settings.StartVideoFullscreen ? screen.Bounds : screen.WorkingArea,
        };
        form.Controls.Add(view);
        form.Load += async (_, _) =>
        {
            await WebViewHost.InitializeAsync(view);
            view.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                view.CoreWebView2.Navigate(args.Uri);
            };
            view.CoreWebView2.ContainsFullScreenElementChanged += (_, _) =>
            {
                var full = view.CoreWebView2.ContainsFullScreenElement;
                form.FormBorderStyle = full || settings.StartVideoFullscreen ? FormBorderStyle.None : FormBorderStyle.Sizable;
                form.Bounds = full || settings.StartVideoFullscreen ? screen.Bounds : screen.WorkingArea;
            };
            view.CoreWebView2.Navigate(settings.VideoUrl);
        };
        _embedded = form;
        form.Show();
    }

    public static string? FindEdge()
    {
        var registered = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe", null, null) as string;
        var candidates = new[]
        {
            registered,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
        };
        return candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
    }

    public void Dispose() => Close();
}
