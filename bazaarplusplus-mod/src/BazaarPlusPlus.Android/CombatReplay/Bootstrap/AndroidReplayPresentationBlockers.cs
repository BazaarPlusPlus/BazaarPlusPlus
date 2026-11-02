namespace BazaarPlusPlus.Android.CombatReplay.Bootstrap;

/// <summary>
/// What held the replay's presentation wait: the conditions blocking now and when each was last
/// seen blocking. A timeout names the first; a finished wait logs the second, so a slow device
/// shows which game state it waited on.
/// </summary>
internal sealed class AndroidReplayPresentationBlockers
{
    private readonly Dictionary<string, long> _lastBlockedMs = new();
    private IReadOnlyList<string> _current = [];

    /// <summary>Records one poll; true when nothing blocks.</summary>
    public bool Observe(IReadOnlyList<string> blockers, long elapsedMs)
    {
        _current = blockers;
        foreach (var blocker in blockers)
            _lastBlockedMs[blocker] = elapsedMs;
        return blockers.Count == 0;
    }

    public string Current => _current.Count == 0 ? "none" : string.Join(",", _current);

    /// <summary>Each condition that blocked at all, longest first, as <c>name=ms</c>.</summary>
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
