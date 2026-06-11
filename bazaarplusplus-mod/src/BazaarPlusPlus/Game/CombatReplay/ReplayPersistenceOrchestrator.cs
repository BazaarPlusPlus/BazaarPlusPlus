#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Storage.Upload;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class ReplayPersistenceOrchestrator : IDisposable
{
    private readonly IBppServices _services;
    private readonly IPvpBattleCatalog _battleCatalog;
    private readonly CombatReplayPayloadStore _payloadStore;
    private readonly BattleReplaySyncStateStore? _syncStateStore;
    private readonly CombatReplayPersistenceQueue _persistenceQueue;
    private bool _disposed;

    public ReplayPersistenceOrchestrator(IBppServices services, IPvpBattleCatalog battleCatalog)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _battleCatalog = battleCatalog ?? throw new ArgumentNullException(nameof(battleCatalog));

        var combatReplayDirectoryPath =
            services.Paths.CombatReplayDirectoryPath
            ?? throw new InvalidOperationException(
                "Combat replay directory path is not initialized."
            );

        _payloadStore = new CombatReplayPayloadStore(combatReplayDirectoryPath);
        _syncStateStore = new BattleReplaySyncStateStore(services.Paths);
        _persistenceQueue = new CombatReplayPersistenceQueue(
            _payloadStore.Save,
            _battleCatalog.Save,
            _payloadStore.Delete
        );

        CleanupOrphanedPayloads();
    }

    public IPvpBattleCatalog Catalog => _battleCatalog;
    public CombatReplayPayloadStore PayloadStore => _payloadStore;
    public bool HasPendingPersistence => _persistenceQueue.HasPendingPersistence;

    public void Enqueue(PvpReplayPayload payload, PvpBattleManifest manifest)
    {
        if (_disposed)
            return;

        _persistenceQueue.Enqueue(payload, manifest);
    }

    public void DrainPendingResults()
    {
        var processedAny = false;
        while (_persistenceQueue.TryDequeueResult(out var result))
        {
            processedAny = true;
            if (!result.Succeeded)
            {
                BppLog.Error(
                    "ReplayPersistenceOrchestrator",
                    $"Failed to persist combat replay {result.Manifest.BattleId}: {result.Error}"
                );
                continue;
            }

            _services.EventBus.Publish(new PvpBattleRecorded { Manifest = result.Manifest });
            _syncStateStore?.MarkReplayDirty(result.Manifest.BattleId);
            BppLog.Info(
                "ReplayPersistenceOrchestrator",
                $"Saved combat replay {result.Manifest.BattleId} for run={result.Manifest.RunId ?? "unknown"}"
            );
        }

        if (processedAny && !_persistenceQueue.HasPendingPersistence)
        {
            _services.EventBus.Publish(new CombatReplayPersistenceDrained());
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _persistenceQueue.Dispose();
        DrainPendingResults();
    }

    private void CleanupOrphanedPayloads()
    {
        try
        {
            foreach (var battleId in _payloadStore.ListBattleIds())
            {
                if (_battleCatalog.TryLoad(battleId) != null)
                    continue;

                try
                {
                    _payloadStore.Delete(battleId);
                }
                catch (Exception ex)
                {
                    BppLog.Warn(
                        "ReplayPersistenceOrchestrator",
                        $"Failed to delete orphaned combat replay payload {battleId}: {ex.Message}"
                    );
                }
            }
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "ReplayPersistenceOrchestrator",
                $"Failed to scan combat replay payloads for orphan cleanup: {ex.Message}"
            );
        }
    }
}
