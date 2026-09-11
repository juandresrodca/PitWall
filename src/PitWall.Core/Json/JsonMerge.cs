using System.Text.Json.Nodes;

namespace PitWall.Core.Json;

/// <summary>
/// Applies live-timing deltas onto accumulated state.
/// </summary>
/// <remarks>
/// The feed sends partial objects, not JSON Patch. Three rules cover everything it does:
/// objects merge key by key; an object whose keys are integers merges into an array by
/// index (the feed sends <c>{"Sectors":{"1":{...}}}</c> to update the second sector); and a
/// <c>_deleted</c> key lists entries to remove. Anything else replaces the old value.
/// </remarks>
public static class JsonMerge
{
    public const string DeletedKey = "_deleted";

    /// <summary>
    /// Merges <paramref name="patch"/> into <paramref name="target"/> and returns the merged node.
    /// Nodes are moved out of <paramref name="patch"/>, so the patch must not be used afterwards.
    /// </summary>
    public static JsonNode? Merge(JsonNode? target, JsonNode? patch)
    {
        if (patch is not JsonObject patchObject)
        {
            return Detach(patch);
        }

        switch (target)
        {
            case JsonArray targetArray:
                MergeIntoArray(targetArray, patchObject);
                return targetArray;
            case JsonObject targetObject:
                MergeIntoObject(targetObject, patchObject);
                return targetObject;
            default:
                var fresh = new JsonObject();
                MergeIntoObject(fresh, patchObject);
                return fresh;
        }
    }

    private static void MergeIntoObject(JsonObject target, JsonObject patch)
    {
        foreach (var (key, value) in patch.ToList())
        {
            if (key == DeletedKey)
            {
                foreach (var name in DeletedNames(value))
                {
                    target.Remove(name);
                }
                continue;
            }

            var existing = target[key];
            if (value is JsonObject && existing is JsonObject or JsonArray)
            {
                patch.Remove(key);
                Merge(existing, value);
            }
            else
            {
                target[key] = Detach(value);
            }
        }
    }

    private static void MergeIntoArray(JsonArray target, JsonObject patch)
    {
        foreach (var (key, value) in patch.ToList())
        {
            if (key == DeletedKey)
            {
                var indexes = DeletedNames(value)
                    .Select(n => int.TryParse(n, out var i) ? i : -1)
                    .Where(i => i >= 0 && i < target.Count)
                    .OrderDescending();
                foreach (var index in indexes)
                {
                    target.RemoveAt(index);
                }
                continue;
            }

            if (!int.TryParse(key, out var slot) || slot < 0)
            {
                continue;
            }

            while (target.Count <= slot)
            {
                target.Add(null);
            }

            var existing = target[slot];
            if (value is JsonObject && existing is JsonObject or JsonArray)
            {
                patch.Remove(key);
                Merge(existing, value);
            }
            else
            {
                target[slot] = Detach(value);
            }
        }
    }

    private static IEnumerable<string> DeletedNames(JsonNode? value) => value switch
    {
        JsonArray array => array.Select(n => n?.ToString()).OfType<string>().ToList(),
        JsonValue single => [single.ToString()],
        _ => [],
    };

    /// <summary>A node can only have one parent, so take it away from its old one first.</summary>
    private static JsonNode? Detach(JsonNode? node)
    {
        if (node?.Parent is null)
        {
            return node;
        }

        switch (node.Parent)
        {
            case JsonObject parentObject:
                parentObject.Remove(node.GetPropertyName());
                return node;
            case JsonArray parentArray:
                var index = node.GetElementIndex();
                parentArray[index] = null;
                return node;
            default:
                return node.DeepClone();
        }
    }
}
