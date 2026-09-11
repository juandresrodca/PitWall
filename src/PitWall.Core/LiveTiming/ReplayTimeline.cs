using System.Text.Json.Nodes;
using PitWall.Core.Json;

namespace PitWall.Core.LiveTiming;

/// <summary>
/// Every archived message of one session, interleaved across topics in arrival order.
/// </summary>
public sealed class ReplayTimeline
{
    private ReplayTimeline(List<StreamLine> lines, DateTimeOffset? utcAtZero, TimeSpan? greenFlag)
    {
        Lines = lines;
        UtcAtZero = utcAtZero;
        GreenFlag = greenFlag;
    }

    public IReadOnlyList<StreamLine> Lines { get; }

    /// <summary>Wall-clock time of offset zero, derived from the Heartbeat topic.</summary>
    public DateTimeOffset? UtcAtZero { get; }

    /// <summary>When the session actually started (first <c>SessionStatus: Started</c>).</summary>
    public TimeSpan? GreenFlag { get; }

    public TimeSpan Duration => Lines.Count == 0 ? TimeSpan.Zero : Lines[^1].Offset;

    public static ReplayTimeline Create(IEnumerable<StreamLine> lines)
    {
        // OrderBy is stable, so messages sharing an offset keep their per-topic order.
        var sorted = lines.OrderBy(l => l.Offset).ToList();

        DateTimeOffset? utcAtZero = null;
        TimeSpan? greenFlag = null;
        foreach (var line in sorted)
        {
            if (utcAtZero is null && line.Topic == Topics.Heartbeat
                && TryParse(line.Json)?["Utc"].AsDate() is { } utc)
            {
                utcAtZero = utc - line.Offset;
            }
            if (greenFlag is null && line.Topic == Topics.SessionStatus
                && TryParse(line.Json)?["Status"].AsString() == "Started")
            {
                greenFlag = line.Offset;
            }
            if (utcAtZero is not null && greenFlag is not null)
            {
                break;
            }
        }

        return new ReplayTimeline(sorted, utcAtZero, greenFlag);
    }

    /// <summary>Index of the first line at or after <paramref name="offset"/>.</summary>
    public int IndexAt(TimeSpan offset)
    {
        int lo = 0, hi = Lines.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (Lines[mid].Offset < offset)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }
        return lo;
    }

    public DateTimeOffset? UtcAt(TimeSpan offset) => UtcAtZero + offset;

    private static JsonNode? TryParse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
