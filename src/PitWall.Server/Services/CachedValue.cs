namespace PitWall.Server.Services;

/// <summary>
/// A value refreshed at most once per <paramref name="lifetime"/>. If a refresh fails the last
/// good value is served, so a flaky upstream never blanks a dashboard panel mid-race.
/// </summary>
public sealed class CachedValue<T>(TimeSpan lifetime, Func<CancellationToken, Task<T>> load) where T : class
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private T? _value;
    private DateTimeOffset _loaded;

    public async Task<T?> GetAsync(CancellationToken cancellation)
    {
        if (_value is not null && DateTimeOffset.UtcNow - _loaded < lifetime)
        {
            return _value;
        }

        await _gate.WaitAsync(cancellation);
        try
        {
            if (_value is not null && DateTimeOffset.UtcNow - _loaded < lifetime)
            {
                return _value;
            }
            _value = await load(cancellation);
            _loaded = DateTimeOffset.UtcNow;
        }
        catch (Exception) when (_value is not null && !cancellation.IsCancellationRequested)
        {
            // Retry sooner than a full lifetime, but keep serving what we had.
            _loaded = DateTimeOffset.UtcNow - lifetime + TimeSpan.FromMinutes(1);
        }
        finally
        {
            _gate.Release();
        }
        return _value;
    }
}
