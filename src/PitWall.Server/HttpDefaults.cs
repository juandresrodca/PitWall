namespace PitWall.Server;

/// <summary>Shared HTTP identity for every request PitWall makes.</summary>
public static class HttpDefaults
{
    /// <summary>
    /// Formula 1's CDN refuses requests that carry no product identity (the SignalR client's
    /// default headers among them), so every PitWall client names itself honestly.
    /// </summary>
    public const string UserAgent = "PitWall/0.2 (+https://github.com/juandresrodca/PitWall)";
}
