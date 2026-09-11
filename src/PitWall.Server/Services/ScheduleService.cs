using System.Globalization;
using System.Text.Json.Nodes;
using PitWall.Core.Json;

namespace PitWall.Server.Services;

public sealed record WeekendSession(string Kind, string Name, DateTimeOffset Start, DateTimeOffset End);

public sealed record RaceWeekend(
    int Season,
    int Round,
    string Name,
    string CircuitId,
    string Circuit,
    string Locality,
    string Country,
    double Latitude,
    double Longitude,
    IReadOnlyList<WeekendSession> Sessions)
{
    public DateTimeOffset Start => Sessions[0].Start;
    public DateTimeOffset End => Sessions[^1].End;
}

public sealed record Season(int Year, IReadOnlyList<RaceWeekend> Rounds);

public sealed record DriverStanding(int Position, double Points, int Wins, string Code, string Number, string FirstName, string LastName, string Team, string Nationality);

public sealed record TeamStanding(int Position, double Points, int Wins, string Name, string Nationality);

public sealed record Standings(int Season, int Round, IReadOnlyList<DriverStanding> Drivers, IReadOnlyList<TeamStanding> Teams);

/// <summary>Calendar and championship tables from the Jolpica mirror of the Ergast API.</summary>
public sealed class ScheduleService
{
    private const string Api = "https://api.jolpi.ca/ergast/f1/";

    private readonly HttpClient _http;
    private readonly CachedValue<Season> _season;
    private readonly CachedValue<Standings> _standings;

    public ScheduleService(HttpClient http)
    {
        _http = http;
        _season = new CachedValue<Season>(TimeSpan.FromHours(1), LoadSeasonAsync);
        _standings = new CachedValue<Standings>(TimeSpan.FromMinutes(20), LoadStandingsAsync);
    }

    public Task<Season?> GetSeasonAsync(CancellationToken cancellation) => _season.GetAsync(cancellation);

    public Task<Standings?> GetStandingsAsync(CancellationToken cancellation) => _standings.GetAsync(cancellation);

    /// <summary>The weekend in progress, or else the next one. Weekends run until four hours after the race.</summary>
    public async Task<RaceWeekend?> GetCurrentWeekendAsync(CancellationToken cancellation)
    {
        var season = await GetSeasonAsync(cancellation);
        var now = DateTimeOffset.UtcNow;
        return season?.Rounds.FirstOrDefault(r => r.End + TimeSpan.FromHours(4) > now);
    }

    private async Task<Season> LoadSeasonAsync(CancellationToken cancellation)
    {
        var root = await GetJsonAsync("current.json?limit=100", cancellation);
        var races = root?["MRData"]?["RaceTable"]?["Races"] as JsonArray ?? [];
        var year = root?["MRData"]?["RaceTable"]?["season"].AsInt() ?? DateTime.UtcNow.Year;

        var rounds = new List<RaceWeekend>();
        foreach (var race in races)
        {
            if (race is null)
            {
                continue;
            }

            var sessions = new List<WeekendSession>();
            void Add(string key, string kind, string name, TimeSpan length)
            {
                if (When(race[key]) is { } start)
                {
                    sessions.Add(new WeekendSession(kind, name, start, start + length));
                }
            }

            Add("FirstPractice", "FP1", "Practice 1", TimeSpan.FromHours(1));
            Add("SecondPractice", "FP2", "Practice 2", TimeSpan.FromHours(1));
            Add("ThirdPractice", "FP3", "Practice 3", TimeSpan.FromHours(1));
            Add("SprintQualifying", "SQ", "Sprint Qualifying", TimeSpan.FromMinutes(45));
            Add("SprintShootout", "SQ", "Sprint Shootout", TimeSpan.FromMinutes(45));
            Add("Sprint", "SPR", "Sprint", TimeSpan.FromHours(1));
            Add("Qualifying", "Q", "Qualifying", TimeSpan.FromHours(1));
            if (When(race) is { } raceStart)
            {
                sessions.Add(new WeekendSession("R", "Race", raceStart, raceStart + TimeSpan.FromHours(2)));
            }
            if (sessions.Count == 0)
            {
                continue;
            }
            sessions.Sort((a, b) => a.Start.CompareTo(b.Start));

            var circuit = race["Circuit"];
            var location = circuit?["Location"];
            rounds.Add(new RaceWeekend(
                year,
                race["round"].AsInt(),
                race["raceName"].AsString() ?? "",
                circuit?["circuitId"].AsString() ?? "",
                circuit?["circuitName"].AsString() ?? "",
                location?["locality"].AsString() ?? "",
                location?["country"].AsString() ?? "",
                Double(location?["lat"]),
                Double(location?["long"]),
                sessions));
        }

        return new Season(year, rounds);
    }

    private async Task<Standings> LoadStandingsAsync(CancellationToken cancellation)
    {
        var driversTask = GetJsonAsync("current/driverStandings.json", cancellation);
        var teamsTask = GetJsonAsync("current/constructorStandings.json", cancellation);
        var driversRoot = await driversTask;
        var teamsRoot = await teamsTask;

        var driverList = driversRoot?["MRData"]?["StandingsTable"]?["StandingsLists"]?[0];
        var teamList = teamsRoot?["MRData"]?["StandingsTable"]?["StandingsLists"]?[0];

        var drivers = (driverList?["DriverStandings"] as JsonArray ?? [])
            .Where(d => d is not null)
            .Select(d => new DriverStanding(
                d!["position"].AsInt(),
                Double(d["points"]),
                d["wins"].AsInt(),
                d["Driver"]?["code"].AsString() ?? "",
                d["Driver"]?["permanentNumber"].AsString() ?? "",
                d["Driver"]?["givenName"].AsString() ?? "",
                d["Driver"]?["familyName"].AsString() ?? "",
                (d["Constructors"] as JsonArray)?.LastOrDefault()?["name"].AsString() ?? "",
                d["Driver"]?["nationality"].AsString() ?? ""))
            .ToList();

        var teams = (teamList?["ConstructorStandings"] as JsonArray ?? [])
            .Where(t => t is not null)
            .Select(t => new TeamStanding(
                t!["position"].AsInt(),
                Double(t["points"]),
                t["wins"].AsInt(),
                t["Constructor"]?["name"].AsString() ?? "",
                t["Constructor"]?["nationality"].AsString() ?? ""))
            .ToList();

        return new Standings(driverList?["season"].AsInt() ?? 0, driverList?["round"].AsInt() ?? 0, drivers, teams);
    }

    private async Task<JsonNode?> GetJsonAsync(string path, CancellationToken cancellation)
    {
        using var response = await _http.GetAsync(Api + path, cancellation);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation));
    }

    private static DateTimeOffset? When(JsonNode? node)
    {
        var date = node?["date"].AsString();
        var time = node?["time"].AsString() ?? "00:00:00Z";
        return DateTimeOffset.TryParse($"{date}T{time}", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static double Double(JsonNode? node) =>
        double.TryParse(node.AsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
}
