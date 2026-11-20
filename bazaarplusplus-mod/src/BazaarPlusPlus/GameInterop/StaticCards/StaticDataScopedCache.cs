#nullable enable

namespace BazaarPlusPlus.GameInterop.StaticCards;

internal sealed class StaticDataScopedCache<TKey, TValue>
    where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _values = new();
    private object? _staticData;
    private string? _scope;

    // Pass static lambdas and carry inputs in state so cache hits allocate nothing.
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
