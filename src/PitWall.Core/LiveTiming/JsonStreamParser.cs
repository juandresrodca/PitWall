using System.Globalization;

namespace PitWall.Core.LiveTiming;

/// <summary>One archived feed message: when it arrived relative to the stream start, and its body.</summary>
public sealed record StreamLine(TimeSpan Offset, string Topic, string Json);

/// <summary>
/// Reads the archive's <c>.jsonStream</c> files. Each line is a <c>HH:MM:SS.fff</c> offset
/// immediately followed by the message body, e.g. <c>00:12:50.677{"Status":"1"}</c>.
/// Compressed topics carry a quoted base64 string instead of an object.
/// </summary>
public static class JsonStreamParser
{
    private const int OffsetLength = 12; // "HH:MM:SS.fff"

    public static IEnumerable<StreamLine> Parse(string topic, TextReader reader)
    {
        while (reader.ReadLine() is { } line)
        {
            if (TryParseLine(topic, line, out var parsed))
            {
                yield return parsed;
            }
        }
    }

    public static bool TryParseLine(string topic, string line, out StreamLine parsed)
    {
        parsed = null!;
        var text = line.TrimStart('﻿').TrimEnd();
        if (text.Length <= OffsetLength)
        {
            return false;
        }

        if (!TimeSpan.TryParseExact(
                text[..OffsetLength], @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out var offset))
        {
            return false;
        }

        parsed = new StreamLine(offset, topic, text[OffsetLength..]);
        return true;
    }
}
