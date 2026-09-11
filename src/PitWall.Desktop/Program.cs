using PitWall.Server;

namespace PitWall.Desktop;

internal static class Program
{
    private const string Usage = """
        PitWall — Formula 1 race-weekend command centre

          PitWall.exe                 dashboard + live video on two screens
          PitWall.exe --setup         choose screens again before starting
          PitWall.exe --headless      run only the dashboard server (open it in any browser)
          PitWall.exe --replay PATH   start by replaying an archived session, e.g.
                                      2026/2026-09-06_Italian_Grand_Prix/2026-09-06_Race/
          PitWall.exe --port N        dashboard port (default 5320)
          PitWall.exe --lan           let other devices on the network open the dashboard
          PitWall.exe --webroot DIR   serve the dashboard from DIR (frontend development)
        """;

    [STAThread]
    private static int Main(string[] args)
    {
        var options = ParseArgs(args, out var headless, out var setup, out var help);
        if (help)
        {
            NativeMethods.AttachConsole(NativeMethods.AttachParentProcess);
            Console.WriteLine(Usage);
            return 0;
        }

        using var single = new Mutex(true, @"Local\PitWall.SingleInstance", out var first);
        if (!first)
        {
            if (!headless)
            {
                MessageBox.Show("PitWall is already running. Look for its icon in the notification area.",
                    "PitWall", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return 1;
        }

        return headless ? RunHeadless(options) : RunDesktop(options, setup);
    }

    private static int RunHeadless(PitWallOptions options)
    {
        NativeMethods.AttachConsole(NativeMethods.AttachParentProcess);
        var server = PitWallServer.StartAsync(options).GetAwaiter().GetResult();
        Console.WriteLine();
        Console.WriteLine($"PitWall dashboard: {server.Url}  (Ctrl+C to stop)");

        using var stop = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Set();
        };
        stop.Wait();
        server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return 0;
    }

    private static int RunDesktop(PitWallOptions options, bool forceSetup)
    {
        ApplicationConfiguration.Initialize();

        if (!WebViewHost.RuntimeInstalled())
        {
            MessageBox.Show(
                "PitWall needs the Microsoft Edge WebView2 Runtime, which ships with Windows 11.\n\n" +
                "Install it from https://developer.microsoft.com/microsoft-edge/webview2/ and start PitWall again.",
                "PitWall", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 2;
        }

        var settings = DesktopSettings.Load();
        options.Port = options.Port == 5320 ? settings.Port : options.Port;
        options.AllowLan |= settings.AllowLan;

        if (forceSetup || (Screen.AllScreens.Length > 1 && !settings.DisplaysValid()))
        {
            using var setupForm = new ScreenSetupForm(settings);
            if (setupForm.ShowDialog() != DialogResult.OK && !settings.DisplaysValid())
            {
                return 0;
            }
        }
        else if (Screen.AllScreens.Length == 1)
        {
            // One screen: the dashboard gets it, and the video opens in a normal window on top.
            settings.DashboardDisplay = settings.VideoDisplay = Screen.PrimaryScreen!.DeviceName;
            settings.StartVideoFullscreen = false;
        }

        PitWallServer server;
        try
        {
            server = Task.Run(() => PitWallServer.StartAsync(options)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"PitWall could not start its dashboard server:\n\n{ex.Message}", "PitWall",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 3;
        }

        try
        {
            Application.Run(new PitWallContext(server, settings));
        }
        finally
        {
            Task.Run(() => server.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(5));
        }
        return 0;
    }

    private static PitWallOptions ParseArgs(string[] args, out bool headless, out bool setup, out bool help)
    {
        var options = new PitWallOptions();
        headless = setup = help = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--headless": headless = true; break;
                case "--setup": setup = true; break;
                case "--lan": options.AllowLan = true; break;
                case "--replay" when i + 1 < args.Length: options.ReplayPath = args[++i]; break;
                case "--webroot" when i + 1 < args.Length: options.WebRoot = args[++i]; break;
                case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out var port): options.Port = port; i++; break;
                case "--help" or "-h" or "/?": help = true; break;
            }
        }
        return options;
    }
}
