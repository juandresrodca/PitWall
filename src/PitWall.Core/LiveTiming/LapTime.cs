using System.Globalization;

namespace PitWall.Core.LiveTiming;

public static class LapTime
{
    /// <summary>Parses feed times such as <c>1:21.456</c>, <c>81.456</c> or <c>+0.695</c> into milliseconds.</summary>
    public static bool TryParseMs(string? text, out int milliseconds)
    {
        milliseconds = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim().TrimStart('+');
        var minutes = 0;
        var colon = value.IndexOf(':');
        if (colon > 0)
        {
            if (!int.TryParse(value[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out minutes))
            {
                return false;
            }
            value = value[(colon + 1)..];
        }

        if (!double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        milliseconds = (int)Math.Round((minutes * 60 + seconds) * 1000);
        return true;
    }
}
