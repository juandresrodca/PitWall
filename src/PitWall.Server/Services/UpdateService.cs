using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PitWall.Core.Json;

namespace PitWall.Server.Services;

public enum UpdateState
{
    /// <summary>Nothing checked yet.</summary>
    Unknown,
    Checking,
    UpToDate,
    Available,
    Downloading,
    /// <summary>Downloaded and verified; restarting installs it.</summary>
    Ready,
    Failed,
    /// <summary>Not an installed build — running from source, or the exe cannot be replaced.</summary>
    NotSupported,
}

public sealed record UpdateStatus
{
    public required string Current { get; init; }
    public string? Latest { get; init; }
    public string? Notes { get; init; }
    public DateTimeOffset? Published { get; init; }
    public string? ReleaseUrl { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter<UpdateState>))]
    public UpdateState State { get; init; }

    /// <summary>Download progress, 0-100.</summary>
    public int Progress { get; init; }

    public string? Error { get; init; }
}

/// <summary>
/// Updates PitWall from its own GitHub releases.
/// </summary>
/// <remarks>
/// Checking is automatic and harmless; downloading and installing happen only when the user
/// asks. The downloaded file is checked against the <c>SHA256SUMS.txt</c> published with the
/// release and discarded if it does not match, so a truncated or substituted download cannot be
/// installed. A running single-file exe cannot overwrite itself, so the swap is done by a short
/// script that waits for this process to exit, keeps the old exe aside until the new one is in
/// place, and then starts it.
/// </remarks>
public sealed class UpdateService(HttpClient http, ILogger<UpdateService> logger)
{
    public const string Repository = "juandresrodca/PitWall";

    private const string ExeAsset = "PitWall.exe";
    private const string ChecksumAsset = "SHA256SUMS.txt";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private UpdateStatus _status = new() { Current = CurrentVersion().ToString(3), State = UpdateState.Unknown };
    private DateTimeOffset _checkedAt;
    private string? _downloadUrl;
    private string? _readyFile;

    public UpdateStatus Status => _status;

    /// <summary>Looks for a newer release. Safe to call often; the result is cached.</summary>
    public async Task<UpdateStatus> CheckAsync(bool force, CancellationToken cancellation)
    {
        if (!force && _status.State != UpdateState.Unknown && DateTimeOffset.UtcNow - _checkedAt < CheckInterval)
        {
            return _status;
        }

        await _gate.WaitAsync(cancellation);
        try
        {
            if (_status.State is UpdateState.Downloading or UpdateState.Ready)
            {
                return _status;
            }
            if (InstalledExePath() is null)
            {
                return Set(_status with
                {
                    State = UpdateState.NotSupported,
                    Error = "This is a development build. Updates apply to PitWall.exe from a release.",
                });
            }

            Set(_status with { State = UpdateState.Checking, Error = null });

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode)
            {
                return Set(_status with
                {
                    State = UpdateState.Failed,
                    Error = response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? "No release has been published yet."
                        : $"GitHub answered {(int)response.StatusCode}.",
                });
            }

            var release = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation));
            _checkedAt = DateTimeOffset.UtcNow;

            if (!TryParseVersion(release?["tag_name"].AsString(), out var latest))
            {
                return Set(_status with { State = UpdateState.Failed, Error = "The latest release has no version tag." });
            }

            _downloadUrl = (release?["assets"] as JsonArray)?
                .FirstOrDefault(a => string.Equals(a?["name"].AsString(), ExeAsset, StringComparison.OrdinalIgnoreCase))
                ?["browser_download_url"].AsString();

            var newer = latest > CurrentVersion();
            return Set(new UpdateStatus
            {
                Current = CurrentVersion().ToString(3),
                Latest = latest.ToString(3),
                Notes = Trim(release?["body"].AsString()),
                Published = release?["published_at"].AsDate(),
                ReleaseUrl = release?["html_url"].AsString(),
                State = newer && _downloadUrl is not null ? UpdateState.Available : UpdateState.UpToDate,
                Error = newer && _downloadUrl is null ? $"That release has no {ExeAsset} to download." : null,
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Update check failed");
            return Set(_status with { State = UpdateState.Failed, Error = "Could not reach GitHub." });
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Downloads the new exe beside the current one and verifies its checksum.</summary>
    public async Task<UpdateStatus> DownloadAsync(CancellationToken cancellation)
    {
        if (_status.State == UpdateState.Ready)
        {
            return _status;
        }
        if (_status.State != UpdateState.Available || _downloadUrl is null)
        {
            await CheckAsync(force: true, cancellation);
            if (_status.State != UpdateState.Available || _downloadUrl is null)
            {
                return _status;
            }
        }

        await _gate.WaitAsync(cancellation);
        try
        {
            if (InstalledExePath() is not { } target)
            {
                return Set(_status with { State = UpdateState.NotSupported });
            }

            Set(_status with { State = UpdateState.Downloading, Progress = 0, Error = null });

            var expected = await ExpectedHashAsync(cancellation);
            if (expected is null)
            {
                return Set(_status with
                {
                    State = UpdateState.Failed,
                    Error = $"The release publishes no {ChecksumAsset}, so the download cannot be verified.",
                });
            }

            // Beside the target, so installing it is a rename on the same volume rather than a copy.
            var temp = target + ".new";
            await DownloadToAsync(_downloadUrl, temp, cancellation);

            var actual = await HashAsync(temp, cancellation);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temp);
                logger.LogWarning("Discarded an update that failed its checksum");
                return Set(_status with
                {
                    State = UpdateState.Failed,
                    Error = "The download did not match its published checksum and was discarded.",
                });
            }

            _readyFile = temp;
            logger.LogInformation("Update {Version} downloaded and verified", _status.Latest);
            return Set(_status with { State = UpdateState.Ready, Progress = 100 });
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Update download failed");
            return Set(_status with
            {
                State = UpdateState.Failed,
                Error = ex is UnauthorizedAccessException
                    ? "PitWall.exe sits in a folder this account cannot write to. Move it, or install the release by hand."
                    : "The download did not finish.",
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Hands the swap to a detached script. Returns true when it started, meaning the caller
    /// should now exit so the file can be replaced.
    /// </summary>
    public bool StartInstall(IEnumerable<string>? restartArgs = null)
    {
        if (_status.State != UpdateState.Ready || _readyFile is null || InstalledExePath() is not { } target)
        {
            return false;
        }

        var script = Path.Combine(Path.GetTempPath(), $"pitwall-update-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(script, InstallScript(target, _readyFile, Environment.ProcessId, restartArgs), new UTF8Encoding(false));

        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script })
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            Process.Start(start);
            logger.LogInformation("Installing update {Version} on exit", _status.Latest);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not start the update installer");
            Set(_status with { State = UpdateState.Failed, Error = "Could not start the installer." });
            return false;
        }
    }

    private static string InstallScript(string target, string staged, int processId, IEnumerable<string>? restartArgs)
    {
        var arguments = (restartArgs ?? []).Select(Quote).ToList();
        var restart = arguments.Count > 0
            ? $"Start-Process -FilePath $target -ArgumentList {string.Join(", ", arguments)}"
            : "Start-Process -FilePath $target";

        // A plain raw string with placeholders: PowerShell's braces would otherwise collide
        // with C# interpolation. Renaming the old exe aside comes first because Windows allows
        // renaming a file that is still mapped, but not overwriting one.
        return """
            $ErrorActionPreference = 'SilentlyContinue'
            $target = __TARGET__
            $staged = __STAGED__
            $backup = "$target.old"

            Wait-Process -Id __PID__ -Timeout 120

            $installed = $false
            for ($i = 0; $i -lt 40 -and -not $installed; $i++) {
                Remove-Item -LiteralPath $backup -Force
                if (Test-Path -LiteralPath $target) {
                    Rename-Item -LiteralPath $target -NewName ([IO.Path]::GetFileName($backup)) -Force
                }
                if (-not (Test-Path -LiteralPath $target)) {
                    Move-Item -LiteralPath $staged -Destination $target -Force
                    if (Test-Path -LiteralPath $target) {
                        $installed = $true
                    }
                    elseif (Test-Path -LiteralPath $backup) {
                        Rename-Item -LiteralPath $backup -NewName ([IO.Path]::GetFileName($target)) -Force
                    }
                }
                if (-not $installed) { Start-Sleep -Milliseconds 500 }
            }

            Remove-Item -LiteralPath $backup -Force
            if (Test-Path -LiteralPath $target) { __RESTART__ }
            Remove-Item -LiteralPath $PSCommandPath -Force
            """
            .Replace("__TARGET__", Quote(target))
            .Replace("__STAGED__", Quote(staged))
            .Replace("__PID__", processId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("__RESTART__", restart);
    }

    private async Task<string?> ExpectedHashAsync(CancellationToken cancellation)
    {
        var url = _downloadUrl!.Replace(ExeAsset, ChecksumAsset, StringComparison.OrdinalIgnoreCase);
        using var response = await http.GetAsync(url, cancellation);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        // Lines read "<hash>  PitWall.exe", as sha256sum and Get-FileHash produce.
        foreach (var line in (await response.Content.ReadAsStringAsync(cancellation)).Split('\n'))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2 && parts[^1].TrimStart('*').Equals(ExeAsset, StringComparison.OrdinalIgnoreCase))
            {
                return parts[0];
            }
        }
        return null;
    }

    private async Task DownloadToAsync(string url, string destination, CancellationToken cancellation)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync(cancellation);
        await using var file = File.Create(destination);

        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), cancellation);
            done += read;
            if (total > 0)
            {
                Set(_status with { State = UpdateState.Downloading, Progress = (int)(done * 100 / total) });
            }
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellation)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation));
    }

    /// <summary>The exe to replace, or null when this is not an installed release build.</summary>
    public static string? InstalledExePath()
    {
        var path = Environment.ProcessPath;
        return path is not null && Path.GetFileName(path).Equals(ExeAsset, StringComparison.OrdinalIgnoreCase)
            ? path
            : null;
    }

    private static Version CurrentVersion() =>
        typeof(UpdateService).Assembly.GetName().Version is { } v
            ? new Version(v.Major, v.Minor, v.Build)
            : new Version(0, 0, 0);

    private static bool TryParseVersion(string? tag, out Version version) =>
        Version.TryParse((tag ?? string.Empty).TrimStart('v', 'V'), out version!);

    private static string? Trim(string? notes) =>
        string.IsNullOrWhiteSpace(notes) ? null
        : notes.Length <= 1200 ? notes.Trim()
        : notes[..1200].Trim() + "…";

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private UpdateStatus Set(UpdateStatus status) => _status = status;
}
