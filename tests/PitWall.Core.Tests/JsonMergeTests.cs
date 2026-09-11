using System.Text.Json.Nodes;
using PitWall.Core.Json;

namespace PitWall.Core.Tests;

public class JsonMergeTests
{
    private static string Merge(string target, string patch) =>
        JsonMerge.Merge(JsonNode.Parse(target), JsonNode.Parse(patch))!.ToJsonString();

    [Fact]
    public void Objects_merge_key_by_key()
    {
        var result = Merge("""{"a":1,"b":{"c":2,"d":3}}""", """{"b":{"c":9},"e":5}""");
        Assert.Equal("""{"a":1,"b":{"c":9,"d":3},"e":5}""", result);
    }

    [Fact]
    public void Integer_keyed_object_updates_array_by_index()
    {
        var result = Merge(
            """{"Sectors":[{"Value":""},{"Value":""},{"Value":""}]}""",
            """{"Sectors":{"1":{"Value":"28.101"}}}""");
        Assert.Equal("""{"Sectors":[{"Value":""},{"Value":"28.101"},{"Value":""}]}""", result);
    }

    [Fact]
    public void Integer_keyed_object_extends_array()
    {
        var result = Merge(
            """{"Captures":[{"Path":"a"},{"Path":"b"}]}""",
            """{"Captures":{"2":{"Path":"c"}}}""");
        Assert.Equal("""{"Captures":[{"Path":"a"},{"Path":"b"},{"Path":"c"}]}""", result);
    }

    [Fact]
    public void Nested_array_element_merges_deeply()
    {
        var result = Merge(
            """{"Sectors":[{"Segments":[{"Status":0},{"Status":0}]}]}""",
            """{"Sectors":{"0":{"Segments":{"1":{"Status":2049}}}}}""");
        Assert.Equal("""{"Sectors":[{"Segments":[{"Status":0},{"Status":2049}]}]}""", result);
    }

    [Fact]
    public void Deleted_key_removes_entries()
    {
        var result = Merge(
            """{"PitTimes":{"77":{"Duration":"1"},"44":{"Duration":"2"}}}""",
            """{"PitTimes":{"_deleted":["77"]}}""");
        Assert.Equal("""{"PitTimes":{"44":{"Duration":"2"}}}""", result);
    }

    [Fact]
    public void Arrays_replace_wholesale()
    {
        var result = Merge("""{"Stints":[{"a":1},{"a":2}]}""", """{"Stints":[]}""");
        Assert.Equal("""{"Stints":[]}""", result);
    }

    [Fact]
    public void Scalar_replaces_object_and_object_replaces_scalar()
    {
        Assert.Equal("""{"a":5}""", Merge("""{"a":{"b":1}}""", """{"a":5}"""));
        Assert.Equal("""{"a":{"b":1}}""", Merge("""{"a":5}""", """{"a":{"b":1}}"""));
    }

    [Fact]
    public void Merging_into_null_builds_the_object()
    {
        var result = JsonMerge.Merge(null, JsonNode.Parse("""{"Lines":{"1":{"Position":"1"}}}"""));
        Assert.Equal("""{"Lines":{"1":{"Position":"1"}}}""", result!.ToJsonString());
    }
}
