using System.Text;
using PitWall.Core.Auth;
using PitWall.Core.LiveTiming;
using PitWall.Core.Track;

namespace PitWall.Core.Tests;

public class ParsingTests
{
    [Fact]
    public void Stream_lines_parse_offset_and_body_ignoring_bom()
    {
        var text = "﻿00:00:07.403{\"CurrentLap\":1}\r\n00:58:24.422{\"CurrentLap\":2}\r\n\r\ngarbage\n";
        var lines = JsonStreamParser.Parse(Topics.LapCount, new StringReader(text)).ToList();

        Assert.Equal(2, lines.Count);
        Assert.Equal(new TimeSpan(0, 0, 0, 7, 403), lines[0].Offset);
        Assert.Equal("{\"CurrentLap\":1}", lines[0].Json);
        Assert.Equal(new TimeSpan(0, 0, 58, 24, 422), lines[1].Offset);
    }

    [Fact]
    public void Timeline_interleaves_topics_and_finds_green_flag_and_wall_clock()
    {
        var timeline = ReplayTimeline.Create(
        [
            new StreamLine(TimeSpan.FromSeconds(30), Topics.SessionStatus, """{"Status":"Started"}"""),
            new StreamLine(TimeSpan.FromSeconds(10), Topics.Heartbeat, """{"Utc":"2026-09-06T13:00:10Z"}"""),
            new StreamLine(TimeSpan.FromSeconds(20), Topics.LapCount, """{"CurrentLap":1}"""),
        ]);

        Assert.Equal([Topics.Heartbeat, Topics.LapCount, Topics.SessionStatus], timeline.Lines.Select(l => l.Topic));
        Assert.Equal(TimeSpan.FromSeconds(30), timeline.GreenFlag);
        Assert.Equal(DateTimeOffset.Parse("2026-09-06T13:00:00Z"), timeline.UtcAtZero);
        Assert.Equal(1, timeline.IndexAt(TimeSpan.FromSeconds(15)));
        Assert.Equal(3, timeline.IndexAt(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Token_parses_from_login_session_cookie()
    {
        var payload = Base64Url("""{"SubscriptionStatus":"active","SubscribedProduct":"F1 TV Pro","exp":1893456000,"iat":1893000000}""");
        var jwt = $"{Base64Url("""{"alg":"none"}""")}.{payload}.sig";
        var cookie = Uri.EscapeDataString($$$"""{"data":{"subscriptionStatus":"active","subscriptionToken":"{{{jwt}}}"}}""");

        var token = F1TvToken.TryParse(cookie);

        Assert.NotNull(token);
        Assert.True(token.IsActive);
        Assert.Equal("F1 TV Pro", token.Product);
        Assert.Equal(jwt, token.SubscriptionToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1893456000), token.Expires);
        Assert.DoesNotContain(jwt, token.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a token")]
    [InlineData("%7B%22data%22%3A%7B%7D%7D")]
    public void Token_rejects_junk(string input) => Assert.Null(F1TvToken.TryParse(input));

    [Fact]
    public void Outline_is_recovered_from_a_lapping_car()
    {
        // Three laps of an ellipse, sampled unevenly, starting mid-lap.
        var trace = new List<Point>();
        for (var i = 0; i < 900; i++)
        {
            var angle = 0.7 + i * (2 * Math.PI / 300) + Math.Sin(i * 0.37) * 0.004;
            trace.Add(new Point(4000 * Math.Cos(angle), 2500 * Math.Sin(angle)));
        }

        var outline = TrackOutlineBuilder.Build([trace]);

        Assert.NotNull(outline);
        Assert.Equal(600, outline.Points.Count);
        // Ellipse perimeter (Ramanujan) for a=4000, b=2500.
        var h = Math.Pow(4000 - 2500, 2) / Math.Pow(4000 + 2500, 2);
        var perimeter = Math.PI * (4000 + 2500) * (1 + 3 * h / (10 + Math.Sqrt(4 - 3 * h)));
        Assert.InRange(outline.Length, perimeter * 0.97, perimeter * 1.03);
        Assert.All(outline.Points, p =>
            Assert.InRange(p.X * p.X / (4000.0 * 4000) + p.Y * p.Y / (2500.0 * 2500), 0.95, 1.05));
    }

    [Fact]
    public void Outline_needs_enough_data() => Assert.Null(TrackOutlineBuilder.Build([[new Point(0, 0), new Point(1, 1)]]));

    private static string Base64Url(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
