#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.RunLogging.Models;

namespace BazaarPlusPlus.Game.RunLogging.Persistence;

internal sealed class QueuedRunLogStore : IRunLogStore, IDisposable
{
    private static readonly TimeSpan DefaultShutdownDrainTimeout = TimeSpan.FromMilliseconds(2500);

    private readonly IRunLogStore _innerStore;
    private readonly TimeSpan _shutdownDrainTimeout;
    private readonly object _lifecycleGate = new();
    private readonly ConcurrentQueue<QueuedWrite> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly Task _worker;
    private int _stopRequested;
    private int _stopAcceptingNewWork;
    private int _disposeStarted;

    public QueuedRunLogStore(IRunLogStore innerStore)
        : this(innerStore, DefaultShutdownDrainTimeout) { }

    internal QueuedRunLogStore(IRunLogStore innerStore, TimeSpan shutdownDrainTimeout)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _shutdownDrainTimeout =
            shutdownDrainTimeout <= TimeSpan.Zero
                ? DefaultShutdownDrainTimeout
                : shutdownDrainTimeout;
        _worker = Task.Run(ProcessLoopAsync);
    }

    public RunLogSessionState? TryResumeActiveRun()
    {
        return _innerStore.TryResumeActiveRun();
    }

    public RunLogSessionState CreateRun(RunLogCreateRequest request)
    {
        return _innerStore.CreateRun(request);
    }

    public void AppendEvent(string runId, RunLogEvent entry)
    {
        EnqueueWrite($"append event for run {runId}", () => _innerStore.AppendEvent(runId, entry));
    }

    public void SaveCheckpoint(string runId, RunLogCheckpoint checkpoint)
    {
        EnqueueWrite(
            $"save checkpoint for run {runId}",
            () => _innerStore.SaveCheckpoint(runId, checkpoint)
        );
    }

    public void CompleteRun(string runId, RunLogCompletion completion)
    {
        DrainPendingWrites();
        _innerStore.CompleteRun(runId, completion);
    }

    public void MarkRunAbandoned(string runId, RunLogAbandonment abandonment)
    {
        DrainPendingWrites();
        _innerStore.MarkRunAbandoned(runId, abandonment);
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

        if (!_worker.Wait(_shutdownDrainTimeout))
        {
            BppLog.Warn(
                "QueuedRunLogStore",
                "Timed out while draining queued run logging writes during shutdown."
            );
            return;
        }

        _signal.Dispose();
    }

    private void DrainPendingWrites()
    {
        using var drained = new ManualResetEventSlim(false);
        EnqueueWrite("drain barrier", drained.Set);
        if (!drained.Wait(_shutdownDrainTimeout))
        {
            throw new TimeoutException(
                "Timed out while waiting for queued run logging writes to drain."
            );
        }
    }

    private void EnqueueWrite(string description, Action action)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _stopAcceptingNewWork) == 1)
            {
                throw new ObjectDisposedException(
                    nameof(QueuedRunLogStore),
                    $"Cannot queue write after shutdown: {description}."
                );
            }

            _pending.Enqueue(new QueuedWrite(description, action));
            _signal.Release();
        }
    }

    private async Task ProcessLoopAsync()
    {
        while (true)
        {
            while (_pending.TryDequeue(out var write))
            {
                try
                {
                    write.Execute();
                }
                catch (Exception ex)
                {
                    BppLog.Error("QueuedRunLogStore", $"Failed to {write.Description}.", ex);
                }
            }

            if (Volatile.Read(ref _stopRequested) == 1 && _pending.IsEmpty)
                return;

            await _signal.WaitAsync().ConfigureAwait(false);
        }
    }

    private readonly struct QueuedWrite
    {
        public QueuedWrite(string description, Action action)
        {
            Description = description;
            Action = action;
        }

        public string Description { get; }

        private Action Action { get; }

        public void Execute()
        {
            Action();
        }
    }
}
