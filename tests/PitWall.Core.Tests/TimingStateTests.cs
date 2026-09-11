using System.Text.Json.Nodes;
using PitWall.Core.LiveTiming;

namespace PitWall.Core.Tests;

public class TimingStateTests
{
    [Fact]
    public void Patch_is_forwarded_verbatim_and_merged()
    {
        var state = new TimingState();
        state.Apply(Topics.TimingData, """{"Lines":{"44":{"Position":"3","InPit":false}}}""");
        var changes = state.Apply(Topics.TimingData, """{"Lines":{"44":{"Position":"2"}}}""");

        var patch = Assert.IsType<PatchChange>(Assert.Single(changes));
        Assert.Equal("""{"Lines":{"44":{"Position":"2"}}}""", patch.Json);
        Assert.Equal("2", state.Topic(Topics.TimingData)!["Lines"]!["44"]!["Position"]!.GetValue<string>());
        Assert.False(state.Topic(Topics.TimingData)!["Lines"]!["44"]!["InPit"]!.GetValue<bool>());
    }

    [Fact]
    public void Compressed_car_data_becomes_compact_telemetry()
    {
        var body = Compression.Deflate(
            """{"Entries":[{"Utc":"2026-09-06T13:34:59Z","Cars":{"44":{"Channels":{"0":11200,"2":312,"3":8,"4":100,"5":0}}}}]}""");
        var state = new TimingState();

        var change = Assert.IsType<TelemetryChange>(Assert.Single(state.Apply(Topics.CarData, $"\"{body}\"")));

        Assert.Contains("\"44\":[11200,312,8,100,0]", change.Json);
        Assert.Contains("2026-09-06T13:34:59", change.Json);
    }

    [Fact]
    public void Compressed_positions_keep_the_latest_sample()
    {
        var body = Compression.Deflate("""
            {"Position":[
              {"Timestamp":"2026-09-06T13:00:00Z","Entries":{"1":{"Status":"OnTrack","X":10,"Y":20,"Z":0}}},
              {"Timestamp":"2026-09-06T13:00:01Z","Entries":{"1":{"Status":"OffTrack","X":11,"Y":21,"Z":0}}}
            ]}
            """);
        var state = new TimingState();

        var change = Assert.IsType<PositionsChange>(Assert.Single(state.Apply(Topics.Position, body)));

        Assert.Contains("\"1\":[11,21,0]", change.Json);
    }

    [Fact]
    public void New_last_lap_time_records_a_lap_with_the_current_tyre()
    {
        var state = new TimingState();
        state.Apply(Topics.TimingAppData, """{"Lines":{"16":{"Stints":[{"Compound":"MEDIUM","TotalLaps":4}]}}}""");
        state.Apply(Topics.TimingData, """{"Lines":{"16":{"NumberOfLaps":4,"LastLapTime":{"Value":"1:21.456"}}}}""");

        var changes = state.Apply(Topics.TimingData, """{"Lines":{"16":{"NumberOfLaps":5,"LastLapTime":{"Value":"1:21.300"}}}}""");

        var lap = Assert.IsType<LapChange>(changes[1]);
        Assert.Equal("""{"n":"16","lap":{"l":5,"t":81300,"c":"MEDIUM","a":4,"f":0}}""", lap.Json);
        Assert.Equal(2, state.Laps.All["16"].Count);
    }

    [Fact]
    public void Repeated_patch_without_new_time_records_nothing()
    {
        var state = new TimingState();
        state.Apply(Topics.TimingData, """{"Lines":{"16":{"NumberOfLaps":4,"LastLapTime":{"Value":"1:21.456"}}}}""");

        var changes = state.Apply(Topics.TimingData, """{"Lines":{"16":{"Position":"3"}}}""");

        Assert.Single(changes);
    }

    [Fact]
    public void Load_replaces_state_from_a_subscribe_response()
    {
        var state = new TimingState();
        state.Apply(Topics.LapCount, """{"CurrentLap":40}""");

        state.Load(JsonNode.Parse("""{"LapCount":{"CurrentLap":1,"TotalLaps":53},"SessionInfo":{"Path":"2026/x/"}}""")!.AsObject());

        Assert.Equal(1, state.Topic(Topics.LapCount)!["CurrentLap"]!.GetValue<int>());
        Assert.Equal("2026/x/", state.SessionPath);
    }

    [Fact]
    public void Session_change_is_reported_when_the_path_moves()
    {
        var state = new TimingState();
        state.Apply(Topics.SessionInfo, """{"Path":"2026/a/"}""");

        var changes = state.Apply(Topics.SessionInfo, """{"Path":"2026/b/"}""");

        Assert.Contains(changes, c => c is SessionChange { SessionPath: "2026/b/" });
    }

    [Theory]
    [InlineData("1:21.456", 81456)]
    [InlineData("81.456", 81456)]
    [InlineData("+0.695", 695)]
    [InlineData("59.9", 59900)]
    public void Lap_times_parse(string text, int expected)
    {
        Assert.True(LapTime.TryParseMs(text, out var ms));
        Assert.Equal(expected, ms);
    }

    [Theory]
    [InlineData("")]
    [InlineData("LAP 1")]
    [InlineData("1L")]
    public void Non_times_do_not_parse(string text) => Assert.False(LapTime.TryParseMs(text, out _));
}
