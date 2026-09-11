namespace PitWall.Core.LiveTiming;

/// <summary>Live-timing topic names, spelled exactly as the feed spells them.</summary>
public static class Topics
{
    public const string Heartbeat = "Heartbeat";
    public const string CarData = "CarData.z";
    public const string Position = "Position.z";
    public const string ExtrapolatedClock = "ExtrapolatedClock";
    public const string TimingStats = "TimingStats";
    public const string TimingAppData = "TimingAppData";
    public const string WeatherData = "WeatherData";
    public const string TrackStatus = "TrackStatus";
    public const string SessionStatus = "SessionStatus";
    public const string DriverList = "DriverList";
    public const string RaceControlMessages = "RaceControlMessages";
    public const string SessionInfo = "SessionInfo";
    public const string SessionData = "SessionData";
    public const string LapCount = "LapCount";
    public const string TimingData = "TimingData";
    public const string TeamRadio = "TeamRadio";
    public const string ChampionshipPrediction = "ChampionshipPrediction";
    public const string PitLaneTimeCollection = "PitLaneTimeCollection";
    public const string PitStopSeries = "PitStopSeries";
    public const string TopThree = "TopThree";
    public const string DriverRaceInfo = "DriverRaceInfo";

    /// <summary>Everything the dashboard renders. Some are only served to F1 TV subscribers.</summary>
    public static readonly string[] Subscribed =
    [
        Heartbeat, CarData, Position, ExtrapolatedClock, TimingStats, TimingAppData,
        WeatherData, TrackStatus, SessionStatus, DriverList, RaceControlMessages,
        SessionInfo, SessionData, LapCount, TimingData, TeamRadio, ChampionshipPrediction,
        PitLaneTimeCollection, PitStopSeries, TopThree, DriverRaceInfo,
    ];

    /// <summary>Topics the public feed withholds unless an F1 TV subscription token is sent.</summary>
    public static readonly string[] SubscriberOnly =
    [
        CarData, Position, ChampionshipPrediction, PitLaneTimeCollection, PitStopSeries,
    ];

    public static bool IsCompressed(string topic) => topic.EndsWith(".z", StringComparison.Ordinal);
}
