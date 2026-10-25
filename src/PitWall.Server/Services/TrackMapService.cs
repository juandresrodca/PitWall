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
    private readonly ConcurrentDictionary<int, DateTimeOffset> _retryAfter = new();

    /// <summary>How long to wait before rebuilding a circuit whose outline could not be made.</summary>
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The finished outline, or null with <paramref name="building"/> set while one is being
    /// made. The first build of a circuit downloads a whole session's positions and can take
    /// minutes, so this never holds the request open: callers poll instead.
    /// </summary>
    /// <param name="circuitKey">
    /// The circuit from the feed's own SessionInfo when known. A live session is not in the
    /// archive index until it has been archived, so looking it up by path would fail mid-session.
    /// </param>
    public TrackMap? TryGet(string sessionPath, int circuitKey, out bool building)
    {
        building = false;
        if (!int.TryParse(sessionPath.Split('/')[0], out var year))
        {
            return null;
        }

        if (circuitKey == 0)
        {
            // Resolving it needs the archive index; fetch that in the background and let the
            // caller poll, rather than blocking on the network inside a request.
            building = true;
            StartResolve(sessionPath, year);
            return null;
        }

        if (_retryAfter.TryGetValue(circuitKey, out var until) && until > DateTimeOffset.UtcNow)
        {
            return null; // Tried recently and there was nothing to build from.
        }

        var task = _byCircuit.GetOrAdd(circuitKey, key => new Lazy<Task<TrackMap?>>(() => BuildAsync(key, year))).Value;
        if (!task.IsCompleted)
        {
            building = true;
            return null;
        }

        if (task.IsCompletedSuccessfully && task.Result is { } map)
        {
            return map;
        }

        // Nothing to build from (a brand-new circuit, or the archive refused us). Try again
        // later — a session gets archived a few hours after it runs — but not on every poll.
        _byCircuit.TryRemove(circuitKey, out _);
        _retryAfter[circuitKey] = DateTimeOffset.UtcNow + FailureCooldown;
        return null;
    }

    private readonly ConcurrentDictionary<string, byte> _resolving = new();

    private void StartResolve(string sessionPath, int year)
    {
        if (!_resolving.TryAdd(sessionPath, 0))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var key = (await archive.FindSessionAsync(sessionPath, CancellationToken.None))?.CircuitKey ?? 0;
                if (key != 0)
                {
                    _ = _byCircuit.GetOrAdd(key, k => new Lazy<Task<TrackMap?>>(() => BuildAsync(k, year))).Value;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not resolve the circuit for {Path}", sessionPath);
            }
            finally
            {
                _resolving.TryRemove(sessionPath, out _);
            }
        });
    }

    /// <summary>Warms the cache in the background so the map is ready when the dashboard asks.</summary>
    public void Prefetch(string? sessionPath, int circuitKey)
    {
        if (sessionPath is not null)
        {
            TryGet(sessionPath, circuitKey, out _);
        }
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
