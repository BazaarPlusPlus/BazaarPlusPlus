#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class ReplayVideoReadbackLimiter
{
    private readonly int _limit;
    private int _outstanding;
    private int _maxObserved;

    internal ReplayVideoReadbackLimiter(int limit)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit));
        _limit = limit;
    }

    internal int Outstanding => Volatile.Read(ref _outstanding);
    internal int MaxObserved => Volatile.Read(ref _maxObserved);

    internal bool TryReserve()
    {
        while (true)
        {
            var current = Volatile.Read(ref _outstanding);
            if (current >= _limit)
                return false;
            if (Interlocked.CompareExchange(ref _outstanding, current + 1, current) != current)
                continue;

            ObserveMaximum(current + 1);
            return true;
        }
    }

    internal void Release()
    {
        var remaining = Interlocked.Decrement(ref _outstanding);
        if (remaining < 0)
            Interlocked.Exchange(ref _outstanding, 0);
    }

    private void ObserveMaximum(int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref _maxObserved);
            if (current >= value)
                return;
            if (Interlocked.CompareExchange(ref _maxObserved, value, current) == current)
                return;
        }
    }
}
