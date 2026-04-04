#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BazaarPlusPlus.Game.Upload;

internal sealed class StartupUploadAttemptRunner
{
    private readonly string _logScope;
    private readonly string _skipLiveRunMessage;
    private readonly string _startMessage;
    private readonly string _failureMessage;
    private Task? _task;
    private bool _waitingForRunExitLogged;

    public StartupUploadAttemptRunner(
        string logScope,
        string skipLiveRunMessage,
        string startMessage,
        string failureMessage
    )
    {
        _logScope = string.IsNullOrWhiteSpace(logScope)
            ? throw new ArgumentException("Log scope is required.", nameof(logScope))
            : logScope.Trim();
        _skipLiveRunMessage = string.IsNullOrWhiteSpace(skipLiveRunMessage)
            ? throw new ArgumentException(
                "Skip-live-run message is required.",
                nameof(skipLiveRunMessage)
            )
            : skipLiveRunMessage.Trim();
        _startMessage = string.IsNullOrWhiteSpace(startMessage)
            ? throw new ArgumentException("Start message is required.", nameof(startMessage))
            : startMessage.Trim();
        _failureMessage = string.IsNullOrWhiteSpace(failureMessage)
            ? throw new ArgumentException("Failure message is required.", nameof(failureMessage))
            : failureMessage.Trim();
    }

    public bool HasPendingTask => _task != null;

    public void Tick(
        StartupUploadAttemptGate gate,
        float currentTimeSeconds,
        bool liveRunActive,
        Func<CancellationToken, Task> startAsync,
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

            try
            {
                _task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                BppLog.Error(_logScope, $"{_failureMessage}: {ex}");
            }
            finally
            {
                _task = null;
            }

            return;
        }

        switch (gate.Poll(currentTimeSeconds, liveRunActive))
        {
            case StartupUploadAttemptDecision.Wait:
                return;
            case StartupUploadAttemptDecision.SkipLiveRun:
                if (!_waitingForRunExitLogged)
                {
                    BppLog.Info(_logScope, _skipLiveRunMessage);
                    _waitingForRunExitLogged = true;
                }
                return;
            case StartupUploadAttemptDecision.Done:
                return;
            case StartupUploadAttemptDecision.Start:
                break;
        }

        _waitingForRunExitLogged = false;
        BppLog.Info(_logScope, _startMessage);
        _task = startAsync(cancellationToken);
    }
}
