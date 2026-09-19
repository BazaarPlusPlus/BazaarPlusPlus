#nullable enable
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.RunLog.Replication;
using Xunit;

namespace RunLoggingQueueLogging.Tests;

public sealed class QueuedRunLogStoreDiagnosticTests
{
    [Fact]
    public async Task Failed_append_emits_typed_write_diagnostic_without_description_text()
    {
        var failure = new InvalidOperationException("write failed");
        var logger = new CapturingRunLogStoreLogger();
        var inner = new DelegatingRunLogStore { Append = (_, _) => throw failure };
        var store = new QueuedRunLogStore(inner, TimeSpan.FromSeconds(1), logger);

        store.AppendEvent("run-123456789", new RunLogEvent());
        var diagnostic = await logger.Next.WaitAsync(TimeSpan.FromSeconds(2));
        store.Dispose();

        Assert.Equal(RunLogStoreDiagnosticKind.WriteFailed, diagnostic.Kind);
        Assert.Equal(RunLogStoreWriteOperation.AppendEvent, diagnostic.Operation);
        Assert.Equal("run-123456789", diagnostic.RunId);
        Assert.Same(failure, diagnostic.Exception);
        Assert.Null(diagnostic.TimeoutMilliseconds);
    }

    [Fact]
    public async Task Shutdown_timeout_emits_timeout_and_outstanding_write_count()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var logger = new CapturingRunLogStoreLogger();
        var inner = new DelegatingRunLogStore
        {
            Append = (_, _) =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
                throw new InvalidOperationException("write aborted after shutdown timeout");
            },
        };
        var store = new QueuedRunLogStore(inner, TimeSpan.FromMilliseconds(25), logger);

        store.AppendEvent("run-1", new RunLogEvent());
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
        store.Dispose();
        var diagnostic = await logger.Next.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(RunLogStoreDiagnosticKind.ShutdownDrainTimedOut, diagnostic.Kind);
        Assert.Equal(25, diagnostic.TimeoutMilliseconds);
        Assert.Equal(1, diagnostic.PendingCount);
        Assert.Null(diagnostic.RunId);
        Assert.Null(diagnostic.Exception);

        release.Set();
        await store.WorkerCompletion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Single(logger.Diagnostics);
    }

    [Fact]
    public async Task Unexpected_worker_exit_emits_typed_terminal_diagnostic()
    {
        var failure = new InvalidOperationException("signal failed");
        var logger = new CapturingRunLogStoreLogger();
        var store = new QueuedRunLogStore(
            new DelegatingRunLogStore(),
            TimeSpan.FromSeconds(1),
            logger,
            () => Task.FromException(failure)
        );

        var diagnostic = await logger.Next.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(RunLogStoreDiagnosticKind.WorkerFailed, diagnostic.Kind);
        Assert.Equal(0, diagnostic.PendingCount);
        Assert.Same(failure, diagnostic.Exception);
        Assert.Null(diagnostic.Operation);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.WorkerCompletion
        );
        var disposeFailure = Record.Exception(store.Dispose);
        Assert.Null(disposeFailure);
        store.Dispose();
    }

    private sealed class CapturingRunLogStoreLogger : IRunLogStoreLogger
    {
        private readonly List<RunLogStoreDiagnostic> _diagnostics = new();
        private readonly TaskCompletionSource<RunLogStoreDiagnostic> _next = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal Task<RunLogStoreDiagnostic> Next => _next.Task;
        internal IReadOnlyList<RunLogStoreDiagnostic> Diagnostics
        {
            get
            {
                lock (_diagnostics)
                    return _diagnostics.ToArray();
            }
        }

        public void Emit(RunLogStoreDiagnostic diagnostic)
        {
            lock (_diagnostics)
                _diagnostics.Add(diagnostic);
            _next.TrySetResult(diagnostic);
        }
    }

    private sealed class DelegatingRunLogStore : IRunLogStore
    {
        internal Action<string, RunLogEvent>? Append { get; init; }

        public RunLogSessionState? TryResumeActiveRun() => null;

        public RunLogSessionState CreateRun(RunLogCreateRequest request) => new();

        public void SetPlayerAccountIdOnce(string runId, string? playerAccountId) { }

        public void AppendEvent(string runId, RunLogEvent entry) => Append?.Invoke(runId, entry);

        public void SaveCheckpoint(string runId, RunLogCheckpoint checkpoint) { }

        public void CompleteRun(string runId, RunLogCompletion completion) { }

        public void MarkRunAbandoned(string runId, RunLogAbandonment abandonment) { }
    }
}
