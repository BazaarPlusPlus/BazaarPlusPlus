#nullable enable

namespace BazaarPlusPlus.Game.Screenshots;

/// <summary>Caps expensive end-of-run visual work without slowing the workflow poll.</summary>
internal sealed class EndOfRunHeavySampleCadence
{
    internal const float IntervalSeconds = 0.075f;

    private float _nextSampleAtSeconds;
    private float _lastObservedAtSeconds;
    private int _generation = int.MinValue;

    internal bool ShouldSample(float nowSeconds, int generation)
    {
        if (float.IsNaN(nowSeconds) || float.IsInfinity(nowSeconds))
            return true;
        if (_generation != generation || nowSeconds < _lastObservedAtSeconds)
        {
            _generation = generation;
            _lastObservedAtSeconds = nowSeconds;
            _nextSampleAtSeconds = nowSeconds + IntervalSeconds;
            return true;
        }
        _lastObservedAtSeconds = nowSeconds;
        if (nowSeconds < _nextSampleAtSeconds)
            return false;

        _nextSampleAtSeconds = nowSeconds + IntervalSeconds;
        return true;
    }

    internal void Reset()
    {
        _generation = int.MinValue;
        _nextSampleAtSeconds = 0f;
        _lastObservedAtSeconds = 0f;
    }
}

internal readonly record struct EndOfRunHierarchySentinelState(
    int InstanceId,
    int ParentInstanceId,
    int SiblingIndex,
    int ChildCount,
    bool IsExcluded
);

internal static class EndOfRunHierarchySentinelCore
{
    internal static bool Matches(
        EndOfRunHierarchySentinelState expected,
        EndOfRunHierarchySentinelState current
    ) => expected == current;
}

/// <summary>
/// Reuses a structural sampling plan only while its source generation and native objects remain
/// valid. Callers still read every live pose value on each sample.
/// </summary>
internal sealed class EndOfRunReusablePlanCache<TKey, TPlan>
    where TKey : notnull
{
    private readonly Dictionary<TKey, Entry> _entries = new();
    private readonly List<TKey> _pruneKeys = new();

    internal int Count => _entries.Count;

    internal bool TryGet(TKey key, int generation, out TPlan plan)
    {
        if (_entries.TryGetValue(key, out var entry) && entry.Generation == generation)
        {
            plan = entry.Plan;
            return true;
        }
        plan = default!;
        return false;
    }

    internal void Set(TKey key, int generation, TPlan plan) =>
        _entries[key] = new Entry(generation, plan);

    internal void Prune(int generation, ISet<TKey> activeKeys, Func<TPlan, bool> isAlive)
    {
        if (activeKeys == null)
            throw new ArgumentNullException(nameof(activeKeys));
        if (isAlive == null)
            throw new ArgumentNullException(nameof(isAlive));

        _pruneKeys.Clear();
        foreach (var pair in _entries)
        {
            if (
                pair.Value.Generation != generation
                || !activeKeys.Contains(pair.Key)
                || !isAlive(pair.Value.Plan)
            )
                _pruneKeys.Add(pair.Key);
        }
        foreach (var key in _pruneKeys)
            _entries.Remove(key);
    }

    internal void Clear() => _entries.Clear();

    private readonly record struct Entry(int Generation, TPlan Plan);
}
