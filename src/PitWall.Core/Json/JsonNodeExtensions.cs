using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PitWall.Core.Json;

/// <summary>
/// Lenient readers. The feed is inconsistent about types (<c>"New":"true"</c> next to
/// <c>"New":true</c>, lap numbers as strings), so nothing here throws.
/// </summary>
public static class JsonNodeExtensions
{
    public static string? AsString(this JsonNode? node) => node is JsonValue value
        ? value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToJsonString(),
            _ => null,
        }
        : null;

    public static int AsInt(this JsonNode? node, int fallback = 0)
    {
        if (node is not JsonValue value)
        {
            return fallback;
        }
        if (value.TryGetValue<int>(out var i))
        {
            return i;
        }
        var text = value.GetValueKind() switch
        {
            JsonValueKind.Number => value.ToJsonString(),
            JsonValueKind.String => value.GetValue<string>(),
            _ => null,
        };
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? (int)Math.Round(d)
            : fallback;
    }

    public static bool AsBool(this JsonNode? node) => node is JsonValue value && value.GetValueKind() switch
    {
        JsonValueKind.True => true,
        JsonValueKind.String => bool.TryParse(value.GetValue<string>(), out var b) && b,
        JsonValueKind.Number => value.AsInt() != 0,
        _ => false,
    };

    public static DateTimeOffset? AsDate(this JsonNode? node) =>
        DateTimeOffset.TryParse(node.AsString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}
