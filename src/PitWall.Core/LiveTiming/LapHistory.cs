using System.Text;
using System.Text.Json.Nodes;
using PitWall.Core.Json;

namespace PitWall.Core.LiveTiming;

/// <summary>One completed lap as the dashboard's pace and tyre panels consume it.</summary>
public readonly record struct LapRecord(int Lap, int TimeMs, string Compound, int TyreAge, LapFlags Flags);

[Flags]
public enum LapFlags
{
    None = 0,
    /// <summary>The car was in or leaving the pit lane when the lap time was set.</summary>
    Pit = 1,
    /// <summary>Safety car, virtual safety car or red flag on track.</summary>
    Neutralised = 2,
    /// <summary>Yellow flag somewhere on track.</summary>
    Yellow = 4,
}

/// <summary>
/// Builds per-driver lap history from TimingData. The feed only ever holds the latest lap,
/// so history has to be recorded as it happens (or replayed from the archive).
/// </summary>
public sealed class LapHistory
{
    private readonly Dictionary<string, List<LapRecord>> _laps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lastSeen = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, List<LapRecord>> All => _laps;

    public void Clear()
    {
        _laps.Clear();
        _lastSeen.Clear();
    }

    /// <summary>Checks every driver for a new last-lap time and returns the laps it recorded, as JSON.</summary>
    public IEnumerable<string> Observe(JsonObject root)
    {
        if (root[Topics.TimingData]?["Lines"] is not JsonObject lines)
        {
            yield break;
        }

        foreach (var (number, line) in lines)
        {
            var value = line?["LastLapTime"]?["Value"].AsString();
            if (string.IsNullOrEmpty(value) || _lastSeen.GetValueOrDefault(number) == value)
            {
                continue;
            }
            _lastSeen[number] = value;

            if (!LapTime.TryParseMs(value, out var ms))
            {
                continue;
            }

            var lap = line!["NumberOfLaps"].AsInt();
            var (compound, age) = CurrentTyre(root, number);
            var flags = LapFlags.None;
            if (line["InPit"].AsBool() || line["PitOut"].AsBool())
            {
                flags |= LapFlags.Pit;
            }
            flags |= TrackFlags(root);

            var record = new LapRecord(lap, ms, compound, age, flags);
            if (!_laps.TryGetValue(number, out var list))
            {
                _laps[number] = list = [];
            }

            // A corrected time for the same lap replaces the earlier one.
            if (list.Count > 0 && list[^1].Lap == lap)
            {
                list[^1] = record;
            }
            else
            {
                list.Add(record);
            }

            yield return $"{{\"n\":\"{number}\",\"lap\":{Serialize(record)}}}";
        }
    }

    public string ToJson()
    {
        var sb = new StringBuilder(8 * 1024);
        sb.Append('{');
        var first = true;
        foreach (var (number, laps) in _laps)
        {
            if (!first)
            {
                sb.Append(',');
            }
            first = false;
            sb.Append('"').Append(number).Append("\":[");
            for (var i = 0; i < laps.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(Serialize(laps[i]));
            }
            sb.Append(']');
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string Serialize(LapRecord r) =>
        $"{{\"l\":{r.Lap},\"t\":{r.TimeMs},\"c\":\"{r.Compound}\",\"a\":{r.TyreAge},\"f\":{(int)r.Flags}}}";

    private static (string Compound, int Age) CurrentTyre(JsonObject root, string number)
    {
        if (root[Topics.TimingAppData]?["Lines"]?[number]?["Stints"] is not JsonArray stints)
        {
            return ("UNKNOWN", 0);
        }

        for (var i = stints.Count - 1; i >= 0; i--)
        {
            var compound = stints[i]?["Compound"].AsString();
            if (!string.IsNullOrEmpty(compound))
            {
                return (compound, stints[i]!["TotalLaps"].AsInt());
            }
        }
        return ("UNKNOWN", 0);
    }

    private static LapFlags TrackFlags(JsonObject root) =>
        root[Topics.TrackStatus]?["Status"].AsString() switch
        {
            "4" or "5" or "6" or "7" => LapFlags.Neutralised,
            "2" => LapFlags.Yellow,
            _ => LapFlags.None,
        };
}
