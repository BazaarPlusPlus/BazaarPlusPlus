#nullable enable
namespace BazaarPlusPlus.Storage.RunLog;

public enum RunLogStoreDiagnosticKind
{
    ShutdownDrainTimedOut,
    WriteFailed,
    WorkerFailed,
}

public enum RunLogStoreWriteOperation
{
    AppendEvent,
    SaveCheckpoint,
    DrainBarrier,
}

public sealed class RunLogStoreDiagnostic
{
    private RunLogStoreDiagnostic(
        RunLogStoreDiagnosticKind kind,
        long? timeoutMilliseconds,
        int pendingCount,
        RunLogStoreWriteOperation? operation,
        string? runId,
        Exception? exception
    )
    {
        Kind = kind;
        TimeoutMilliseconds = timeoutMilliseconds;
        PendingCount = pendingCount;
        Operation = operation;
        RunId = runId;
        Exception = exception;
    }

    public RunLogStoreDiagnosticKind Kind { get; }

    public long? TimeoutMilliseconds { get; }

    public int PendingCount { get; }

    public RunLogStoreWriteOperation? Operation { get; }

    public string? RunId { get; }

    public Exception? Exception { get; }

    internal static RunLogStoreDiagnostic ShutdownDrainTimedOut(
        TimeSpan timeout,
        int pendingCount
    ) =>
        new(
            RunLogStoreDiagnosticKind.ShutdownDrainTimedOut,
            ToMilliseconds(timeout),
            pendingCount,
            null,
            null,
            null
        );

    internal static RunLogStoreDiagnostic WriteFailed(
        RunLogStoreWriteOperation operation,
        string runId,
        Exception exception
    ) => new(RunLogStoreDiagnosticKind.WriteFailed, null, 0, operation, runId, exception);

    internal static RunLogStoreDiagnostic WorkerFailed(int pendingCount, Exception exception) =>
        new(RunLogStoreDiagnosticKind.WorkerFailed, null, pendingCount, null, null, exception);

    private static long ToMilliseconds(TimeSpan timeout)
    {
        var milliseconds = timeout.TotalMilliseconds;
        if (milliseconds >= long.MaxValue)
            return long.MaxValue;
        if (milliseconds <= 0)
            return 0;
        return (long)Math.Ceiling(milliseconds);
    }
}
