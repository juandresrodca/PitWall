using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using PitWall.Core.Json;
using PitWall.Core.LiveTiming;
using PitWall.Server.Auth;

namespace PitWall.Server.Archive;

public sealed record ArchiveSession(
    int Key,
    string Type,
    string Name,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string? Path,
    int MeetingKey,
    string MeetingName,
    string Location,
    string CountryCode,
    int CircuitKey);

/// <summary>
/// Reads Formula 1's timing archive (livetiming.formula1.com/static). Completed sessions are
/// cached on disk because they never change and some streams are ~10 MB.
/// </summary>
/// <remarks>
/// Since 15 October 2026 the archive is behind the F1 TV paywall: without a subscription token
/// every file answers 401. Requests therefore carry the saved token, and a 401 is reported as
/// "sign in", not as "this session has no data" — sessions already cached still replay offline.
/// </remarks>
public sealed class ArchiveClient(HttpClient http, TokenStore tokens, PitWallOptions options)
{
    public const string BaseUrl = "https://livetiming.formula1.com/static/";

    private readonly ConcurrentDictionary<int, (DateTimeOffset Fetched, IReadOnlyList<ArchiveSession> Sessions)> _seasons = new();

    /// <summary>True when Formula 1 last answered 401: the archive needs an F1 TV sign-in.</summary>
    public bool NeedsSignIn { get; private set; }

    /// <summary>GET with the F1 TV token attached, and 401 remembered for the UI.</summary>
    private async Task<HttpResponseMessage> GetAsync(string url, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (tokens.UsableToken(DateTimeOffset.UtcNow) is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await http.SendAsync(request, cancellation);
        NeedsSignIn = response.StatusCode == HttpStatusCode.Unauthorized;
        return response;
    }

    public async Task<IReadOnlyList<ArchiveSession>> GetSeasonAsync(int year, CancellationToken cancellation)
    {
        if (_seasons.TryGetValue(year, out var cached) && DateTimeOffset.UtcNow - cached.Fetched < TimeSpan.FromMinutes(10))
        {
            return cached.Sessions;
        }

        using var response = await GetAsync($"{BaseUrl}{year}/Index.json", cancellation);
        if (!response.IsSuccessStatusCode)
        {
            return cached.Sessions ?? [];
        }

        var text = (await response.Content.ReadAsStringAsync(cancellation)).TrimStart('﻿');
        var sessions = ParseSeason(JsonNode.Parse(text));
        _seasons[year] = (DateTimeOffset.UtcNow, sessions);
        return sessions;
    }

    internal static IReadOnlyList<ArchiveSession> ParseSeason(JsonNode? index)
    {
        var result = new List<ArchiveSession>();
        if (index?["Meetings"] is not JsonArray meetings)
        {
            return result;
        }

        foreach (var meeting in meetings)
        {
            if (meeting?["Sessions"] is not JsonArray sessions)
            {
                continue;
            }
            foreach (var s in sessions)
            {
                if (s is null)
                {
                    continue;
                }
                var offset = TimeSpan.TryParse(s["GmtOffset"].AsString(), out var o) ? o : TimeSpan.Zero;
                var start = ParseLocal(s["StartDate"].AsString(), offset);
                var end = ParseLocal(s["EndDate"].AsString(), offset);
                if (start is null || end is null)
                {
                    continue;
                }
                result.Add(new ArchiveSession(
                    s["Key"].AsInt(),
                    s["Type"].AsString() ?? "",
                    s["Name"].AsString() ?? "",
                    start.Value,
                    end.Value,
                    s["Path"].AsString(),
                    meeting["Key"].AsInt(),
                    meeting["Name"].AsString() ?? "",
                    meeting["Location"].AsString() ?? "",
                    meeting["Country"]?["Code"].AsString() ?? "",
                    meeting["Circuit"]?["Key"].AsInt() ?? 0));
            }
        }
        return result;
    }

    /// <summary>Downloads (or reads from cache) one topic stream of an archived session.</summary>
    public async Task<IReadOnlyList<StreamLine>> GetStreamAsync(string sessionPath, string topic, CancellationToken cancellation)
    {
        var cacheFile = CachePath(sessionPath, topic);
        string text;
        if (File.Exists(cacheFile))
        {
            text = await File.ReadAllTextAsync(cacheFile, cancellation);
        }
        else
        {
            using var response = await GetAsync($"{BaseUrl}{sessionPath}{topic}.jsonStream", cancellation);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }
            text = await response.Content.ReadAsStringAsync(cancellation);
            if (await IsFinishedAsync(sessionPath, cancellation))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
                await File.WriteAllTextAsync(cacheFile, text, cancellation);
            }
        }

        using var reader = new StringReader(text);
        return JsonStreamParser.Parse(topic, reader).ToList();
    }

    public async Task<ArchiveSession?> FindSessionAsync(string sessionPath, CancellationToken cancellation)
    {
        if (!int.TryParse(sessionPath.Split('/')[0], out var year))
        {
            return null;
        }
        var season = await GetSeasonAsync(year, cancellation);
        return season.FirstOrDefault(s => s.Path == sessionPath);
    }

    private async Task<bool> IsFinishedAsync(string sessionPath, CancellationToken cancellation)
    {
        var session = await FindSessionAsync(sessionPath, cancellation);
        return session is not null && session.EndUtc < DateTimeOffset.UtcNow - TimeSpan.FromHours(2);
    }

    private string CachePath(string sessionPath, string topic)
    {
        var safe = string.Join(Path.DirectorySeparatorChar,
            sessionPath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => string.Concat(part.Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != '.'))));
        return Path.Combine(options.DataDirectory, "archive", safe, topic + ".jsonStream");
    }

    private static DateTimeOffset? ParseLocal(string? text, TimeSpan offset) =>
        DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var local)
            ? new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset)
            : null;
}
