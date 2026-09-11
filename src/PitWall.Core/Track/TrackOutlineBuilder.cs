namespace PitWall.Core.Track;

public readonly record struct Point(double X, double Y);

/// <summary>A closed circuit outline in feed coordinates (tenths of a metre).</summary>
public sealed record TrackOutline(IReadOnlyList<Point> Points, double Length, int CircuitKey = 0);

/// <summary>
/// Derives a circuit outline from car positions alone, so no third-party map data is needed.
/// </summary>
/// <remarks>
/// A single car's position trace is a sequence of laps. Starting from many points along the
/// trace, walk forward until the car comes back to where it started: that loop is one lap.
/// Pit-lane loops and laps with dropped samples produce odd lengths, so the loop closest to
/// the median length wins, then it is resampled to evenly spaced points.
/// </remarks>
public static class TrackOutlineBuilder
{
    private const int MinimumTracePoints = 200;
    private const int OutputPoints = 600;

    public static TrackOutline? Build(IEnumerable<IReadOnlyList<Point>> carTraces)
    {
        var traces = carTraces
            .Select(Deduplicate)
            .Where(t => t.Count >= MinimumTracePoints)
            .OrderByDescending(t => t.Count)
            .Take(4)
            .ToList();
        if (traces.Count == 0)
        {
            return null;
        }

        var diagonal = Diagonal(traces.SelectMany(t => t));
        if (diagonal <= 0)
        {
            return null;
        }

        // A closed loop that spans its bounding box is at least twice the diagonal long;
        // 1.6 leaves room for tracks that do not fill their box.
        var minimumLoop = diagonal * 1.6;
        var maximumLoop = diagonal * 6;
        var closeEnough = diagonal * 0.012;

        var loops = new List<(IReadOnlyList<Point> Points, double Length, double MaxGap)>();
        foreach (var trace in traces)
        {
            var step = Math.Max(1, trace.Count / 60);
            for (var start = 0; start < trace.Count; start += step)
            {
                if (FindLoop(trace, start, minimumLoop, maximumLoop, closeEnough) is { } loop)
                {
                    loops.Add(loop);
                }
            }
        }

        if (loops.Count == 0)
        {
            return null;
        }

        var lengths = loops.Select(l => l.Length).Order().ToList();
        var median = lengths[lengths.Count / 2];
        var gapLimit = diagonal * 0.05;
        var best = loops
            .OrderBy(l => Math.Abs(l.Length - median) / median + (l.MaxGap > gapLimit ? 1 : 0))
            .First();

        var resampled = Resample(best.Points, OutputPoints);
        return new TrackOutline(Smooth(resampled), best.Length);
    }

    private static (IReadOnlyList<Point>, double, double)? FindLoop(
        IReadOnlyList<Point> trace, int start, double minimum, double maximum, double closeEnough)
    {
        var origin = trace[start];
        var length = 0.0;
        var maxGap = 0.0;
        for (var i = start + 1; i < trace.Count; i++)
        {
            var gap = Distance(trace[i - 1], trace[i]);
            length += gap;
            maxGap = Math.Max(maxGap, gap);
            if (length > maximum)
            {
                return null;
            }
            if (length >= minimum && Distance(trace[i], origin) <= closeEnough)
            {
                var points = new List<Point>(i - start + 1);
                for (var j = start; j <= i; j++)
                {
                    points.Add(trace[j]);
                }
                return (points, length, maxGap);
            }
        }
        return null;
    }

    private static List<Point> Deduplicate(IReadOnlyList<Point> trace)
    {
        var result = new List<Point>(trace.Count);
        foreach (var p in trace)
        {
            if (result.Count == 0 || Distance(result[^1], p) > 1)
            {
                result.Add(p);
            }
        }
        return result;
    }

    internal static List<Point> Resample(IReadOnlyList<Point> points, int count)
    {
        var cumulative = new double[points.Count];
        for (var i = 1; i < points.Count; i++)
        {
            cumulative[i] = cumulative[i - 1] + Distance(points[i - 1], points[i]);
        }

        var total = cumulative[^1];
        var result = new List<Point>(count);
        var segment = 1;
        for (var k = 0; k < count; k++)
        {
            var target = total * k / count;
            while (segment < points.Count - 1 && cumulative[segment] < target)
            {
                segment++;
            }
            var span = cumulative[segment] - cumulative[segment - 1];
            var t = span <= 0 ? 0 : (target - cumulative[segment - 1]) / span;
            var a = points[segment - 1];
            var b = points[segment];
            result.Add(new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
        }
        return result;
    }

    /// <summary>Three-point moving average on a closed loop; removes GPS jitter without rounding corners off.</summary>
    private static List<Point> Smooth(IReadOnlyList<Point> points)
    {
        var n = points.Count;
        var result = new List<Point>(n);
        for (var i = 0; i < n; i++)
        {
            var a = points[(i - 1 + n) % n];
            var b = points[i];
            var c = points[(i + 1) % n];
            result.Add(new Point((a.X + 2 * b.X + c.X) / 4, (a.Y + 2 * b.Y + c.Y) / 4));
        }
        return result;
    }

    private static double Diagonal(IEnumerable<Point> points)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in points)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }
        return minX == double.MaxValue ? 0 : Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
