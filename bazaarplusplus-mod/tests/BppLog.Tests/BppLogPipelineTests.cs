using System.Collections.Concurrent;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class BppLogPipelineTests
{
    private static readonly BppLogFieldDefinition WarningReason = new(
        0,
        "reason",
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );
    private static readonly BppLogEventDefinition GuardedWarning = new(
        BppLogFeatureScope.Logger,
        "logging.test.degraded",
        new[] { WarningReason },
        new BppLogStormPolicy(new[] { WarningReason })
    );
    private static readonly BppLogFieldDefinition RequestId = new(
        0,
        "request_id",
        BppLogCorrelationPolicy.Short,
        BppLogCardinality.High
    );
    private static readonly BppLogEventDefinition GuardedError = new(
        BppLogFeatureScope.Logger,
        "logging.test.failed",
        new[] { RequestId },
        new BppLogStormPolicy(Array.Empty<BppLogFieldDefinition>())
    );
    private static readonly BppLogEventDefinition GuardedErrorWithoutCorrelation = new(
        BppLogFeatureScope.Logger,
        "logging.global.failed",
        Array.Empty<BppLogFieldDefinition>(),
        new BppLogStormPolicy(Array.Empty<BppLogFieldDefinition>())
    );
    private static readonly BppLogEventDefinition UnguardedInfo = new(
        BppLogFeatureScope.Logger,
        "logging.test.succeeded",
        Array.Empty<BppLogFieldDefinition>()
    );
    private static readonly BppLogEventDefinition RecoveredInfo = new(
        BppLogFeatureScope.Logger,
        "logging.test.recovered",
        Array.Empty<BppLogFieldDefinition>()
    );

    [Fact]
    public void First_guarded_occurrence_is_emitted_immediately()
    {
        var (pipeline, output, _) = CreatePipeline();

        pipeline.Emit(
            BppLogSeverity.Warning,
            GuardedWarning,
            new[] { WarningReason.Bind("offline") }
        );

        var record = Assert.Single(output);
        Assert.Equal(BppLogSeverity.Warning, record.Severity);
        Assert.Contains("event=logging.test.degraded", record.Message);
        Assert.Contains("reason=offline", record.Message);
    }

    [Fact]
    public void Guarded_repeats_are_summarized_on_shutdown_flush()
    {
        var (pipeline, output, _) = CreatePipeline();
        var fields = new[] { WarningReason.Bind("offline") };

        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields);
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields);
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields);
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields);

        Assert.Single(output);
        pipeline.Flush();

        Assert.Equal(2, output.Count);
        Assert.Contains("event=logging.storm.suppressed", output[1].Message);
        Assert.Contains("source_event=logging.test.degraded", output[1].Message);
        Assert.Contains("suppressed_count=3", output[1].Message);
        Assert.Contains("window_ms=30000", output[1].Message);
        Assert.Contains("flush_reason=shutdown", output[1].Message);
    }

    [Fact]
    public void Unguarded_duplicates_are_never_suppressed()
    {
        var (pipeline, output, _) = CreatePipeline();

        pipeline.Emit(BppLogSeverity.Info, UnguardedInfo);
        pipeline.Emit(BppLogSeverity.Info, UnguardedInfo);

        Assert.Equal(2, output.Count);
    }

    [Fact]
    public void Different_warning_reason_dimensions_have_distinct_keys()
    {
        var (pipeline, output, _) = CreatePipeline();

        pipeline.Emit(
            BppLogSeverity.Warning,
            GuardedWarning,
            new[] { WarningReason.Bind("offline") }
        );
        pipeline.Emit(
            BppLogSeverity.Warning,
            GuardedWarning,
            new[] { WarningReason.Bind("timeout") }
        );

        Assert.Equal(2, output.Count);
    }

    [Fact]
    public void Error_keys_use_full_correlation_not_short_display_value()
    {
        var (pipeline, output, _) = CreatePipeline();
        var exception = new InvalidOperationException("same failure");

        pipeline.Emit(
            BppLogSeverity.Error,
            GuardedError,
            new[] { RequestId.Bind("12345678-first") },
            exception
        );
        pipeline.Emit(
            BppLogSeverity.Error,
            GuardedError,
            new[] { RequestId.Bind("12345678-second") },
            exception
        );

        Assert.Equal(2, output.Count);
        Assert.All(output, record => Assert.Contains("request_id=12345678", record.Message));
    }

    [Fact]
    public void Error_keys_keep_different_exception_fingerprints_distinct()
    {
        var (pipeline, output, _) = CreatePipeline();
        var fields = new[] { RequestId.Bind("request-1") };

        pipeline.Emit(
            BppLogSeverity.Error,
            GuardedError,
            fields,
            new InvalidOperationException("failure one")
        );
        pipeline.Emit(
            BppLogSeverity.Error,
            GuardedError,
            fields,
            new InvalidOperationException("failure two")
        );

        Assert.Equal(2, output.Count);
    }

    [Fact]
    public void Matching_error_correlation_and_fingerprint_are_suppressed()
    {
        var (pipeline, output, _) = CreatePipeline();
        var fields = new[] { RequestId.Bind("request-1") };
        var exception = new InvalidOperationException("same instance");

        pipeline.Emit(BppLogSeverity.Error, GuardedError, fields, exception);
        pipeline.Emit(BppLogSeverity.Error, GuardedError, fields, exception);
        pipeline.Flush();

        Assert.Equal(2, output.Count);
        Assert.Contains("suppressed_count=1", output[1].Message);
    }

    [Fact]
    public void Error_without_an_exception_fails_open()
    {
        var (pipeline, output, _) = CreatePipeline();
        var fields = new[] { RequestId.Bind("request-1") };

        pipeline.Emit(BppLogSeverity.Error, GuardedError, fields);
        pipeline.Emit(BppLogSeverity.Error, GuardedError, fields);

        Assert.Equal(2, output.Count);
    }

    [Fact]
    public void Error_without_declared_correlation_uses_exception_fingerprint()
    {
        var (pipeline, output, _) = CreatePipeline();
        var exception = new InvalidOperationException("same failure");

        pipeline.Emit(BppLogSeverity.Error, GuardedErrorWithoutCorrelation, exception: exception);
        pipeline.Emit(BppLogSeverity.Error, GuardedErrorWithoutCorrelation, exception: exception);
        pipeline.Flush();

        Assert.Equal(2, output.Count);
        Assert.Contains("suppressed_count=1", output[1].Message);
    }

    [Fact]
    public void Error_without_declared_correlation_or_exception_fails_open()
    {
        var (pipeline, output, _) = CreatePipeline();

        pipeline.Emit(BppLogSeverity.Error, GuardedErrorWithoutCorrelation);
        pipeline.Emit(BppLogSeverity.Error, GuardedErrorWithoutCorrelation);
        pipeline.Flush();

        Assert.Equal(2, output.Count);
        Assert.DoesNotContain(
            output,
            record => record.Message.Contains("logging.storm.suppressed", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Error_with_missing_declared_correlation_fails_open()
    {
        var (pipeline, output, _) = CreatePipeline();
        var exception = new InvalidOperationException("same failure");

        pipeline.Emit(BppLogSeverity.Error, GuardedError, exception: exception);
        pipeline.Emit(BppLogSeverity.Error, GuardedError, exception: exception);
        pipeline.Flush();

        Assert.Equal(2, output.Count);
        Assert.DoesNotContain(
            output,
            record => record.Message.Contains("logging.storm.suppressed", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Matching_long_exception_fingerprints_are_suppressed()
    {
        var (pipeline, output, _) = CreatePipeline();
        var fields = new[] { RequestId.Bind("request-1") };
        var message = new string('m', 700);
        var stack = new string('s', 6000);

        pipeline.Emit(
            BppLogSeverity.Error,
            GuardedError,
            fields,
            new FingerprintException(message, stack)
        );
        pipeline.Emit(
            BppLogSeverity.Error,
            GuardedError,
            fields,
            new FingerprintException(message, stack)
        );
        pipeline.Flush();

        Assert.Equal(2, output.Count);
        Assert.Contains("suppressed_count=1", output[1].Message);
    }

    [Fact]
    public void Exception_messages_differing_beyond_display_budget_do_not_merge()
    {
        var (pipeline, output, _) = CreatePipeline();
        var fields = new[] { RequestId.Bind("request-1") };
        var common = new string('m', 700);

        pipeline.Emit(
            BppLogSeverity.Error,
            GuardedError,
            fields,
            new FingerprintException(common + "secret-A", "same-stack")
        );
        pipeline.Emit(
            BppLogSeverity.Error,
            GuardedError,
            fields,
            new FingerprintException(common + "secret-B", "same-stack")
        );
        pipeline.Flush();

        Assert.Equal(2, output.Count);
        Assert.Equal(output[0].Message, output[1].Message);
        Assert.DoesNotContain("secret-A", output[0].Message);
        Assert.DoesNotContain("secret-B", output[1].Message);
    }

    [Fact]
    public void Exception_stacks_differing_in_hidden_middle_do_not_merge()
    {
        var (pipeline, output, _) = CreatePipeline();
        var fields = new[] { RequestId.Bind("request-1") };
        var stackHead = new string('h', 3000);
        var stackTail = new string('t', 3000);

        pipeline.Emit(
            BppLogSeverity.Error,
            GuardedError,
            fields,
            new FingerprintException("same", stackHead + "A" + stackTail)
        );
        pipeline.Emit(
            BppLogSeverity.Error,
            GuardedError,
            fields,
            new FingerprintException("same", stackHead + "B" + stackTail)
        );
        pipeline.Flush();

        Assert.Equal(2, output.Count);
        Assert.Equal(output[0].Message, output[1].Message);
    }

    [Fact]
    public void Exception_fingerprint_over_total_budget_fails_open()
    {
        var (pipeline, output, _) = CreatePipeline();
        var fields = new[] { RequestId.Bind("request-1") };
        var exception = new FingerprintException(
            new string('m', 600_000),
            new string('s', 600_000)
        );

        pipeline.Emit(BppLogSeverity.Error, GuardedError, fields, exception);
        pipeline.Emit(BppLogSeverity.Error, GuardedError, fields, exception);
        pipeline.Flush();

        Assert.Equal(2, output.Count);
    }

    [Fact]
    public void Throwing_exception_fingerprint_getters_fail_open()
    {
        var (pipeline, output, _) = CreatePipeline();
        var fields = new[] { RequestId.Bind("request-1") };
        var exception = new FingerprintException("hidden", "hidden", throwOnRead: true);

        pipeline.Emit(BppLogSeverity.Error, GuardedError, fields, exception);
        pipeline.Emit(BppLogSeverity.Error, GuardedError, fields, exception);
        pipeline.Flush();

        Assert.Equal(2, output.Count);
        Assert.All(output, record => Assert.DoesNotContain("hidden", record.Message));
    }

    [Fact]
    public void Recovery_flushes_pending_summary_and_resets_source_keys()
    {
        var (pipeline, output, _) = CreatePipeline();
        var fields = new[] { WarningReason.Bind("offline") };
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields);
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields);

        pipeline.RecoverStorm(GuardedWarning);
        pipeline.Emit(BppLogSeverity.Info, RecoveredInfo);
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields);

        Assert.Equal(4, output.Count);
        Assert.Contains("flush_reason=recovered", output[1].Message);
        Assert.Contains("event=logging.test.recovered", output[2].Message);
        Assert.Contains("event=logging.test.degraded", output[3].Message);
    }

    [Fact]
    public void Targeted_recovery_only_resets_the_matching_warning_key()
    {
        var (pipeline, output, _) = CreatePipeline();
        var offline = new[] { WarningReason.Bind("offline") };
        var timeout = new[] { WarningReason.Bind("timeout") };
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, offline);
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, timeout);

        pipeline.RecoverStorm(GuardedWarning, offline);
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, offline);
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, timeout);

        Assert.Equal(3, output.Count);
        Assert.Contains("reason=offline", output[2].Message);

        pipeline.Flush();

        Assert.Equal(4, output.Count);
        Assert.Contains("source_event=logging.test.degraded", output[3].Message);
        Assert.Contains("suppressed_count=1", output[3].Message);
    }

    [Fact]
    public void Shutdown_flush_is_idempotent()
    {
        var (pipeline, output, _) = CreatePipeline();
        var fields = new[] { WarningReason.Bind("offline") };
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields);
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields);

        pipeline.Flush();
        pipeline.Flush();

        Assert.Equal(2, output.Count);
        Assert.Contains("flush_reason=shutdown", output[1].Message);
    }

    [Fact]
    public void Events_after_shutdown_flush_bypass_storm_state()
    {
        var (pipeline, output, _) = CreatePipeline();
        var fields = new[] { WarningReason.Bind("offline") };
        pipeline.Flush();

        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields);
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields);
        pipeline.Flush();

        Assert.Equal(2, output.Count);
        Assert.All(
            output,
            record => Assert.Contains("event=logging.test.degraded", record.Message)
        );
        Assert.Equal(0, pipeline.ActiveStormKeyCount);
    }

    [Fact]
    public void Expired_window_emits_summary_before_a_new_first_occurrence()
    {
        var (pipeline, output, clock) = CreatePipeline();
        var fields = new[] { WarningReason.Bind("offline") };
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields);
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields);

        clock.Advance(TimeSpan.FromSeconds(30));
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields);

        Assert.Equal(3, output.Count);
        Assert.Contains("flush_reason=expired", output[1].Message);
        Assert.Contains("event=logging.test.degraded", output[2].Message);
    }

    [Fact]
    public void Adding_key_257_evicts_an_old_key_and_emits_its_summary()
    {
        var (pipeline, output, _) = CreatePipeline();
        pipeline.Emit(
            BppLogSeverity.Warning,
            GuardedWarning,
            new[] { WarningReason.Bind("reason-0") }
        );
        pipeline.Emit(
            BppLogSeverity.Warning,
            GuardedWarning,
            new[] { WarningReason.Bind("reason-0") }
        );
        for (var index = 1; index <= BppLogPipeline.MaximumActiveStormKeys; index++)
        {
            pipeline.Emit(
                BppLogSeverity.Warning,
                GuardedWarning,
                new[] { WarningReason.Bind("reason-" + index) }
            );
        }

        Assert.Equal(BppLogPipeline.MaximumActiveStormKeys + 2, output.Count);
        Assert.Contains(
            output,
            record =>
                record.Message.Contains("flush_reason=evicted", StringComparison.Ordinal)
                && record.Message.Contains("suppressed_count=1", StringComparison.Ordinal)
        );
        Assert.True(pipeline.ActiveStormKeyCount <= BppLogPipeline.MaximumActiveStormKeys);
    }

    [Fact]
    public void Concurrent_identical_writes_emit_one_source_and_exact_summary_count()
    {
        var output = new ConcurrentQueue<(BppLogSeverity Severity, string Message)>();
        var pipeline = new BppLogPipeline(
            new BppLogEventRenderer(),
            (severity, message) => output.Enqueue((severity, message)),
            () => DateTimeOffset.UnixEpoch
        );
        var fields = new[] { WarningReason.Bind("offline") };

        Parallel.For(0, 1000, _ => pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields));
        pipeline.Flush();

        var records = output.ToArray();
        Assert.Equal(2, records.Length);
        Assert.Contains("suppressed_count=999", records[1].Message);
    }

    [Fact]
    public async Task Sink_is_called_outside_the_storm_state_lock()
    {
        using var sinkEntered = new ManualResetEventSlim();
        using var releaseSink = new ManualResetEventSlim();
        var sinkCalls = 0;
        var pipeline = new BppLogPipeline(
            new BppLogEventRenderer(),
            (_, _) =>
            {
                if (Interlocked.Increment(ref sinkCalls) == 1)
                {
                    sinkEntered.Set();
                    releaseSink.Wait(TimeSpan.FromSeconds(5));
                }
            },
            () => DateTimeOffset.UnixEpoch
        );
        var fields = new[] { WarningReason.Bind("offline") };

        var first = Task.Run(() => pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields));
        Assert.True(sinkEntered.Wait(TimeSpan.FromSeconds(2)));
        var repeated = Task.Run(() =>
            pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields)
        );

        try
        {
            await repeated.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            releaseSink.Set();
        }
        await first.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Concurrent_flush_waits_for_source_before_emitting_summary()
    {
        using var sinkEntered = new ManualResetEventSlim();
        using var releaseSink = new ManualResetEventSlim();
        var output = new ConcurrentQueue<string>();
        var attempts = 0;
        var pipeline = new BppLogPipeline(
            new BppLogEventRenderer(),
            (_, message) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    sinkEntered.Set();
                    releaseSink.Wait(TimeSpan.FromSeconds(5));
                }
                output.Enqueue(message);
            },
            () => DateTimeOffset.UnixEpoch
        );
        var fields = new[] { WarningReason.Bind("offline") };
        var first = Task.Run(() => pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields));
        Assert.True(sinkEntered.Wait(TimeSpan.FromSeconds(2)));
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields);
        var flush = Task.Run(pipeline.Flush);

        try
        {
            await Task.Delay(100);
            Assert.False(flush.IsCompleted);
        }
        finally
        {
            releaseSink.Set();
        }
        await Task.WhenAll(first, flush).WaitAsync(TimeSpan.FromSeconds(2));

        var records = output.ToArray();
        Assert.Equal(2, records.Length);
        Assert.Contains("event=logging.test.degraded", records[0]);
        Assert.Contains("flush_reason=shutdown", records[1]);
    }

    [Fact]
    public async Task Concurrent_flush_skips_summary_when_source_sink_fails()
    {
        using var sinkEntered = new ManualResetEventSlim();
        using var releaseSink = new ManualResetEventSlim();
        var output = new ConcurrentQueue<string>();
        var attempts = 0;
        var pipeline = new BppLogPipeline(
            new BppLogEventRenderer(),
            (_, message) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    sinkEntered.Set();
                    releaseSink.Wait(TimeSpan.FromSeconds(5));
                    throw new InvalidOperationException("listener failed");
                }
                output.Enqueue(message);
            },
            () => DateTimeOffset.UnixEpoch
        );
        var fields = new[] { WarningReason.Bind("offline") };
        var first = Task.Run(() => pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields));
        Assert.True(sinkEntered.Wait(TimeSpan.FromSeconds(2)));
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields);
        var flush = Task.Run(pipeline.Flush);

        try
        {
            await Task.Delay(100);
            Assert.False(flush.IsCompleted);
        }
        finally
        {
            releaseSink.Set();
        }
        await Task.WhenAll(first, flush).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(output);
        Assert.Equal(0, pipeline.ActiveStormKeyCount);
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, fields);
        Assert.Contains("event=logging.test.degraded", Assert.Single(output));
    }

    private static (
        BppLogPipeline Pipeline,
        List<(BppLogSeverity Severity, string Message)> Output,
        FakeClock Clock
    ) CreatePipeline()
    {
        var output = new List<(BppLogSeverity, string)>();
        var clock = new FakeClock();
        var pipeline = new BppLogPipeline(
            new BppLogEventRenderer(),
            (severity, message) => output.Add((severity, message)),
            () => clock.UtcNow
        );
        return (pipeline, output, clock);
    }

    private sealed class FakeClock
    {
        internal DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UnixEpoch;

        internal void Advance(TimeSpan duration) => UtcNow += duration;
    }

    private sealed class FingerprintException : Exception
    {
        private readonly string _message;
        private readonly string? _stack;
        private readonly bool _throwOnRead;

        internal FingerprintException(
            string message,
            string? stack,
            Exception? inner = null,
            bool throwOnRead = false
        )
            : base("placeholder", inner)
        {
            _message = message;
            _stack = stack;
            _throwOnRead = throwOnRead;
        }

        public override string Message =>
            _throwOnRead ? throw new InvalidOperationException("message unavailable") : _message;

        public override string? StackTrace =>
            _throwOnRead ? throw new InvalidOperationException("stack unavailable") : _stack;
    }
}
