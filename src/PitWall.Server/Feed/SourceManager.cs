using System.Text.RegularExpressions;

namespace PitWall.Server.Feed;

/// <summary>Keeps exactly one data source (live or replay) running at a time.</summary>
public sealed partial class SourceManager(LiveTimingSource live, ReplaySource replay, FeedHub hub) : IAsyncDisposable
{
    private readonly SemaphoreSlim _switch = new(1, 1);

    public ReplaySource Replay => replay;

    /// <summary>Archive paths look like <c>2026/2026-09-06_Italian_Grand_Prix/2026-09-06_Race/</c>.</summary>
    [GeneratedRegex(@"^\d{4}/[A-Za-z0-9_\-]+/[A-Za-z0-9_\-]+/$")]
    public static partial Regex SessionPathPattern();

    public async Task UseLiveAsync()
    {
        await _switch.WaitAsync();
        try
        {
            await replay.StopAsync();
            hub.Reset();
            live.Start();
        }
        finally
        {
            _switch.Release();
        }
    }

    public async Task UseReplayAsync(string sessionPath, TimeSpan? startAt, CancellationToken cancellation)
    {
        if (!SessionPathPattern().IsMatch(sessionPath))
        {
            throw new ArgumentException("Not an archive session path.", nameof(sessionPath));
        }

        await _switch.WaitAsync(cancellation);
        try
        {
            await live.StopAsync();
            await replay.LoadAsync(sessionPath, startAt, cancellation);
        }
        finally
        {
            _switch.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await live.StopAsync();
        await replay.StopAsync();
        _switch.Dispose();
    }
}
