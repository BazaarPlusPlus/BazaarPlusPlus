#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.PvpBattles;

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

        if (!_worker.Wait(ShutdownDrainTimeout))
        {
            var abandonedCount = Volatile.Read(ref _pendingSaveCount);
            if (abandonedCount > 0)
            {
                BppLog.Warn(
                    "CombatReplayPersistenceQueue",
                    $"Abandoning {abandonedCount} pending replay persistence request(s) after shutdown timeout."
                );
            }

            _shutdown.Cancel();
            _worker.Wait();
        }

        EnqueueAbandonedPendingResults();
        _signal.Dispose();
        _shutdown.Dispose();
    }

    private async Task ProcessLoopAsync()
    {
        while (true)
        {
            while (!_shutdown.IsCancellationRequested && _pending.TryDequeue(out var request))
            {
                var payloadSaved = false;
                try
                {
                    _savePayload(request.Payload);
                    payloadSaved = true;
                    _saveManifest(request.Manifest);
                    _completed.Enqueue(CombatReplayPersistenceResult.Success(request.Manifest));
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
                            BppLog.Warn(
                                "CombatReplayPersistenceQueue",
                                $"Failed to delete replay payload {request.Payload.BattleId} after manifest persistence failure: {rollbackEx.Message}"
                            );
                        }
                    }

                    _completed.Enqueue(CombatReplayPersistenceResult.Failure(request.Manifest, ex));
                }
                finally
                {
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
            _completed.Enqueue(
                CombatReplayPersistenceResult.Failure(
                    request.Manifest,
                    new OperationCanceledException(
                        $"Replay persistence for {request.Manifest.BattleId} was abandoned during shutdown."
                    )
                )
            );
            Interlocked.Decrement(ref _pendingSaveCount);
        }
    }

    private readonly struct CombatReplayPersistenceRequest
    {
        public CombatReplayPersistenceRequest(PvpReplayPayload payload, PvpBattleManifest manifest)
        {
            Payload = payload;
            Manifest = manifest;
        }

        public PvpReplayPayload Payload { get; }

        public PvpBattleManifest Manifest { get; }
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
