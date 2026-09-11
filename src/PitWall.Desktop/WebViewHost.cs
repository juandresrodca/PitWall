using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PitWall.Server;

namespace PitWall.Desktop;

/// <summary>
/// One WebView2 profile for every PitWall window, so signing in to F1 TV in the login
/// window also signs in the embedded video window.
/// </summary>
internal static class WebViewHost
{
    private static Task<CoreWebView2Environment>? _environment;

    public static string ProfileFolder => Path.Combine(PitWallOptions.DefaultDataDirectory, "WebView2");

    public static bool RuntimeInstalled()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
    }

    public static Task<CoreWebView2Environment> Environment =>
        _environment ??= CoreWebView2Environment.CreateAsync(null, ProfileFolder,
            new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));

    public static async Task InitializeAsync(WebView2 view)
    {
        await view.EnsureCoreWebView2Async(await Environment);
        view.CoreWebView2.Settings.IsStatusBarEnabled = false;
        view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = Debugger.IsAttached;
        view.CoreWebView2.Settings.AreDevToolsEnabled = Debugger.IsAttached;
    }

    /// <summary>Opens a link in the user's normal browser. Only web links, never file or custom schemes.</summary>
    public static void OpenExternal(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }
}
