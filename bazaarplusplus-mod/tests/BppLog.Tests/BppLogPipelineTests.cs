using System.Collections.Concurrent;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class BppLogPipelineTests
{
    private static readonly BppLogEvent GuardedWarning = new(
        BppLogFeatureScope.Logger,
        "logging.test.degraded",
        storm: ["reason"]
    );
    private static readonly BppLogEvent GuardedError = new(
        BppLogFeatureScope.Logger,
        "logging.test.failed",
        storm: []
    );
    private static readonly BppLogEvent UnguardedInfo = new(
        BppLogFeatureScope.Logger,
        "logging.test.succeeded"
    );
    private static readonly BppLogEvent RecoveredInfo = new(
        BppLogFeatureScope.Logger,
        "logging.test.recovered"
    );

    [Fact]
    public void First_guarded_occurrence_is_emitted_immediately()
    {
        var (pipeline, output, _) = CreatePipeline();

        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, new[] { WarningReason("offline") });

        var record = Assert.Single(output);
        Assert.Equal(BppLogSeverity.Warning, record.Severity);
        Assert.Contains("event=logging.test.degraded", record.Message);
        Assert.Contains("reason=offline", record.Message);
    }

    [Fact]
    public void Guarded_repeats_are_summarized_on_shutdown_flush()
    {
        var (pipeline, output, _) = CreatePipeline();
        var fields = new[] { WarningReason("offline") };

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

        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, new[] { WarningReason("offline") });
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, new[] { WarningReason("timeout") });

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
            new[] { RequestId("12345678-first") },
            exception
        );
        pipeline.Emit(
            BppLogSeverity.Error,
            GuardedError,
            new[] { RequestId("12345678-second") },
            exception
        );

        Assert.Equal(2, output.Count);
        Assert.All(output, record => Assert.Contains("request_id=12345678", record.Message));
    }

    [Fact]
    public void Matching_error_correlation_and_exception_type_are_suppressed()
    {
        var (pipeline, output, _) = CreatePipeline();
        var fields = new[] { RequestId("request-1") };
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
        var fields = new[] { RequestId("request-1") };

        pipeline.Emit(BppLogSeverity.Error, GuardedError, fields);
        pipeline.Emit(BppLogSeverity.Error, GuardedError, fields);

        Assert.Equal(2, output.Count);
    }

    [Fact]
    public void Error_keys_merge_messages_of_one_exception_type_and_split_types()
    {
        var (pipeline, output, _) = CreatePipeline();
        var fields = new[] { RequestId("request-1") };

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
        pipeline.Emit(BppLogSeverity.Error, GuardedError, fields, new TimeoutException("late"));
        pipeline.Flush();

        Assert.Equal(3, output.Count);
        Assert.Contains("exception_type=System.InvalidOperationException", output[0].Message);
        Assert.Contains("exception_type=System.TimeoutException", output[1].Message);
        Assert.Contains("suppressed_count=1", output[2].Message);
    }

    [Fact]
    public void Error_without_correlation_fields_keys_on_the_exception_type()
    {
        var (pipeline, output, _) = CreatePipeline();

        pipeline.Emit(BppLogSeverity.Error, GuardedError, exception: new IOException("disk"));
        pipeline.Emit(BppLogSeverity.Error, GuardedError, exception: new IOException("disk"));
        pipeline.Flush();

        Assert.Equal(2, output.Count);
        Assert.Contains("suppressed_count=1", output[1].Message);
    }

    [Fact]
    public void Error_with_a_null_correlation_value_fails_open()
    {
        var (pipeline, output, _) = CreatePipeline();
        var fields = new BppLogField[] { ("request_id", null, BppLogCorrelationPolicy.Short) };
        var exception = new InvalidOperationException("same failure");

        pipeline.Emit(BppLogSeverity.Error, GuardedError, fields, exception);
        pipeline.Emit(BppLogSeverity.Error, GuardedError, fields, exception);

        Assert.Equal(2, output.Count);
    }

    [Fact]
    public void Recovery_flushes_pending_summary_and_resets_source_keys()
    {
        var (pipeline, output, _) = CreatePipeline();
        var fields = new[] { WarningReason("offline") };
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
        var offline = new[] { WarningReason("offline") };
        var timeout = new[] { WarningReason("timeout") };
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
        var fields = new[] { WarningReason("offline") };
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
        var fields = new[] { WarningReason("offline") };
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
        var fields = new[] { WarningReason("offline") };
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
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, new[] { WarningReason("reason-0") });
        pipeline.Emit(BppLogSeverity.Warning, GuardedWarning, new[] { WarningReason("reason-0") });
        for (var index = 1; index <= BppLogPipeline.MaximumActiveStormKeys; index++)
        {
            pipeline.Emit(
                BppLogSeverity.Warning,
                GuardedWarning,
                new[] { WarningReason("reason-" + index) }
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
            (severity, message) => output.Enqueue((severity, message)),
            () => DateTimeOffset.UnixEpoch
        );
        var fields = new[] { WarningReason("offline") };

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
        var fields = new[] { WarningReason("offline") };

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
        var fields = new[] { WarningReason("offline") };
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
        var fields = new[] { WarningReason("offline") };
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

    private static BppLogField WarningReason(string value) => ("reason", value);

    private static BppLogField RequestId(string value) =>
        ("request_id", value, BppLogCorrelationPolicy.Short);

    private static (
        BppLogPipeline Pipeline,
        List<(BppLogSeverity Severity, string Message)> Output,
        FakeClock Clock
    ) CreatePipeline()
    {
        var output = new List<(BppLogSeverity, string)>();
        var clock = new FakeClock();
        var pipeline = new BppLogPipeline(
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
}
