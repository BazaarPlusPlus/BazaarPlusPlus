#nullable enable
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.Upload;

internal sealed class StartupUploadAttemptRunner
{
    private readonly UploadFeedKind _feed;
    private readonly UploadFeedLogState _logState;
    private Task<UploadAttemptResult>? _task;
    private bool _waitingForRunExitLogged;

    public StartupUploadAttemptRunner(UploadFeedKind feed, UploadFeedLogState logState)
    {
        _feed = feed;
        _logState = logState ?? throw new ArgumentNullException(nameof(logState));
    }

    public bool HasPendingTask => _task != null;

    public bool TryDrainPendingTaskOnShutdown(TimeSpan timeout, Action? afterObserved = null)
    {
        var task = _task;
        _task = null;
        if (task == null)
        {
            RunShutdownCleanup(afterObserved);
            return true;
        }

        if (task.IsCompleted)
        {
            ObserveTaskCompletion(task);
            RunShutdownCleanup(afterObserved);
            return true;
        }

        if (WaitForCompletion(task, timeout))
        {
            ObserveTaskCompletion(task);
            RunShutdownCleanup(afterObserved);
            return true;
        }

        task.ContinueWith(
            completed =>
            {
                ObserveTaskCompletion(completed);
                RunShutdownCleanup(afterObserved);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
        return false;
    }

    private static bool WaitForCompletion(Task task, TimeSpan timeout)
    {
        try
        {
            return ReferenceEquals(
                Task.WhenAny(task, Task.Delay(timeout)).GetAwaiter().GetResult(),
                task
            );
        }
        catch
        {
            return task.IsCompleted;
        }
    }

    public void Tick(
        StartupUploadAttemptGate gate,
        float currentTimeSeconds,
        bool liveRunActive,
        Func<CancellationToken, Task<UploadAttemptResult>> startAsync,
        CancellationToken cancellationToken
    )
    {
        if (gate == null)
            throw new ArgumentNullException(nameof(gate));
        if (startAsync == null)
            throw new ArgumentNullException(nameof(startAsync));

        if (_task != null)
        {
            if (!_task.IsCompleted)
                return;

            ObserveTaskCompletion(_task);
            _task = null;
            return;
        }

        switch (gate.Poll(currentTimeSeconds, liveRunActive))
        {
            case StartupUploadAttemptDecision.Wait:
                return;
            case StartupUploadAttemptDecision.SkipLiveRun:
                if (!_waitingForRunExitLogged)
                {
                    _logState.ReportDeferred(UploadLogReasonCode.LiveRunActive);
                    _waitingForRunExitLogged = true;
                }
                return;
            case StartupUploadAttemptDecision.Done:
                return;
            case StartupUploadAttemptDecision.Start:
                break;
        }

        _waitingForRunExitLogged = false;
        BppLog.DebugEvent(
            UploadLogEvents.AttemptStarted,
            () => [UploadLogEvents.AttemptStartedFeed.Bind(_feed)]
        );
        try
        {
            _task = startAsync(cancellationToken);
            if (_task == null)
            {
                _logState.ReportDegraded(
                    null,
                    UploadLogReasonCode.AttemptException,
                    new InvalidOperationException("Upload delegate returned no task.")
                );
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logState.ReportDegraded(null, UploadLogReasonCode.AttemptException, ex);
        }
    }

    private void ObserveTaskCompletion(Task<UploadAttemptResult> task)
    {
        try
        {
            _logState.Observe(task.GetAwaiter().GetResult());
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logState.ReportDegraded(null, UploadLogReasonCode.AttemptException, ex);
        }
    }

    private void RunShutdownCleanup(Action? cleanup)
    {
        if (cleanup == null)
            return;

        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                UploadLogEvents.CleanupDegraded,
                ex,
                UploadLogEvents.CleanupDegradedFeed.Bind(_feed),
                UploadLogEvents.CleanupDegradedPhase.Bind(UploadCleanupPhase.ActivationDispose),
                UploadLogEvents.CleanupDegradedReasonCode.Bind(
                    UploadLogReasonCode.ActivationDisposeException
                )
            );
        }
    }
}
