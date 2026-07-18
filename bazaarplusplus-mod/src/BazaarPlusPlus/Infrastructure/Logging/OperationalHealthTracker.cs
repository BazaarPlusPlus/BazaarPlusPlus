#nullable enable
namespace BazaarPlusPlus.Infrastructure.Logging;

internal sealed class OperationalHealthTracker<TKey, TReason>
    where TKey : notnull
{
    private readonly Dictionary<TKey, TReason> _failures = new();

    internal bool ObserveFailure(TKey key, TReason reason)
    {
        if (_failures.ContainsKey(key))
            return false;
        _failures.Add(key, reason);
        return true;
    }

    internal bool ObserveSuccess(TKey key, out TReason reason)
    {
        if (!_failures.TryGetValue(key, out reason!))
            return false;
        _failures.Remove(key);
        return true;
    }

    internal void Reset() => _failures.Clear();
}
