#nullable enable
namespace BazaarPlusPlus.Infrastructure.Logging;

public sealed class OperationalHealthTracker<TKey, TReason>
    where TKey : notnull
{
    private readonly Dictionary<TKey, TReason> _failures = new();

    public bool ObserveFailure(TKey key, TReason reason)
    {
        if (_failures.ContainsKey(key))
            return false;
        _failures.Add(key, reason);
        return true;
    }

    public bool ObserveSuccess(TKey key, out TReason reason)
    {
        if (!_failures.TryGetValue(key, out reason!))
            return false;
        _failures.Remove(key);
        return true;
    }

    public void Reset() => _failures.Clear();
}
