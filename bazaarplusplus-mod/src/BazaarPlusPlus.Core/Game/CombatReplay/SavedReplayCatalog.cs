#nullable enable
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;

namespace BazaarPlusPlus.Game.CombatReplay;

/// <summary>
/// The saved half of a replay both hosts' runtimes read: a battle's manifest from the catalog
/// and its Replay Payload from the payload store. A battle is saved only when both exist. Turning
/// the payload's message bytes into the game's messages is each host's loader.
/// </summary>
public sealed class SavedReplayCatalog
{
    private readonly IPvpBattleCatalog _battleCatalog;
    private readonly CombatReplayPayloadStore _payloadStore;

    public SavedReplayCatalog(
        IPvpBattleCatalog battleCatalog,
        CombatReplayPayloadStore payloadStore
    )
    {
        _battleCatalog = battleCatalog ?? throw new ArgumentNullException(nameof(battleCatalog));
        _payloadStore = payloadStore ?? throw new ArgumentNullException(nameof(payloadStore));
    }

    public bool HasSavedReplay(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return false;

        // Existence only: History probes this on every view refresh, so no manifest is built.
        return _battleCatalog.Exists(battleId) && _payloadStore.Exists(battleId);
    }

    public PvpBattleManifest? LoadBattle(string battleId)
    {
        var manifest = _battleCatalog.TryLoad(battleId);
        if (manifest == null || !_payloadStore.Exists(manifest.BattleId))
            return null;

        return manifest;
    }

    public PvpReplayPayload? LoadPayload(PvpBattleManifest manifest)
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));

        return _payloadStore.Load(manifest.BattleId);
    }

    /// <summary>
    /// Reads a saved battle's manifest and payload. The caller holds the playback payload lease,
    /// so replay maintenance cannot evict the payload mid-read.
    /// </summary>
    public SavedBattleLoadOutcome TryLoad(
        string battleId,
        out PvpBattleManifest? manifest,
        out PvpReplayPayload? payload
    )
    {
        manifest = null;
        payload = null;
        if (!HasSavedReplay(battleId))
            return SavedBattleLoadOutcome.Missing;
        manifest = LoadBattle(battleId);
        payload = manifest == null ? null : LoadPayload(manifest);
        return payload == null ? SavedBattleLoadOutcome.Missing : SavedBattleLoadOutcome.Loaded;
    }
}
