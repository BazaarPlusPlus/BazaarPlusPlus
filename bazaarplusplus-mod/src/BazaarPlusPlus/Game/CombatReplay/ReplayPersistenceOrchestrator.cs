#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Storage.Paths;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class ReplayPersistenceOrchestrator : IDisposable
{
    private readonly IBppServices _services;
    private readonly IPvpBattleCatalog _battleCatalog;
    private readonly CombatReplayPayloadStore _payloadStore;
    private readonly CombatReplayPersistenceQueue _persistenceQueue;
    private readonly ReplayPayloadOperationGate _operationGate = new();
    private readonly ReplayPayloadMaintenanceService _payloadMaintenance;
    private readonly ReplayMaintenanceFlight _maintenanceFlight = new();
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

        var combatReplayDirectoryPath = PathConstants.CombatReplays(
            services.Paths.RequireDataRoot()
        );

        _payloadStore = new CombatReplayPayloadStore(combatReplayDirectoryPath);
        _persistenceQueue = new CombatReplayPersistenceQueue(
            _payloadStore.Save,
            _battleCatalog.Save,
            _payloadStore.Delete,
            _operationGate
        );
        _persistenceQueue.SetLateResultsAvailableCallback(DrainLateShutdownResults);
        _payloadMaintenance = new ReplayPayloadMaintenanceService(
            _battleCatalog,
            _payloadStore,
            _operationGate,
            _persistenceQueue.SnapshotProtectedBattleIds
        );
        _maintenanceFlight.Start(token => Task.Run(() => RunMaintenance(token), token));
    }

    public IPvpBattleCatalog Catalog => _battleCatalog;
    public CombatReplayPayloadStore PayloadStore => _payloadStore;
    public bool HasPendingPersistence => _persistenceQueue.HasPendingPersistence;

    internal IDisposable? TryAcquirePlaybackPayloadLease() => _operationGate.TryAcquirePlayback();

    public void Enqueue(PvpReplayPayload payload, PvpBattleManifest manifest)
    {
        if (_disposed)
            return;

        ReplayPersistenceStateTracker.Enqueued(manifest.RunId);
        try
        {
            _persistenceQueue.Enqueue(payload, manifest);
        }
        catch
        {
            ReplayPersistenceStateTracker.Completed(manifest.RunId);
            throw;
        }
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
                ReplayPersistenceStateTracker.Completed(result.Manifest.RunId);
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

        _maintenanceFlight.Dispose();
        _persistenceQueue.Dispose();
        DrainPendingResults(publishSideEffects: true);
        _operationGate.Dispose();
    }

    private void RunMaintenance(CancellationToken cancellationToken)
    {
        try
        {
            var result = _payloadMaintenance.Run(DateTimeOffset.UtcNow, cancellationToken);
            ReplayPersistenceLogWriter.EmitMaintenanceTerminal(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            ReplayPersistenceLogWriter.EmitMaintenanceFailed(ex);
        }
    }

    private readonly record struct PersistenceResultNotification(
        PvpBattleManifest Manifest,
        bool Succeeded,
        Exception? Error
    );
}
