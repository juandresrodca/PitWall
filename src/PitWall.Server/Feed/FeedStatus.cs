using System.Text.Json.Serialization;

namespace PitWall.Server.Feed;

public enum FeedSource { Idle, Live, Replay }

public enum FeedConnection { Stopped, Connecting, Connected, Reconnecting, Loading, Error }

/// <summary>What the dashboard shows in its status strip. Serialised to the browser as-is.</summary>
public sealed record FeedStatus
{
    [JsonConverter(typeof(JsonStringEnumConverter<FeedSource>))]
    public FeedSource Source { get; init; } = FeedSource.Idle;

    [JsonConverter(typeof(JsonStringEnumConverter<FeedConnection>))]
    public FeedConnection Connection { get; init; } = FeedConnection.Stopped;

    public string? Message { get; init; }

    /// <summary>True once car positions or telemetry have arrived (they need F1 TV).</summary>
    public bool SubscriberData { get; init; }

    public string? SessionPath { get; init; }

    public ReplayStatus? Replay { get; init; }

    public AuthStatus Auth { get; init; } = new();
}

public sealed record ReplayStatus
{
    public required string Path { get; init; }
    public string? Title { get; init; }
    public double Offset { get; init; }
    public double Duration { get; init; }
    public double? GreenFlag { get; init; }
    public double Speed { get; init; } = 1;
    public bool Playing { get; init; }
    public double LoadProgress { get; init; }
}

public sealed record AuthStatus
{
    public bool Connected { get; init; }
    public string? Subscription { get; init; }
    public string? Product { get; init; }
    public DateTimeOffset? Expires { get; init; }
    public string? Problem { get; init; }
}
