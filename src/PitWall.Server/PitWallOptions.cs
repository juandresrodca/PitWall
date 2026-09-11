namespace PitWall.Server;

public sealed class PitWallOptions
{
    /// <summary>Local port for the dashboard. 0 picks a free one.</summary>
    public int Port { get; set; } = 5320;

    /// <summary>
    /// Listen on every interface so a tablet or phone on the same network can open the dashboard.
    /// Off by default: the API can switch data sources, so it stays on loopback unless asked.
    /// </summary>
    public bool AllowLan { get; set; }

    /// <summary>Where caches, track maps and the protected F1 TV token live.</summary>
    public string DataDirectory { get; set; } = DefaultDataDirectory;

    /// <summary>Start on the live feed (default) or replay an archived session path.</summary>
    public string? ReplayPath { get; set; }

    /// <summary>Serve the dashboard from this folder instead of the embedded build (frontend development).</summary>
    public string? WebRoot { get; set; }

    public static string DefaultDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PitWall");
}
