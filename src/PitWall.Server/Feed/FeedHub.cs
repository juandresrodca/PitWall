using System.Net.ServerSentEvents;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using PitWall.Core.LiveTiming;

namespace PitWall.Server.Feed;

/// <summary>
/// Single owner of the timing state and of every connected dashboard.
/// </summary>
/// <remarks>
/// Applying a message and fanning it out happen under one lock, so each browser sees deltas
/// in exactly the order the state absorbed them, and a new subscriber's snapshot can never
/// miss or double-count a delta. A browser that stops reading is disconnected rather than
/// buffered forever; EventSource reconnects on its own and starts again from a fresh snapshot.
/// </remarks>
public sealed class FeedHub
{
    private const int SubscriberBuffer = 8192;

    private static readonly JsonSerializerOptions StatusJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Lock _gate = new();
    private readonly TimingState _state = new();
    private readonly List<Channel<SseItem<string>>> _subscribers = [];
    private FeedStatus _status = new();
    private string? _announcedPath;

    /// <summary>Raised (outside the lock) when the feed reports a different session path.</summary>
    public event Action<string?>? SessionChanged;

    public FeedStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public ChannelReader<SseItem<string>> Subscribe(CancellationToken cancellation)
    {
        var channel = Channel.CreateBounded<SseItem<string>>(new BoundedChannelOptions(SubscriberBuffer)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        lock (_gate)
        {
            channel.Writer.TryWrite(new SseItem<string>(JsonSerializer.Serialize(_status, StatusJson), "status"));
            channel.Writer.TryWrite(new SseItem<string>(_state.SnapshotJson(), "snapshot"));
            _subscribers.Add(channel);
        }

        cancellation.Register(() =>
        {
            lock (_gate)
            {
                _subscribers.Remove(channel);
            }
            channel.Writer.TryComplete();
        });

        return channel.Reader;
    }

    public void Apply(string topic, JsonNode? data) => Run(state => state.Apply(topic, data));

    public void Apply(string topic, string json) => Run(state => state.Apply(topic, json));

    /// <summary>
    /// Replaces the state with the result of applying <paramref name="lines"/> from scratch,
    /// then sends one snapshot instead of thousands of deltas (replay seeking).
    /// </summary>
    public void Rebuild(IEnumerable<StreamLine> lines)
    {
        lock (_gate)
        {
            _state.Reset();
            foreach (var line in lines)
            {
                SafeApply(_state, line);
            }
            _status = _status with { SessionPath = _state.SessionPath, SubscriberData = _state.HasSubscriberData };
            SendLocked(new SseItem<string>(JsonSerializer.Serialize(_status, StatusJson), "status"));
            SendSnapshotLocked();
        }
        AnnounceSessionIfChanged();
    }

    /// <summary>Replaces everything with a Subscribe response and re-sends snapshots.</summary>
    public void Load(JsonObject subscribeResponse)
    {
        lock (_gate)
        {
            _state.Load(subscribeResponse);
            _status = _status with { SessionPath = _state.SessionPath, SubscriberData = _state.HasSubscriberData };
            SendLocked(new SseItem<string>(JsonSerializer.Serialize(_status, StatusJson), "status"));
            SendSnapshotLocked();
        }
        AnnounceSessionIfChanged();
    }

    public void Reset()
    {
        lock (_gate)
        {
            _state.Reset();
            _announcedPath = null;
            _status = _status with { SessionPath = null, SubscriberData = false };
            SendLocked(new SseItem<string>(JsonSerializer.Serialize(_status, StatusJson), "status"));
            SendSnapshotLocked();
        }
    }

    public void UpdateStatus(Func<FeedStatus, FeedStatus> update)
    {
        lock (_gate)
        {
            _status = update(_status);
            SendLocked(new SseItem<string>(JsonSerializer.Serialize(_status, StatusJson), "status"));
        }
    }

    /// <summary>Session wall-clock time, so browsers can run countdowns in live and replay alike.</summary>
    public void PublishClock(DateTimeOffset sessionUtc, double speed, bool running)
    {
        var json = $$"""{"utc":"{{sessionUtc.UtcDateTime:O}}","speed":{{speed.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"running":{{(running ? "true" : "false")}}}""";
        lock (_gate)
        {
            SendLocked(new SseItem<string>(json, "clock"));
        }
    }

    /// <summary>Reads timing state under the lock (for services such as track maps).</summary>
    public T Read<T>(Func<TimingState, T> read)
    {
        lock (_gate)
        {
            return read(_state);
        }
    }

    private void Run(Func<TimingState, IReadOnlyList<StateChange>> apply)
    {
        lock (_gate)
        {
            var changes = apply(_state);
            foreach (var change in changes)
            {
                switch (change)
                {
                    case PatchChange p:
                        SendLocked(new SseItem<string>($$"""{"t":"{{p.Topic}}","d":{{p.Json}}}""", "patch"));
                        break;
                    case PositionsChange p:
                        SendLocked(new SseItem<string>(p.Json, "pos"));
                        break;
                    case TelemetryChange t:
                        SendLocked(new SseItem<string>(t.Json, "tel"));
                        break;
                    case LapChange l:
                        SendLocked(new SseItem<string>(l.Json, "lap"));
                        break;
                }
            }

            if (_status.SubscriberData != _state.HasSubscriberData || _status.SessionPath != _state.SessionPath)
            {
                _status = _status with { SubscriberData = _state.HasSubscriberData, SessionPath = _state.SessionPath };
                SendLocked(new SseItem<string>(JsonSerializer.Serialize(_status, StatusJson), "status"));
            }
        }

        AnnounceSessionIfChanged();
    }

    private void AnnounceSessionIfChanged()
    {
        string? path;
        lock (_gate)
        {
            path = _state.SessionPath;
            if (path is null || path == _announcedPath)
            {
                return;
            }
            _announcedPath = path;
        }
        SessionChanged?.Invoke(path);
    }

    private static IReadOnlyList<StateChange> SafeApply(TimingState state, StreamLine line)
    {
        try
        {
            return state.Apply(line.Topic, line.Json);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidDataException)
        {
            // One corrupt archive line should not end a replay.
            return [];
        }
    }

    private void SendSnapshotLocked() => SendLocked(new SseItem<string>(_state.SnapshotJson(), "snapshot"));

    private void SendLocked(SseItem<string> item)
    {
        for (var i = _subscribers.Count - 1; i >= 0; i--)
        {
            var channel = _subscribers[i];
            if (!channel.Writer.TryWrite(item))
            {
                _subscribers.RemoveAt(i);
                channel.Writer.TryComplete();
            }
        }
    }
}
