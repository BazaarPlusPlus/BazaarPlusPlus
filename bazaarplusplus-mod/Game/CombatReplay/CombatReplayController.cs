#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using TheBazaar;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayController
{
    private readonly IPvpBattleCatalog _battleCatalog;
    private readonly CombatReplayPayloadStore _payloadStore;
    private readonly CombatReplayLoader _loader;

    public CombatReplayController(
        IPvpBattleCatalog battleCatalog,
        CombatReplayPayloadStore payloadStore,
        CombatReplayLoader loader
    )
    {
        _battleCatalog = battleCatalog ?? throw new ArgumentNullException(nameof(battleCatalog));
        _payloadStore = payloadStore ?? throw new ArgumentNullException(nameof(payloadStore));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    public string? ActiveBattleId { get; private set; }

    public IReadOnlyList<PvpBattleManifest> ListRecentBattles()
    {
        return _battleCatalog
            .ListRecentBattles(50)
            .Where(manifest => _payloadStore.Exists(manifest.BattleId))
            .ToList();
    }

    public PvpBattleManifest? GetLatestBattle()
    {
        return _battleCatalog
            .ListRecentBattles(50)
            .FirstOrDefault(manifest => _payloadStore.Exists(manifest.BattleId));
    }

    public bool HasSavedReplay(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return false;

        var manifest = _battleCatalog.TryLoad(battleId);
        return manifest != null && _payloadStore.Exists(manifest.BattleId);
    }

    public PvpBattleManifest? LoadBattle(string battleId)
    {
        var manifest = _battleCatalog.TryLoad(battleId);
        if (manifest == null || !_payloadStore.Exists(manifest.BattleId))
            return null;

        ActiveBattleId = manifest.BattleId;
        return manifest;
    }

    public PvpReplayPayload? LoadPayload(PvpBattleManifest manifest)
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));

        return _payloadStore.Load(manifest.BattleId);
    }

    public CombatSequenceMessages LoadReplay(PvpReplayPayload payload)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        return _loader.Load(payload);
    }
}
