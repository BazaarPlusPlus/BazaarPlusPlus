#nullable enable
using System.Collections.Concurrent;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayPersistenceQueue : IDisposable
{
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromMilliseconds(500);

    private readonly Action<PvpReplayPayload> _savePayload;
    private readonly Action<PvpBattleManifest> _saveManifest;
    private readonly Action<string> _deletePayload;
    private readonly object _lifecycleGate = new();
    private readonly ConcurrentQueue<CombatReplayPersistenceRequest> _pending = new();
    private readonly ConcurrentQueue<CombatReplayPersistenceResult> _completed = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private int _outstandingPersistenceCount;
    private int _pendingSaveCount;
    private int _stopRequested;
    private int _stopAcceptingNewWork;
    private int _disposeStarted;
    private int _cleanupStarted;
    private CombatReplayPersistenceRequest? _inFlight;
    private Action? _lateResultsAvailable;

    public CombatReplayPersistenceQueue(
        Action<PvpReplayPayload> savePayload,
        Action<PvpBattleManifest> saveManifest,
        Action<string> deletePayload
    )
    {
        _savePayload = savePayload ?? throw new ArgumentNullException(nameof(savePayload));
        _saveManifest = saveManifest ?? throw new ArgumentNullException(nameof(saveManifest));
        _deletePayload = deletePayload ?? throw new ArgumentNullException(nameof(deletePayload));
        _worker = Task.Run(ProcessLoopAsync);
    }

    public bool HasPendingPersistence => Volatile.Read(ref _outstandingPersistenceCount) > 0;

    public void SetLateResultsAvailableCallback(Action callback)
    {
        Volatile.Write(
            ref _lateResultsAvailable,
            callback ?? throw new ArgumentNullException(nameof(callback))
        );
    }

    public void Enqueue(PvpReplayPayload payload, PvpBattleManifest manifest)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));

        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _stopAcceptingNewWork) == 1 || _shutdown.IsCancellationRequested)
                throw new ObjectDisposedException(nameof(CombatReplayPersistenceQueue));

            _pending.Enqueue(new CombatReplayPersistenceRequest(payload, manifest));
            Interlocked.Increment(ref _outstandingPersistenceCount);
            Interlocked.Increment(ref _pendingSaveCount);
            _signal.Release();
        }
    }

    public bool TryDequeueResult(out CombatReplayPersistenceResult result)
    {
        if (!_completed.TryDequeue(out result))
            return false;

        Interlocked.Decrement(ref _outstandingPersistenceCount);
        return true;
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) == 1)
                return;

            Volatile.Write(ref _stopAcceptingNewWork, 1);
            Volatile.Write(ref _stopRequested, 1);
            _signal.Release();
        }

        if (_worker.Wait(ShutdownDrainTimeout))
        {
            CleanupWorkerResources();
            return;
        }

        int pendingQueuedCount;
        bool inFlight;
        _shutdown.Cancel();
        lock (_lifecycleGate)
        {
            pendingQueuedCount = _pending.Count;
            inFlight = _inFlight != null;
            EnqueueAbandonedPendingResults();
        }
        BppLog.DebugEvent(
            CombatReplayLogEvents.PersistenceShutdownIncomplete,
            () =>
                [
                    CombatReplayLogEvents.ShutdownPendingCount.Bind(pendingQueuedCount),
                    CombatReplayLogEvents.ShutdownInFlight.Bind(inFlight),
                    CombatReplayLogEvents.ShutdownTimeoutMs.Bind(
                        (long)ShutdownDrainTimeout.TotalMilliseconds
                    ),
                ]
        );
        _ = _worker.ContinueWith(
            static (_, state) => ((CombatReplayPersistenceQueue)state!).CompleteTimedOutShutdown(),
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private void CompleteTimedOutShutdown()
    {
        CleanupWorkerResources();
        try
        {
            Volatile.Read(ref _lateResultsAvailable)?.Invoke();
        }
        catch
        {
            // A teardown observer must not fault the worker continuation.
        }
    }

    private async Task ProcessLoopAsync()
    {
        while (true)
        {
            while (!_shutdown.IsCancellationRequested)
            {
                CombatReplayPersistenceRequest? request;
                lock (_lifecycleGate)
                {
                    if (_shutdown.IsCancellationRequested || !_pending.TryDequeue(out request))
                        break;
                    _inFlight = request;
                }
                var payloadSaved = false;
                try
                {
                    _savePayload(request.Payload);
                    payloadSaved = true;
                    _saveManifest(request.Manifest);
                    Complete(request, CombatReplayPersistenceResult.Success(request.Manifest));
                }
                catch (Exception ex)
                {
                    if (payloadSaved)
                    {
                        try
                        {
                            _deletePayload(request.Payload.BattleId);
                        }
                        catch (Exception rollbackEx)
                        {
                            BppLog.DebugEvent(
                                CombatReplayLogEvents.PersistenceRollbackCleanupFailed,
                                rollbackEx,
                                () =>
                                    [
                                        CombatReplayLogEvents.RollbackCleanupBattleId.Bind(
                                            request.Payload.BattleId
                                        ),
                                    ]
                            );
                        }
                    }

                    Complete(request, CombatReplayPersistenceResult.Failure(request.Manifest, ex));
                }
                finally
                {
                    lock (_lifecycleGate)
                    {
                        if (ReferenceEquals(_inFlight, request))
                            _inFlight = null;
                    }
                    Interlocked.Decrement(ref _pendingSaveCount);
                }
            }

            if (ShouldExitWorkerLoop())
                return;

            try
            {
                await _signal.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private bool ShouldExitWorkerLoop()
    {
        return Volatile.Read(ref _stopRequested) == 1
            && _pending.IsEmpty
            && Volatile.Read(ref _pendingSaveCount) == 0;
    }

    private void EnqueueAbandonedPendingResults()
    {
        while (_pending.TryDequeue(out var request))
        {
            CompleteAsAbandoned(request);
            Interlocked.Decrement(ref _pendingSaveCount);
        }
    }

    private void CompleteAsAbandoned(CombatReplayPersistenceRequest request)
    {
        Complete(
            request,
            CombatReplayPersistenceResult.Failure(
                request.Manifest,
                new OperationCanceledException("Replay persistence was abandoned during shutdown.")
            )
        );
    }

    private void Complete(
        CombatReplayPersistenceRequest request,
        CombatReplayPersistenceResult result
    )
    {
        if (request.CompletionGate.TryComplete())
            _completed.Enqueue(result);
    }

    private void CleanupWorkerResources()
    {
        if (Interlocked.Exchange(ref _cleanupStarted, 1) == 1)
            return;

        _signal.Dispose();
        _shutdown.Dispose();
    }

    private sealed class CombatReplayPersistenceRequest
    {
        public CombatReplayPersistenceRequest(PvpReplayPayload payload, PvpBattleManifest manifest)
        {
            Payload = payload;
            Manifest = manifest;
        }

        public PvpReplayPayload Payload { get; }

        public PvpBattleManifest Manifest { get; }

        internal ReplayPersistenceCompletionGate CompletionGate { get; } = new();
    }
}

internal readonly struct CombatReplayPersistenceResult
{
    public CombatReplayPersistenceResult(PvpBattleManifest manifest, Exception? error)
    {
        Manifest = manifest;
        Error = error;
    }

    public PvpBattleManifest Manifest { get; }

    public Exception? Error { get; }

    public bool Succeeded => Error == null;

    public static CombatReplayPersistenceResult Success(PvpBattleManifest manifest)
    {
        return new CombatReplayPersistenceResult(manifest, null);
    }

    public static CombatReplayPersistenceResult Failure(PvpBattleManifest manifest, Exception error)
    {
        return new CombatReplayPersistenceResult(manifest, error);
    }
}
