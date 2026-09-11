using System.Text.Json;
using PitWall.Server;

namespace PitWall.Desktop;

public enum VideoMode
{
    /// <summary>Microsoft Edge in app mode with its own profile. Plays F1 TV's DRM streams reliably.</summary>
    Edge,
    /// <summary>An embedded WebView2 window. Shares the sign-in with PitWall, but DRM support varies.</summary>
    WebView2,
    /// <summary>Dashboard only.</summary>
    Off,
}

/// <summary>Per-user desktop preferences, kept next to the other PitWall data. Holds no secrets.</summary>
public sealed class DesktopSettings
{
    public string? DashboardDisplay { get; set; }
    public string? VideoDisplay { get; set; }
    public VideoMode Video { get; set; } = VideoMode.Edge;
    public string VideoUrl { get; set; } = "https://f1tv.formula1.com/";
    public bool StartVideoFullscreen { get; set; } = true;
    public int Port { get; set; } = 5320;
    public bool AllowLan { get; set; }

    private static string FilePath => Path.Combine(PitWallOptions.DefaultDataDirectory, "desktop.json");

    public static DesktopSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new();
            }
        }
        catch (JsonException)
        {
        }
        return new DesktopSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>True when both saved displays are still connected (monitors get unplugged).</summary>
    public bool DisplaysValid()
    {
        var names = Screen.AllScreens.Select(s => s.DeviceName).ToHashSet();
        return DashboardDisplay is not null && names.Contains(DashboardDisplay)
            && (Video == VideoMode.Off || (VideoDisplay is not null && names.Contains(VideoDisplay)));
    }

    public Screen DashboardScreen => Find(DashboardDisplay) ?? Screen.AllScreens.FirstOrDefault(s => !s.Primary) ?? Screen.PrimaryScreen!;

    public Screen VideoScreen => Find(VideoDisplay) ?? Screen.PrimaryScreen!;

    private static Screen? Find(string? device) => Screen.AllScreens.FirstOrDefault(s => s.DeviceName == device);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
