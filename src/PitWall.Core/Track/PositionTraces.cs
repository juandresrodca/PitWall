using System.Text.Json.Nodes;
using PitWall.Core.Json;
using PitWall.Core.LiveTiming;

namespace PitWall.Core.Track;

/// <summary>Collects per-car on-track position traces from <c>Position.z</c> messages.</summary>
public sealed class PositionTraces
{
    private readonly Dictionary<string, List<Point>> _traces = new(StringComparer.Ordinal);

    public int SampleCount { get; private set; }

    public IEnumerable<IReadOnlyList<Point>> Traces => _traces.Values;

    /// <summary>Adds one compressed message (the base64 body of a Position.z line or feed update).</summary>
    public void AddCompressed(string base64) => Add(JsonNode.Parse(Compression.Inflate(base64)));

    public void Add(JsonNode? decoded)
    {
        if (decoded?["Position"] is not JsonArray samples)
        {
            return;
        }

        foreach (var sample in samples)
        {
            if (sample?["Entries"] is not JsonObject entries)
            {
                continue;
            }
            foreach (var (number, car) in entries)
            {
                if (car?["Status"].AsString() != "OnTrack")
                {
                    continue;
                }
                var x = car["X"].AsInt();
                var y = car["Y"].AsInt();
                if (x == 0 && y == 0)
                {
                    continue;
                }
                if (!_traces.TryGetValue(number, out var trace))
                {
                    _traces[number] = trace = [];
                }
                trace.Add(new Point(x, y));
                SampleCount++;
            }
        }
    }

    public TrackOutline? BuildOutline() => TrackOutlineBuilder.Build(Traces);
}
