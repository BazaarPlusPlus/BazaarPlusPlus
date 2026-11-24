#nullable enable
using System.Collections.Concurrent;

namespace BazaarPlusPlus.Storage.RunLog.Replication;

public sealed class QueuedRunLogStore : IRunLogStore, IDisposable
{
    private static readonly TimeSpan DefaultShutdownDrainTimeout = TimeSpan.FromMilliseconds(2500);

    private readonly IRunLogStore _innerStore;
    private readonly IRunLogStoreLogger? _logger;
    private readonly TimeSpan _shutdownDrainTimeout;
    private readonly object _lifecycleGate = new();
    private readonly ConcurrentQueue<QueuedWrite> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly Task _worker;
    private int _stopRequested;
    private int _stopAcceptingNewWork;
    private int _disposeStarted;
    private int _activeWrites;
    private int _shutdownDrainTimedOut;

    public QueuedRunLogStore(IRunLogStore innerStore, IRunLogStoreLogger? logger = null)
        : this(innerStore, DefaultShutdownDrainTimeout, logger) { }

    internal QueuedRunLogStore(
        IRunLogStore innerStore,
        TimeSpan shutdownDrainTimeout,
        IRunLogStoreLogger? logger = null
    )
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _logger = logger;
        _shutdownDrainTimeout =
            shutdownDrainTimeout <= TimeSpan.Zero
                ? DefaultShutdownDrainTimeout
                : shutdownDrainTimeout;
        _worker = Task.Run(ProcessLoopAsync);
    }

    internal Task WorkerCompletion => _worker;

    public RunLogSessionState? TryResumeActiveRun()
    {
        return _innerStore.TryResumeActiveRun();
    }

    public RunLogSessionState CreateRun(RunLogCreateRequest request)
    {
        return _innerStore.CreateRun(request);
    }

    // The active Run's account ID may lag the queue. CompleteRun drains it before publishing
    // completion to the seal worker, and resuming a Run re-enqueues a write dropped at shutdown.
    public void SetPlayerAccountIdOnce(string runId, string? playerAccountId)
    {
        var accountId = RunLogStore.NormalizeAccountId(playerAccountId);
        if (accountId == null)
            return;

        EnqueueWrite(
            RunLogStoreWriteOperation.SetPlayerAccountId,
            runId,
            () => _innerStore.SetPlayerAccountIdOnce(runId, accountId)
        );
    }

    public void AppendEvent(string runId, RunLogEvent entry)
    {
        EnqueueWrite(
            RunLogStoreWriteOperation.AppendEvent,
            runId,
            () => _innerStore.AppendEvent(runId, entry)
        );
    }

    public void SaveCheckpoint(string runId, RunLogCheckpoint checkpoint)
    {
        EnqueueWrite(
            RunLogStoreWriteOperation.SaveCheckpoint,
            runId,
            () => _innerStore.SaveCheckpoint(runId, checkpoint)
        );
    }

    public void CompleteRun(string runId, RunLogCompletion completion)
    {
        DrainPendingWrites(runId);
        _innerStore.CompleteRun(runId, completion);
    }

    public void MarkRunAbandoned(string runId, RunLogAbandonment abandonment)
    {
        DrainPendingWrites(runId);
        _innerStore.MarkRunAbandoned(runId, abandonment);
    }

    // Bounded non-throwing flush for lifecycle pauses (e.g. Android OnPause) so queued writes reach
    // SQLite before the OS can suspend or kill the process without risking an ANR.
    public bool TryFlushPendingWrites(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || Volatile.Read(ref _stopAcceptingNewWork) == 1)
            return false;

        var drained = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        try
        {
            EnqueueWrite(
                RunLogStoreWriteOperation.DrainBarrier,
                string.Empty,
                () => drained.TrySetResult(true)
            );
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        return drained.Task.Wait(timeout);
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

        var workerCompleted = false;
        try
        {
            workerCompleted = _worker.Wait(_shutdownDrainTimeout);
        }
        catch (AggregateException) when (_worker.IsCompleted)
        {
            // ProcessLoopAsync already reported the worker failure. Dispose still owns cleanup.
            workerCompleted = true;
        }

        if (!workerCompleted)
        {
            // A late worker error belongs to the same shutdown timeout, including Mono aborting
            // background threads during exit. Suppress a duplicate terminal record.
            Volatile.Write(ref _shutdownDrainTimedOut, 1);
            _logger?.Emit(
                RunLogStoreDiagnostic.ShutdownDrainTimedOut(
                    _shutdownDrainTimeout,
                    PendingWriteCount()
                )
            );
            _ = _worker.ContinueWith(
                static (_, state) => ((SemaphoreSlim)state!).Dispose(),
                _signal,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
            return;
        }

        _signal.Dispose();
    }

    private void DrainPendingWrites(string runId)
    {
        // Use TaskCompletionSource instead of a disposable wait handle so a late worker completion
        // after timeout cannot throw ObjectDisposedException.
        var drained = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        EnqueueWrite(
            RunLogStoreWriteOperation.DrainBarrier,
            runId,
            () => drained.TrySetResult(true)
        );
        if (!drained.Task.Wait(_shutdownDrainTimeout))
        {
            throw new TimeoutException(
                "Timed out while waiting for queued run logging writes to drain."
            );
        }
    }

    private void EnqueueWrite(RunLogStoreWriteOperation operation, string runId, Action action)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _stopAcceptingNewWork) == 1)
            {
                throw new ObjectDisposedException(
                    nameof(QueuedRunLogStore),
                    $"Cannot queue {operation} after shutdown."
                );
            }

            _pending.Enqueue(new QueuedWrite(operation, runId, action));
            _signal.Release();
        }
    }

    private async Task ProcessLoopAsync()
    {
        try
        {
            while (true)
            {
                while (_pending.TryDequeue(out var write))
                {
                    Interlocked.Increment(ref _activeWrites);
                    try
                    {
                        write.Execute();
                    }
                    catch (Exception ex)
                    {
                        if (Volatile.Read(ref _shutdownDrainTimedOut) == 0)
                        {
                            _logger?.Emit(
                                RunLogStoreDiagnostic.WriteFailed(write.Operation, write.RunId, ex)
                            );
                        }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _activeWrites);
                    }
                }

                if (Volatile.Read(ref _stopRequested) == 1 && _pending.IsEmpty)
                    return;

                await _signal.WaitAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            if (Volatile.Read(ref _shutdownDrainTimedOut) == 0)
                _logger?.Emit(RunLogStoreDiagnostic.WorkerFailed(PendingWriteCount(), ex));
            throw;
        }
    }

    private int PendingWriteCount() => _pending.Count + Volatile.Read(ref _activeWrites);

    private readonly struct QueuedWrite
    {
        public QueuedWrite(RunLogStoreWriteOperation operation, string runId, Action action)
        {
            Operation = operation;
            RunId = runId;
            Action = action;
        }

        public RunLogStoreWriteOperation Operation { get; }

        public string RunId { get; }

        private Action Action { get; }

        public void Execute()
        {
            Action();
        }
    }
}
