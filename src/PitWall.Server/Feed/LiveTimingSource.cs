using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using PitWall.Core.Json;
using PitWall.Core.LiveTiming;
using PitWall.Server.Auth;

namespace PitWall.Server.Feed;

/// <summary>
/// Streams Formula 1's live-timing feed (SignalR Core) into the <see cref="FeedHub"/>.
/// </summary>
/// <remarks>
/// With an F1 TV token the feed also serves car positions and telemetry. If the token is
/// refused the source reconnects without it rather than going dark: timing still works.
/// </remarks>
public sealed class LiveTimingSource(FeedHub hub, TokenStore tokens, ILogger<LiveTimingSource> logger) : IAsyncDisposable
{
    public const string HubUrl = "https://livetiming.formula1.com/signalrcore";

    private CancellationTokenSource? _run;
    private Task? _loop;
    private TaskCompletionSource _restart = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _loadedPath;
    private bool _tokenRefused;

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _run = new CancellationTokenSource();
        _tokenRefused = false;
        hub.SessionChanged += OnSessionChanged;
        tokens.Changed += OnTokenChanged;
        _loop = Task.Run(() => RunAsync(_run.Token));
    }

    public async Task StopAsync()
    {
        if (_loop is null)
        {
            return;
        }

        hub.SessionChanged -= OnSessionChanged;
        tokens.Changed -= OnTokenChanged;
        await _run!.CancelAsync();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }
        _loop = null;
        _run.Dispose();
        _run = null;
    }

    private void OnSessionChanged(string? path)
    {
        // A new session mid-connection: resubscribe so it starts from a clean snapshot.
        if (_loadedPath is not null && path != _loadedPath)
        {
            logger.LogInformation("Session changed to {Path}; resubscribing", path);
            _restart.TrySetResult();
        }
    }

    private void OnTokenChanged(Core.Auth.F1TvToken? token)
    {
        _tokenRefused = false;
        _restart.TrySetResult();
    }

    private async Task RunAsync(CancellationToken cancellation)
    {
        var attempt = 0;
        while (!cancellation.IsCancellationRequested)
        {
            _restart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var token = _tokenRefused ? null : tokens.UsableToken(DateTimeOffset.UtcNow);
            PublishAuth(token is not null);

            hub.UpdateStatus(s => s with
            {
                Source = FeedSource.Live,
                Replay = null,
                Connection = attempt == 0 ? FeedConnection.Connecting : FeedConnection.Reconnecting,
                Message = token is null ? "Public timing feed" : "F1 TV timing feed",
            });

            await using var connection = Build(token);
            var closed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.Closed += error =>
            {
                closed.TrySetResult(error);
                return Task.CompletedTask;
            };
            connection.On<string, JsonNode?, string?>("feed", (topic, data, _) =>
            {
                try
                {
                    hub.Apply(topic, data);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not apply {Topic}", topic);
                }
            });

            try
            {
                await connection.StartAsync(cancellation);
                var snapshot = await connection.InvokeAsync<JsonObject>("Subscribe", Topics.Subscribed, cancellation);
                // Record the session before loading it: Load announces it, and that must not
                // look like a session change that needs yet another resubscribe.
                _loadedPath = snapshot[Topics.SessionInfo]?["Path"].AsString();
                hub.Load(snapshot);
                attempt = 0;

                hub.UpdateStatus(s => s with { Connection = FeedConnection.Connected, Message = token is null ? "Public timing feed" : "F1 TV timing feed" });
                logger.LogInformation("Live timing connected ({Mode})", token is null ? "public" : "F1 TV");

                using var clock = new PeriodicTimer(TimeSpan.FromSeconds(1));
                var tick = Tick(clock, cancellation);
                var finished = await Task.WhenAny(closed.Task, _restart.Task, tick, Task.Delay(Timeout.Infinite, cancellation));
                if (finished == closed.Task && closed.Task.Result is { } error)
                {
                    logger.LogWarning(error, "Live timing connection closed");
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                break;
            }
            catch (HttpRequestException ex) when (token is not null && ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                logger.LogWarning("F1 TV token refused by the timing feed; continuing without it");
                _tokenRefused = true;
                PublishAuth(false, "Formula 1 refused the saved F1 TV sign-in. Sign in again to restore car positions.");
                continue;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Live timing connection failed");
                hub.UpdateStatus(s => s with { Connection = FeedConnection.Error, Message = "Cannot reach the timing feed. Retrying…" });
            }

            if (cancellation.IsCancellationRequested)
            {
                break;
            }

            attempt++;
            var backoff = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(attempt, 5))));
            if (_restart.Task.IsCompleted)
            {
                backoff = TimeSpan.Zero;
                attempt = 0;
            }
            try
            {
                await Task.Delay(backoff, cancellation);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        hub.UpdateStatus(s => s with { Connection = FeedConnection.Stopped });
    }

    private async Task Tick(PeriodicTimer timer, CancellationToken cancellation)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                hub.PublishClock(DateTimeOffset.UtcNow, 1, running: true);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private HubConnection Build(string? token) =>
        new HubConnectionBuilder()
            .WithUrl(HubUrl, options =>
            {
                if (token is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                }
            })
            .Build();

    private void PublishAuth(bool usingToken, string? problem = null)
    {
        var current = tokens.Current;
        var now = DateTimeOffset.UtcNow;
        problem ??= current switch
        {
            null => null,
            { IsActive: false } => "The signed-in F1 account has no active F1 TV subscription.",
            _ when current.IsExpired(now) => "The F1 TV sign-in expired. Sign in again.",
            _ => null,
        };

        hub.UpdateStatus(s => s with
        {
            Auth = new AuthStatus
            {
                Connected = usingToken,
                Subscription = current?.SubscriptionStatus,
                Product = current?.Product,
                Expires = current?.Expires,
                Problem = problem,
            },
        });
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
