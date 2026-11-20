namespace BazaarPlusPlus.Android.CombatReplay.Bootstrap;

internal sealed class AndroidReplayPresentationBlockers
{
    private readonly Dictionary<string, long> _lastBlockedMs = new();
    private IReadOnlyList<string> _current = [];

    public bool Observe(IReadOnlyList<string> blockers, long elapsedMs)
    {
        _current = blockers;
        foreach (var blocker in blockers)
            _lastBlockedMs[blocker] = elapsedMs;
        return blockers.Count == 0;
    }

    public string Current => _current.Count == 0 ? "none" : string.Join(",", _current);

    public string LastBlocked =>
        _lastBlockedMs.Count == 0
            ? "none"
            : string.Join(
                ",",
                _lastBlockedMs
                    .OrderByDescending(entry => entry.Value)
                    .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => $"{entry.Key}={entry.Value}")
            );
}
