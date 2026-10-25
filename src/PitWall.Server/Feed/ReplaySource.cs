using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PitWall.Core.LiveTiming;
using PitWall.Server.Archive;

namespace PitWall.Server.Feed;

/// <summary>
/// Plays an archived session through the <see cref="FeedHub"/> exactly as if it were live,
/// with seek and speed control. Used to rehearse the dashboard between race weekends.
/// </summary>
public sealed class ReplaySource(FeedHub hub, ArchiveClient archive, ILogger<ReplaySource> logger) : IAsyncDisposable
{
    /// <summary>Everything the live source subscribes to, plus nothing it cannot render.</summary>
    private static readonly string[] ReplayTopics = Topics.Subscribed;

    private readonly Lock _gate = new();
    private CancellationTokenSource? _run;
    private Task? _loop;
    private ReplayTimeline? _timeline;
    private string? _path;
    private string? _title;

    // Playback clock: offset = _anchorOffset + elapsed(_anchorWatch) * _speed while playing.
    private TimeSpan _anchorOffset;
    private readonly Stopwatch _anchorWatch = new();
    private double _speed = 1;
    private bool _playing;
    private int _next;

    public ReplayTimeline? Timeline => _timeline;

    public async Task LoadAsync(string sessionPath, TimeSpan? startAt, CancellationToken cancellation)
    {
        await StopAsync();

        _run = new CancellationTokenSource();
        var token = _run.Token;
        _path = sessionPath;

        var session = await archive.FindSessionAsync(sessionPath, cancellation);
        _title = session is null ? sessionPath : $"{session.MeetingName} · {session.Name}";

        hub.Reset();
        PublishStatus(FeedConnection.Loading, "Downloading archived session…", 0);

        var loaded = 0;
        var tasks = ReplayTopics.Select(async topic =>
        {
            var lines = await archive.GetStreamAsync(sessionPath, topic, token);
            var done = Interlocked.Increment(ref loaded);
            PublishStatus(FeedConnection.Loading, $"Downloading archived session… {done}/{ReplayTopics.Length}", done / (double)ReplayTopics.Length);
            return lines;
        });
        var all = await Task.WhenAll(tasks);

        _timeline = ReplayTimeline.Create(all.SelectMany(l => l));
        if (_timeline.Lines.Count == 0)
        {
            PublishStatus(FeedConnection.Error, archive.NeedsSignIn
                ? "Formula 1 now requires an F1 TV sign-in to download past sessions."
                : "That session has no archived timing data.", 0);
            return;
        }

        logger.LogInformation("Replay loaded: {Title}, {Count} messages, {Duration}", _title, _timeline.Lines.Count, _timeline.Duration);

        // Default to shortly before the start so the formation of the order is visible.
        var start = startAt ?? (_timeline.GreenFlag is { } green ? green - TimeSpan.FromSeconds(90) : TimeSpan.Zero);
        Seek(start < TimeSpan.Zero ? TimeSpan.Zero : start);
        Play();

        _loop = Task.Run(() => PumpAsync(token));
    }

    public void Play()
    {
        lock (_gate)
        {
            if (_playing)
            {
                return;
            }
            _anchorWatch.Restart();
            _playing = true;
        }
        PublishStatus(FeedConnection.Connected, null, 1);
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (!_playing)
            {
                return;
            }
            _anchorOffset = CurrentOffsetLocked();
            _anchorWatch.Reset();
            _playing = false;
        }
        PublishStatus(FeedConnection.Connected, null, 1);
    }

    public void SetSpeed(double speed)
    {
        lock (_gate)
        {
            _anchorOffset = CurrentOffsetLocked();
            _anchorWatch.Restart();
            _speed = Math.Clamp(speed, 0.25, 60);
        }
        PublishStatus(FeedConnection.Connected, null, 1);
    }

    /// <summary>Rebuilds state up to <paramref name="offset"/> in one pass, then resumes from there.</summary>
    public void Seek(TimeSpan offset)
    {
        var timeline = _timeline;
        if (timeline is null)
        {
            return;
        }

        lock (_gate)
        {
            offset = offset < TimeSpan.Zero ? TimeSpan.Zero : offset > timeline.Duration ? timeline.Duration : offset;
            var index = timeline.IndexAt(offset);
            hub.Rebuild(LinesForSeek(timeline, index));
            _next = index;
            _anchorOffset = offset;
            if (_playing)
            {
                _anchorWatch.Restart();
            }
        }
        PublishStatus(FeedConnection.Connected, null, 1);
    }

    public async Task StopAsync()
    {
        if (_run is not null)
        {
            await _run.CancelAsync();
        }
        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
            }
        }
        _loop = null;
        _run?.Dispose();
        _run = null;
        _timeline = null;
        _playing = false;
        _anchorWatch.Reset();
        _anchorOffset = TimeSpan.Zero;
        _speed = 1;
        _next = 0;
    }

    private async Task PumpAsync(CancellationToken cancellation)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        var lastClock = Stopwatch.StartNew();
        var lastStatus = Stopwatch.StartNew();

        try
        {
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                var timeline = _timeline!;
                List<StreamLine>? due = null;
                TimeSpan offset;
                bool playing;
                double speed;

                lock (_gate)
                {
                    offset = CurrentOffsetLocked();
                    playing = _playing;
                    speed = _speed;
                    while (_next < timeline.Lines.Count && timeline.Lines[_next].Offset <= offset)
                    {
                        (due ??= []).Add(timeline.Lines[_next++]);
                    }

                    // Applied under the replay lock so a concurrent seek cannot interleave stale lines.
                    if (due is not null)
                    {
                        foreach (var line in due)
                        {
                            try
                            {
                                hub.Apply(line.Topic, line.Json);
                            }
                            catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException or InvalidDataException)
                            {
                                logger.LogDebug(ex, "Skipping unreadable {Topic} line", line.Topic);
                            }
                        }
                    }

                    if (_next >= timeline.Lines.Count && _playing)
                    {
                        _anchorOffset = timeline.Duration;
                        _anchorWatch.Reset();
                        _playing = false;
                    }
                }

                if (lastClock.ElapsedMilliseconds >= 1000 && timeline.UtcAt(offset) is { } utc)
                {
                    hub.PublishClock(utc, speed, playing);
                    lastClock.Restart();
                }

                if (lastStatus.ElapsedMilliseconds >= 1000)
                {
                    PublishStatus(FeedConnection.Connected, null, 1);
                    lastStatus.Restart();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Lines needed to rebuild state at <paramref name="index"/>. Car data and positions only
    /// matter as their latest sample, and skipping the rest saves thousands of decompressions.
    /// </summary>
    private static IEnumerable<StreamLine> LinesForSeek(ReplayTimeline timeline, int index)
    {
        var lastCompressed = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = index - 1; i >= 0 && lastCompressed.Count < 2; i--)
        {
            var topic = timeline.Lines[i].Topic;
            if (Topics.IsCompressed(topic))
            {
                lastCompressed.TryAdd(topic, i);
            }
        }

        for (var i = 0; i < index; i++)
        {
            var line = timeline.Lines[i];
            if (!Topics.IsCompressed(line.Topic) || lastCompressed.GetValueOrDefault(line.Topic, -1) == i)
            {
                yield return line;
            }
        }
    }

    private TimeSpan CurrentOffsetLocked() =>
        _playing ? _anchorOffset + TimeSpan.FromTicks((long)(_anchorWatch.Elapsed.Ticks * _speed)) : _anchorOffset;

    private void PublishStatus(FeedConnection connection, string? message, double progress)
    {
        TimeSpan offset;
        bool playing;
        double speed;
        lock (_gate)
        {
            offset = CurrentOffsetLocked();
            playing = _playing;
            speed = _speed;
        }

        var timeline = _timeline;
        hub.UpdateStatus(s => s with
        {
            Source = FeedSource.Replay,
            Connection = connection,
            Message = message ?? "Replaying an archived session",
            Replay = new ReplayStatus
            {
                Path = _path ?? "",
                Title = _title,
                Offset = Math.Round(offset.TotalSeconds, 1),
                Duration = Math.Round(timeline?.Duration.TotalSeconds ?? 0, 1),
                GreenFlag = timeline?.GreenFlag?.TotalSeconds,
                Speed = speed,
                Playing = playing,
                LoadProgress = progress,
            },
        });
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
