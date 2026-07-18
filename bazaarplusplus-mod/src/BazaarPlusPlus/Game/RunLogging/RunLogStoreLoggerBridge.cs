#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Storage.RunLog;

namespace BazaarPlusPlus.Game.RunLogging;

internal sealed class RunLogStoreLoggerBridge : IRunLogStoreLogger
{
    public void Emit(RunLogStoreDiagnostic diagnostic)
    {
        switch (diagnostic.Kind)
        {
            case RunLogStoreDiagnosticKind.ShutdownDrainTimedOut:
                BppLog.WarnEvent(
                    RunLoggingLogEvents.QueueShutdownDegraded,
                    RunLoggingLogEvents.QueueShutdownTimeoutMilliseconds.Bind(
                        diagnostic.TimeoutMilliseconds
                    ),
                    RunLoggingLogEvents.QueueShutdownPendingCount.Bind(diagnostic.PendingCount),
                    RunLoggingLogEvents.QueueShutdownReasonCode.Bind(
                        RunLoggingReasonCode.QueueShutdownDrainTimeout
                    )
                );
                return;
            case RunLogStoreDiagnosticKind.WriteFailed:
                BppLog.ErrorEvent(
                    RunLoggingLogEvents.QueueWriteFailed,
                    diagnostic.Exception!,
                    RunLoggingLogEvents.RunId.Bind(diagnostic.RunId),
                    RunLoggingLogEvents.QueueWriteOperation.Bind(diagnostic.Operation),
                    RunLoggingLogEvents.QueueWriteReasonCode.Bind(
                        RunLoggingReasonCode.QueueWriteException
                    )
                );
                return;
            case RunLogStoreDiagnosticKind.WorkerFailed:
                BppLog.ErrorEvent(
                    RunLoggingLogEvents.QueueWorkerFailed,
                    diagnostic.Exception!,
                    RunLoggingLogEvents.QueueWorkerPendingCount.Bind(diagnostic.PendingCount),
                    RunLoggingLogEvents.QueueWorkerReasonCode.Bind(
                        RunLoggingReasonCode.QueueWorkerTerminatedUnexpectedly
                    )
                );
                return;
        }
    }
}
