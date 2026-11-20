#nullable enable
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;

namespace BazaarPlusPlus.Game.CombatReplay;

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

    /// <summary>The caller holds the playback lease so maintenance cannot evict the Replay Payload during the read.</summary>
    public SavedBattleLoadOutcome TryLoad(
        string battleId,
        out PvpBattleManifest? manifest,
        out PvpReplayPayload? payload
    )
    {
        manifest = null;
        payload = null;
        if (string.IsNullOrWhiteSpace(battleId))
            return SavedBattleLoadOutcome.Missing;
        manifest = LoadBattle(battleId);
        payload = manifest == null ? null : LoadPayload(manifest);
        return payload == null ? SavedBattleLoadOutcome.Missing : SavedBattleLoadOutcome.Loaded;
    }
}
