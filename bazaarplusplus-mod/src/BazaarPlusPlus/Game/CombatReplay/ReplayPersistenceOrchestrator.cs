#nullable enable
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
    private readonly Action<PvpBattleManifest, bool, Exception?>? _resultObserver;
    private readonly object _drainGate = new();
    private bool _disposed;

    public ReplayPersistenceOrchestrator(
        IBppServices services,
        IPvpBattleCatalog battleCatalog,
        Action<PvpBattleManifest, bool, Exception?>? resultObserver = null
    )
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _battleCatalog = battleCatalog ?? throw new ArgumentNullException(nameof(battleCatalog));
        _resultObserver = resultObserver;

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
        _persistenceQueue.SetLateResultsAvailableCallback(DrainLateShutdownResults);

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
        DrainPendingResults(publishSideEffects: true);
    }

    private void DrainLateShutdownResults()
    {
        DrainPendingResults(publishSideEffects: false);
    }

    private void DrainPendingResults(bool publishSideEffects)
    {
        List<PersistenceResultNotification>? notifications = null;
        lock (_drainGate)
        {
            var processedAny = false;
            while (_persistenceQueue.TryDequeueResult(out var result))
            {
                processedAny = true;
                if (!result.Succeeded)
                {
                    if (publishSideEffects)
                    {
                        notifications ??= new List<PersistenceResultNotification>();
                        notifications.Add(
                            new PersistenceResultNotification(
                                result.Manifest,
                                Succeeded: false,
                                result.Error
                            )
                        );
                    }
                    BppLog.ErrorEvent(
                        CombatReplayLogEvents.PersistenceFailed,
                        result.Error!,
                        CombatReplayLogEvents.PersistenceBattleId.Bind(result.Manifest.BattleId),
                        CombatReplayLogEvents.PersistenceRunId.Bind(result.Manifest.RunId),
                        CombatReplayLogEvents.PersistenceReasonCode.Bind(
                            result.Error is OperationCanceledException
                                ? ReplayPersistenceReasonCode.ShutdownAbandoned
                                : ReplayPersistenceReasonCode.PersistenceFailed
                        )
                    );
                    continue;
                }

                if (publishSideEffects)
                {
                    notifications ??= new List<PersistenceResultNotification>();
                    notifications.Add(
                        new PersistenceResultNotification(
                            result.Manifest,
                            Succeeded: true,
                            Error: null
                        )
                    );
                }

                if (publishSideEffects)
                {
                    try
                    {
                        _services.EventBus.Publish(
                            new PvpBattleRecorded { Manifest = result.Manifest }
                        );
                        _syncStateStore?.MarkReplayDirty(result.Manifest.BattleId);
                    }
                    catch
                    {
                        // Persistence already succeeded. A secondary observer must not suppress its
                        // authoritative result or prevent the remaining queue from draining.
                    }
                }

                BppLog.DebugEvent(
                    CombatReplayLogEvents.PersistenceSucceeded,
                    () =>
                        [
                            CombatReplayLogEvents.PersistenceBattleId.Bind(
                                result.Manifest.BattleId
                            ),
                            CombatReplayLogEvents.PersistenceRunId.Bind(result.Manifest.RunId),
                            CombatReplayLogEvents.PersistenceReasonCode.Bind(
                                ReplayPersistenceReasonCode.Persisted
                            ),
                        ]
                );
            }

            if (publishSideEffects && processedAny && !_persistenceQueue.HasPendingPersistence)
            {
                try
                {
                    _services.EventBus.Publish(new CombatReplayPersistenceDrained());
                }
                catch
                {
                    // The queue is drained regardless of an observer failure.
                }
            }
        }

        if (notifications == null)
            return;
        for (var index = 0; index < notifications.Count; index++)
        {
            var notification = notifications[index];
            NotifyResultObserver(notification.Manifest, notification.Succeeded, notification.Error);
        }
    }

    private void NotifyResultObserver(PvpBattleManifest manifest, bool succeeded, Exception? error)
    {
        try
        {
            _resultObserver?.Invoke(manifest, succeeded, error);
        }
        catch
        {
            // Persistence remains authoritative even if a UI-facing observer fails.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _persistenceQueue.Dispose();
        DrainPendingResults(publishSideEffects: true);
    }

    private void CleanupOrphanedPayloads()
    {
        var accumulator = new ReplayOrphanCleanupAccumulator();
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
                    accumulator.ReportDeleteFailure(ex);
                }
            }
        }
        catch (Exception ex)
        {
            accumulator.ReportScanFailure(ex);
        }

        if (accumulator.TryBuildResult(out var result))
            ReplayPersistenceLogWriter.EmitOrphanCleanupDegraded(result);
    }

    private readonly record struct PersistenceResultNotification(
        PvpBattleManifest Manifest,
        bool Succeeded,
        Exception? Error
    );
}
