using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PitWall.Core.LiveTiming;
using PitWall.Core.Track;
using PitWall.Server.Archive;

namespace PitWall.Server.Services;

public sealed record TrackMap(int CircuitKey, string BuiltFrom, int[][] Points, double Length);

/// <summary>
/// Circuit outlines, built from archived car positions and cached per circuit forever
/// (circuits change rarely; delete <c>tracks/</c> in the data folder to rebuild).
/// </summary>
public sealed class TrackMapService(ArchiveClient archive, PitWallOptions options, ILogger<TrackMapService> logger)
{
    private readonly ConcurrentDictionary<int, Lazy<Task<TrackMap?>>> _byCircuit = new();

    /// <param name="circuitKey">
    /// The circuit from the feed's own SessionInfo when known. A live session is not in the
    /// archive index until it has been archived, so looking it up by path would fail mid-session.
    /// </param>
    public async Task<TrackMap?> GetForSessionAsync(string sessionPath, int circuitKey, CancellationToken cancellation)
    {
        if (!int.TryParse(sessionPath.Split('/')[0], out var year))
        {
            return null;
        }
        if (circuitKey == 0)
        {
            circuitKey = (await archive.FindSessionAsync(sessionPath, cancellation))?.CircuitKey ?? 0;
        }
        if (circuitKey == 0)
        {
            return null;
        }

        var lazy = _byCircuit.GetOrAdd(circuitKey, key => new Lazy<Task<TrackMap?>>(() => BuildAsync(key, year)));
        var map = await lazy.Value;
        if (map is null)
        {
            // Let a later request retry, e.g. once the first session at a new circuit is archived.
            _byCircuit.TryRemove(circuitKey, out _);
        }
        return map;
    }

    /// <summary>Warms the cache in the background so the map is ready when the dashboard asks.</summary>
    public void Prefetch(string? sessionPath, int circuitKey)
    {
        if (sessionPath is null)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await GetForSessionAsync(sessionPath, circuitKey, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Track map prefetch failed for {Path}", sessionPath);
            }
        });
    }

    private async Task<TrackMap?> BuildAsync(int circuitKey, int year)
    {
        var cacheFile = Path.Combine(options.DataDirectory, "tracks", $"{circuitKey}.json");
        if (File.Exists(cacheFile))
        {
            try
            {
                return JsonSerializer.Deserialize<TrackMap>(await File.ReadAllTextAsync(cacheFile), JsonSerializerOptions.Web);
            }
            catch (JsonException)
            {
                File.Delete(cacheFile);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var candidates = new List<ArchiveSession>();
        foreach (var y in new[] { year, year - 1, year - 2 })
        {
            var season = await archive.GetSeasonAsync(y, CancellationToken.None);
            candidates.AddRange(season.Where(s =>
                s.CircuitKey == circuitKey && s.Path is not null && s.EndUtc < now - TimeSpan.FromMinutes(30)));
        }

        // Newest first, and races before practice: more cars, more laps, cleaner loops.
        foreach (var source in candidates
                     .OrderByDescending(s => s.StartUtc.Year)
                     .ThenByDescending(s => s.Type == "Race")
                     .ThenByDescending(s => s.StartUtc)
                     .Take(3))
        {
            var lines = await archive.GetStreamAsync(source.Path!, Topics.Position, CancellationToken.None);
            if (lines.Count == 0)
            {
                continue;
            }

            var traces = new PositionTraces();
            foreach (var line in lines)
            {
                try
                {
                    traces.AddCompressed(line.Json);
                }
                catch (Exception ex) when (ex is FormatException or JsonException or InvalidDataException)
                {
                }
            }

            var outline = traces.BuildOutline();
            if (outline is null)
            {
                continue;
            }

            var map = new TrackMap(
                circuitKey,
                $"{source.MeetingName} {source.StartUtc.Year} · {source.Name}",
                outline.Points.Select(p => new[] { (int)Math.Round(p.X), (int)Math.Round(p.Y) }).ToArray(),
                Math.Round(outline.Length));

            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(map, JsonSerializerOptions.Web));
            logger.LogInformation("Built track map for circuit {Circuit} from {Source}", circuitKey, map.BuiltFrom);
            return map;
        }

        logger.LogInformation("No archived positions yet for circuit {Circuit}", circuitKey);
        return null;
    }
}
