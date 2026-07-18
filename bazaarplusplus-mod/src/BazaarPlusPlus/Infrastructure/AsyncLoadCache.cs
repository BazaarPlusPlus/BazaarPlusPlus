#nullable enable
namespace BazaarPlusPlus.Infrastructure;

/// <summary>
/// Main-thread-only cache for asynchronous reference-type loads. Cached null values are retained
/// as negative results; callers decide whether each completed load is cacheable.
/// </summary>
/// <remarks>
/// This type is deliberately unsynchronized and must not be used across threads. It performs no
/// logging and has no Unity or BepInEx dependencies; callers own environment-specific policy.
/// </remarks>
internal sealed class AsyncLoadCache<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly Dictionary<TKey, TValue?> _cachedValues = new();
    private readonly Dictionary<TKey, Task<TValue?>> _inFlightLoads = new();
    private readonly Func<TKey, Task<AsyncLoadResult<TValue>>> _loader;

    internal AsyncLoadCache(Func<TKey, Task<AsyncLoadResult<TValue>>> loader)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    internal bool TryGetCached(TKey key, out TValue? value) =>
        _cachedValues.TryGetValue(key, out value);

    internal Task<TValue?> GetOrLoadAsync(TKey key)
    {
        if (_cachedValues.TryGetValue(key, out var cached))
            return Task.FromResult(cached);

        if (_inFlightLoads.TryGetValue(key, out var inFlight))
            return inFlight;

        var completion = new TaskCompletionSource<TValue?>();
        _inFlightLoads[key] = completion.Task;
        _ = CompleteLoadAsync(key, completion);
        return completion.Task;
    }

    private async Task CompleteLoadAsync(TKey key, TaskCompletionSource<TValue?> completion)
    {
        AsyncLoadResult<TValue> result = default;
        Exception? error = null;

        try
        {
            result = await _loader(key);
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            _inFlightLoads.Remove(key);
            if (error == null && result.ShouldCache)
                _cachedValues[key] = result.Value;
        }

        if (error == null)
            completion.SetResult(result.Value);
        else
            completion.SetException(error);
    }
}

internal readonly struct AsyncLoadResult<TValue>
    where TValue : class
{
    internal AsyncLoadResult(TValue? value, bool shouldCache)
    {
        Value = value;
        ShouldCache = shouldCache;
    }

    internal TValue? Value { get; }

    internal bool ShouldCache { get; }
}
