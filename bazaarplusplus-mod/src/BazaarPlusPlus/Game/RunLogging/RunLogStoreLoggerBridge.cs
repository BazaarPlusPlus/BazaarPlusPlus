#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
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
                    new BppLogEvent(
                        BppLogFeatureScope.RunLogging,
                        "run_logging.queue.shutdown_degraded",
                        storm: ["reason_code"]
                    ),
                    ("timeout_ms", diagnostic.TimeoutMilliseconds),
                    ("pending_count", diagnostic.PendingCount),
                    ("reason_code", RunLoggingReasonCode.QueueShutdownDrainTimeout)
                );
                return;
            case RunLogStoreDiagnosticKind.WriteFailed:
                BppLog.ErrorEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.RunLogging,
                        "run_logging.queue.write_failed",
                        storm: []
                    ),
                    diagnostic.Exception!,
                    ("run_id", diagnostic.RunId, BppLogCorrelationPolicy.Short),
                    ("operation", diagnostic.Operation),
                    ("reason_code", RunLoggingReasonCode.QueueWriteException)
                );
                return;
            case RunLogStoreDiagnosticKind.WorkerFailed:
                BppLog.ErrorEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.RunLogging,
                        "run_logging.queue.worker_failed",
                        storm: []
                    ),
                    diagnostic.Exception!,
                    ("pending_count", diagnostic.PendingCount),
                    ("reason_code", RunLoggingReasonCode.QueueWorkerTerminatedUnexpectedly)
                );
                return;
        }
    }
}
