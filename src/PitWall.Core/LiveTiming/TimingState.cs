using System.Text;
using System.Text.Json.Nodes;
using PitWall.Core.Json;

namespace PitWall.Core.LiveTiming;

/// <summary>Something the dashboard needs to hear about after a feed message is applied.</summary>
public abstract record StateChange(string Kind);

/// <summary>A topic delta, forwarded verbatim so browsers merge it the same way the server did.</summary>
public sealed record PatchChange(string Topic, string Json) : StateChange("patch");

/// <summary>Latest car positions (compact form, see <see cref="TimingState.PositionsJson"/>).</summary>
public sealed record PositionsChange(string Json) : StateChange("pos");

/// <summary>Latest car telemetry (compact form, see <see cref="TimingState.TelemetryJson"/>).</summary>
public sealed record TelemetryChange(string Json) : StateChange("tel");

/// <summary>A completed lap was recorded for one driver.</summary>
public sealed record LapChange(string Json) : StateChange("lap");

/// <summary>The feed moved on to a different session; clients should reload everything.</summary>
public sealed record SessionChange(string? SessionPath) : StateChange("session");

/// <summary>
/// The accumulated live-timing picture. Not thread-safe: the owner serialises access.
/// </summary>
public sealed class TimingState
{
    private JsonObject _root = new();
    private readonly SortedDictionary<string, int[]> _telemetry = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int[]> _positions = new(StringComparer.Ordinal);
    private readonly LapHistory _laps = new();
    private DateTimeOffset? _telemetryUtc;
    private DateTimeOffset? _positionsUtc;

    /// <summary>Archive path of the current session, e.g. <c>2026/2026-09-06_Italian_Grand_Prix/2026-09-06_Race/</c>.</summary>
    public string? SessionPath => _root[Topics.SessionInfo]?["Path"].AsString();

    /// <summary>True once any subscriber-only topic has produced data.</summary>
    public bool HasSubscriberData => _positions.Count > 0 || _telemetry.Count > 0;

    public LapHistory Laps => _laps;

    public void Reset()
    {
        _root = new JsonObject();
        _telemetry.Clear();
        _positions.Clear();
        _laps.Clear();
        _telemetryUtc = null;
        _positionsUtc = null;
    }

    /// <summary>Replaces everything with the full state a Subscribe call returns.</summary>
    public void Load(JsonObject subscribeResponse)
    {
        Reset();
        foreach (var (topic, value) in subscribeResponse.ToList())
        {
            if (value is null)
            {
                continue;
            }
            subscribeResponse.Remove(topic);
            Apply(topic, value);
        }
    }

    /// <summary>Applies one feed message and reports what changed.</summary>
    public IReadOnlyList<StateChange> Apply(string topic, JsonNode? data)
    {
        if (data is null)
        {
            return [];
        }

        return topic switch
        {
            Topics.CarData => ApplyCarData(data),
            Topics.Position => ApplyPositions(data),
            _ => ApplyTopic(topic, data),
        };
    }

    /// <summary>Convenience for archive replay, where bodies arrive as raw text.</summary>
    public IReadOnlyList<StateChange> Apply(string topic, string json)
    {
        if (Topics.IsCompressed(topic))
        {
            return Apply(topic, JsonValue.Create(json.Trim().Trim('"')));
        }
        return Apply(topic, JsonNode.Parse(json));
    }

    private IReadOnlyList<StateChange> ApplyTopic(string topic, JsonNode data)
    {
        var previousPath = SessionPath;
        var raw = data.ToJsonString();
        _root[topic] = JsonMerge.Merge(_root[topic], data);

        var changes = new List<StateChange>(2) { new PatchChange(topic, raw) };

        if (topic == Topics.TimingData)
        {
            foreach (var lap in _laps.Observe(_root))
            {
                changes.Add(new LapChange(lap));
            }
        }
        else if (topic == Topics.SessionInfo && SessionPath != previousPath && previousPath is not null)
        {
            changes.Add(new SessionChange(SessionPath));
        }

        return changes;
    }

    private IReadOnlyList<StateChange> ApplyCarData(JsonNode data)
    {
        var doc = Decode(data);
        if (doc?["Entries"] is not JsonArray entries || entries.Count == 0)
        {
            return [];
        }

        foreach (var entry in entries)
        {
            if (entry?["Cars"] is not JsonObject cars)
            {
                continue;
            }
            foreach (var (number, car) in cars)
            {
                if (car?["Channels"] is not JsonObject channels)
                {
                    continue;
                }
                // Channel ids: 0 rpm, 2 speed, 3 gear, 4 throttle, 5 brake.
                _telemetry[number] =
                [
                    channels["0"].AsInt(), channels["2"].AsInt(), channels["3"].AsInt(),
                    channels["4"].AsInt(), channels["5"].AsInt(),
                ];
            }
            _telemetryUtc = entry["Utc"].AsDate() ?? _telemetryUtc;
        }

        return [new TelemetryChange(TelemetryJson())];
    }

    private IReadOnlyList<StateChange> ApplyPositions(JsonNode data)
    {
        var doc = Decode(data);
        if (doc?["Position"] is not JsonArray samples || samples.Count == 0)
        {
            return [];
        }

        foreach (var sample in samples)
        {
            if (sample?["Entries"] is not JsonObject entries)
            {
                continue;
            }
            foreach (var (number, car) in entries)
            {
                if (car is null)
                {
                    continue;
                }
                var onTrack = car["Status"].AsString() == "OnTrack" ? 1 : 0;
                _positions[number] = [car["X"].AsInt(), car["Y"].AsInt(), onTrack];
            }
            _positionsUtc = sample["Timestamp"].AsDate() ?? _positionsUtc;
        }

        return [new PositionsChange(PositionsJson())];
    }

    /// <summary><c>{"utc":"…","cars":{"44":[rpm,speed,gear,throttle,brake]}}</c></summary>
    public string TelemetryJson() => CompactJson(_telemetryUtc, _telemetry);

    /// <summary><c>{"utc":"…","cars":{"44":[x,y,onTrack]}}</c></summary>
    public string PositionsJson() => CompactJson(_positionsUtc, _positions);

    /// <summary>Everything a freshly connected browser needs, as one JSON document.</summary>
    public string SnapshotJson()
    {
        var sb = new StringBuilder(256 * 1024);
        sb.Append("{\"state\":").Append(_root.ToJsonString())
          .Append(",\"pos\":").Append(PositionsJson())
          .Append(",\"tel\":").Append(TelemetryJson())
          .Append(",\"laps\":").Append(_laps.ToJson())
          .Append('}');
        return sb.ToString();
    }

    public JsonNode? Topic(string topic) => _root[topic];

    private static JsonNode? Decode(JsonNode data)
    {
        if (data is JsonValue value && value.TryGetValue<string>(out var base64))
        {
            return JsonNode.Parse(Compression.Inflate(base64));
        }
        return data;
    }

    private static string CompactJson(DateTimeOffset? utc, SortedDictionary<string, int[]> cars)
    {
        var sb = new StringBuilder(cars.Count * 32 + 48);
        sb.Append("{\"utc\":");
        sb.Append(utc is null ? "null" : $"\"{utc.Value.UtcDateTime:O}\"");
        sb.Append(",\"cars\":{");
        var first = true;
        foreach (var (number, values) in cars)
        {
            if (!first)
            {
                sb.Append(',');
            }
            first = false;
            sb.Append('"').Append(number).Append("\":[").AppendJoin(',', values).Append(']');
        }
        sb.Append("}}");
        return sb.ToString();
    }
}
