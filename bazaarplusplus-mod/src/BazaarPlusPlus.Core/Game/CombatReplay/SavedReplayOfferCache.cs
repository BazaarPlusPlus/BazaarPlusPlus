#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay;

/// <summary>
/// Host-thread-only row availability cache. The host supplies monotonic realtime seconds.
/// A cached offer is only a UI hint; playback always reads the actual payload again.
/// </summary>
public sealed class SavedReplayOfferCache(Func<string, bool> probe)
{
    private const float LifetimeSeconds = 5f;
    private readonly Func<string, bool> _probe =
        probe ?? throw new ArgumentNullException(nameof(probe));
    private readonly Dictionary<string, (bool Saved, float At)> _offers = new(
        StringComparer.Ordinal
    );

    public bool CanOffer(string battleId, float now)
    {
        if (
            _offers.TryGetValue(battleId, out var cached)
            && now >= cached.At
            && now - cached.At < LifetimeSeconds
        )
            return cached.Saved;

        // Exceptions remain uncached so the host can report them and the next probe can retry.
        var saved = _probe(battleId);
        if (_offers.Count > 256)
            _offers.Clear();
        _offers[battleId] = (saved, now);
        return saved;
    }

    public void Forget(string battleId) => _offers.Remove(battleId);
}
