using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class BppLogPipelineReliabilityTests
{
    private static readonly BppLogFieldDefinition ValueField = new(
        0,
        "value",
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );
    private static readonly BppLogEventDefinition Event = new(
        BppLogFeatureScope.Logger,
        "logging.reliability.succeeded",
        new[] { ValueField }
    );
    private static readonly BppLogEventDefinition GuardedEvent = new(
        BppLogFeatureScope.Logger,
        "logging.reliability.degraded",
        new[] { ValueField },
        new BppLogStormPolicy(new[] { ValueField })
    );

    [Fact]
    public void Emit_before_install_is_non_throwing_and_drops_the_record()
    {
        var emitter = new BppLogEmitter();

        var exception = Record.Exception(() =>
            emitter.Emit(BppLogSeverity.Info, Event, new[] { ValueField.Bind(new ThrowingValue()) })
        );

        Assert.Null(exception);
    }

    [Fact]
    public void Install_before_first_emit_delivers_the_first_record()
    {
        var emitter = new BppLogEmitter();
        var output = new List<string>();
        emitter.Install(CreatePipeline((_, message) => output.Add(message)));

        emitter.Emit(BppLogSeverity.Info, Event, new[] { ValueField.Bind("loaded") });

        Assert.Contains("event=logging.reliability.succeeded", Assert.Single(output));
    }

    [Fact]
    public void Repeated_install_flushes_old_storm_state_without_duplicate_delivery()
    {
        var emitter = new BppLogEmitter();
        var output = new List<string>();
        var fields = new[] { ValueField.Bind("offline") };
        emitter.Install(CreatePipeline((_, message) => output.Add(message)));
        emitter.Emit(BppLogSeverity.Warning, GuardedEvent, fields);
        emitter.Emit(BppLogSeverity.Warning, GuardedEvent, fields);

        emitter.Install(CreatePipeline((_, message) => output.Add(message)));
        emitter.Emit(BppLogSeverity.Info, Event, new[] { ValueField.Bind("loaded") });

        Assert.Equal(3, output.Count);
        Assert.Contains("event=logging.reliability.degraded", output[0]);
        Assert.Contains("flush_reason=shutdown", output[1]);
        Assert.Contains("event=logging.reliability.succeeded", output[2]);
    }

    [Fact]
    public void Sink_exception_does_not_escape_or_disable_later_records()
    {
        var attempts = 0;
        var delivered = new List<string>();
        var pipeline = CreatePipeline(
            (_, message) =>
            {
                if (++attempts == 1)
                    throw new InvalidOperationException("listener failed");
                delivered.Add(message);
            }
        );

        var first = Record.Exception(() => pipeline.Emit(BppLogSeverity.Info, Event));
        var second = Record.Exception(() => pipeline.Emit(BppLogSeverity.Info, Event));

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(2, attempts);
        Assert.Single(delivered);
    }

    [Fact]
    public void Guarded_sink_failure_does_not_poison_the_storm_key()
    {
        var attempts = 0;
        var output = new List<string>();
        var pipeline = CreatePipeline(
            (_, message) =>
            {
                attempts++;
                if (attempts == 1)
                    throw new InvalidOperationException("listener failed");
                output.Add(message);
            }
        );
        var fields = new[] { ValueField.Bind("offline") };

        pipeline.Emit(BppLogSeverity.Warning, GuardedEvent, fields);
        Assert.Equal(0, pipeline.ActiveStormKeyCount);
        pipeline.Emit(BppLogSeverity.Warning, GuardedEvent, fields);
        pipeline.Emit(BppLogSeverity.Warning, GuardedEvent, fields);
        pipeline.Flush();

        Assert.Equal(3, attempts);
        Assert.Equal(2, output.Count);
        Assert.Contains("event=logging.reliability.degraded", output[0]);
        Assert.Contains("suppressed_count=1", output[1]);
    }

    [Fact]
    public void Reentrant_sink_is_not_called_recursively()
    {
        BppLogPipeline? pipeline = null;
        var attempts = 0;
        pipeline = CreatePipeline(
            (_, _) =>
            {
                attempts++;
                pipeline!.Emit(BppLogSeverity.Info, Event);
            }
        );

        var exception = Record.Exception(() => pipeline!.Emit(BppLogSeverity.Info, Event));

        Assert.Null(exception);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void Throwing_storm_key_ToString_fails_open_and_emits_safe_records()
    {
        var output = new List<string>();
        var pipeline = CreatePipeline((_, message) => output.Add(message));
        var fields = new[] { ValueField.Bind(new ThrowingValue()) };

        var first = Record.Exception(() =>
            pipeline.Emit(BppLogSeverity.Warning, GuardedEvent, fields)
        );
        var second = Record.Exception(() =>
            pipeline.Emit(BppLogSeverity.Warning, GuardedEvent, fields)
        );

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(2, output.Count);
        Assert.All(output, message => Assert.Contains("value=<unrenderable>", message));
    }

    [Fact]
    public void Malformed_definition_renders_one_safe_fallback_record()
    {
        var output = new List<string>();
        var pipeline = CreatePipeline((_, message) => output.Add(message));
        var malformed = new BppLogEventDefinition(
            BppLogFeatureScope.Logger,
            "bad",
            Array.Empty<BppLogFieldDefinition>()
        );

        var exception = Record.Exception(() => pipeline.Emit(BppLogSeverity.Info, malformed));

        Assert.Null(exception);
        Assert.Equal("[BPP][Logger] event=logging.render.failed", Assert.Single(output));
    }

    [Fact]
    public void Debug_factory_is_evaluated_and_emitted_only_in_debug_builds()
    {
        var emitter = new BppLogEmitter();
        var output = new List<string>();
        var evaluations = 0;
        emitter.Install(CreatePipeline((_, message) => output.Add(message)));

        emitter.Debug(
            Event,
            () =>
            {
                evaluations++;
                return new[] { ValueField.Bind("expensive") };
            }
        );

#if DEBUG
        Assert.Equal(1, evaluations);
        Assert.Contains("event=logging.reliability.succeeded", Assert.Single(output));
#else
        Assert.Equal(0, evaluations);
        Assert.Empty(output);
#endif
    }

    [Fact]
    public void Debug_factory_is_not_evaluated_without_an_installation()
    {
        var emitter = new BppLogEmitter();
        var evaluations = 0;

        emitter.Debug(
            Event,
            () =>
            {
                evaluations++;
                return Array.Empty<BppLogFieldValue>();
            }
        );

        Assert.Equal(0, evaluations);
    }

    [Fact]
    public void Debug_exception_factory_is_evaluated_only_in_debug_builds()
    {
        var emitter = new BppLogEmitter();
        var output = new List<string>();
        var evaluations = 0;
        emitter.Install(CreatePipeline((_, message) => output.Add(message)));

        emitter.Debug(
            Event,
            new InvalidOperationException("bounded diagnostic"),
            () =>
            {
                evaluations++;
                return new[] { ValueField.Bind("expensive") };
            }
        );

#if DEBUG
        Assert.Equal(1, evaluations);
        Assert.Contains("exception_type=", Assert.Single(output));
#else
        Assert.Equal(0, evaluations);
        Assert.Empty(output);
#endif
    }

    private static BppLogPipeline CreatePipeline(Action<BppLogSeverity, string> sink) =>
        new(new BppLogEventRenderer(), sink, () => DateTimeOffset.UnixEpoch);

    private sealed class ThrowingValue
    {
        public override string ToString() => throw new InvalidOperationException("secret-value");
    }
}
