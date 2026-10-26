#nullable enable

namespace BazaarPlusPlus.GameInterop.StaticCards;

/// <summary>
/// A lookup cache bound to one static-data manager reference and one scope string (the game
/// language, or empty when the value does not vary with it). A different manager (GameData
/// reload) or scope empties it, so an entry from an earlier scope never comes back.
/// </summary>
internal sealed class StaticDataScopedCache<TKey, TValue>
    where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _values = new();
    private object? _staticData;
    private string? _scope;

    /// <summary>
    /// Returns the cached value for <paramref name="key"/> in this scope, or resolves it and keeps
    /// it when <paramref name="shouldCache"/> accepts it. Pass static lambdas and carry inputs in
    /// <paramref name="state"/> so a hit allocates nothing.
    /// </summary>
    internal TValue GetOrAdd<TState>(
        object staticData,
        string scope,
        TKey key,
        TState state,
        Func<TState, TValue> resolve,
        Func<TValue, bool> shouldCache
    )
    {
        lock (_values)
        {
            Rescope(staticData, scope);
            if (_values.TryGetValue(key, out var cached))
                return cached;
        }

        var value = resolve(state);
        if (!shouldCache(value))
            return value;

        lock (_values)
        {
            // A rescope while resolving means the value belongs to a scope that is gone.
            if (IsScope(staticData, scope))
                _values[key] = value;
        }
        return value;
    }

    private bool IsScope(object staticData, string scope) =>
        ReferenceEquals(_staticData, staticData)
        && string.Equals(_scope, scope, StringComparison.Ordinal);

    private void Rescope(object staticData, string scope)
    {
        if (IsScope(staticData, scope))
            return;
        _values.Clear();
        _staticData = staticData;
        _scope = scope;
    }
}
