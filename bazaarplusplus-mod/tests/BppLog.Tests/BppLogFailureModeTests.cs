using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace BazaarPlusPlus.Tests;

/// <summary>
/// The ways the call-site logging API can fail its readers. Each failure mode has one fact below.
/// <list type="number">
/// <item>A <c>Short</c> field leaks: it renders more than 8 characters, or the full value
/// appears anywhere in the line.</item>
/// <item>A <c>Hash</c> field leaks: it renders anything but 12 lowercase hex digits, or the
/// value appears anywhere in the line.</item>
/// <item>The default policy is wrong: a field without a policy is silently redacted, or a field
/// declared <c>Hash</c> is silently passed through.</item>
/// <item>One event spans lines: a newline, a quote, or U+2028 in a value breaks the
/// one-record-per-line shape that readers of <c>LogOutput.log</c> rely on.</item>
/// <item>A storm key merges on a redacted field: records keyed by a <c>Short</c> or
/// <c>Hash</c> value collapse distinct correlation ids. Such a key must not suppress.</item>
/// <item>Storm suppression floods or loses counts: the same event and key inside the window
/// writes more than one line, or the shutdown flush omits the suppressed count.</item>
/// </list>
/// </summary>
public sealed class BppLogFailureModeTests
{
    private const string SecretId = "0123456789abcdef-secret-correlation";

    private static readonly BppLogEvent Degraded = new(
        BppLogFeatureScope.Upload,
        "upload.failure_mode.degraded",
        storm: ["reason_code"]
    );

    [Fact]
    public void Short_field_renders_at_most_eight_characters_and_never_the_value()
    {
        var line = Render(("run_id", SecretId, BppLogCorrelationPolicy.Short));

        Assert.EndsWith(" run_id=01234567", line);
        Assert.DoesNotContain(SecretId, line, StringComparison.Ordinal);
    }

    [Fact]
    public void Hash_field_renders_only_twelve_hex_digits_and_never_the_value()
    {
        var line = Render(("binding_path", SecretId, BppLogCorrelationPolicy.Hash));

        var rendered = line.Substring(line.IndexOf("binding_path=", StringComparison.Ordinal) + 13);
        Assert.Matches("^[0-9a-f]{12}$", rendered);
        Assert.DoesNotContain(SecretId, line, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_policy_renders_verbatim_and_a_declared_hash_is_not_passed_through()
    {
        var line = Render(
            ("reason_code", "write_failed"),
            ("path_hash", "/private/path", BppLogCorrelationPolicy.Hash)
        );

        Assert.Contains(" reason_code=write_failed ", line, StringComparison.Ordinal);
        Assert.DoesNotContain("/private/path", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Newlines_quotes_and_line_separators_stay_on_one_line()
    {
        var line = Render(("detail", "first\nsecond \"quoted\"\u2028third\u2029fourth\r"));

        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.DoesNotContain('\u2028', line);
        Assert.DoesNotContain('\u2029', line);
        Assert.EndsWith(
            " detail=\"first\\nsecond \\\"quoted\\\"\\u2028third\\u2029fourth\\r\"",
            line
        );
    }

    [Theory]
    [InlineData("short")]
    [InlineData("hash")]
    [InlineData("full")]
    public void Storm_key_on_a_correlation_field_fails_open(string policyName)
    {
        var policy = Enum.Parse<BppLogCorrelationPolicy>(policyName, ignoreCase: true);
        var output = new List<string>();
        var pipeline = new BppLogPipeline(
            (_, line) => output.Add(line),
            () => DateTimeOffset.UnixEpoch
        );
        var keyedOnRedacted = new BppLogEvent(
            BppLogFeatureScope.Upload,
            "upload.failure_mode.degraded",
            storm: ["run_id"]
        );

        pipeline.Emit(BppLogSeverity.Warning, keyedOnRedacted, [("run_id", "run-a-1", policy)]);
        pipeline.Emit(BppLogSeverity.Warning, keyedOnRedacted, [("run_id", "run-a-2", policy)]);
        pipeline.Flush();

        Assert.Equal(2, output.Count);
        Assert.Equal(0, pipeline.ActiveStormKeyCount);
    }

    [Fact]
    public void Same_event_and_key_in_the_window_writes_once_and_flush_reports_the_count()
    {
        var output = new List<string>();
        var pipeline = new BppLogPipeline(
            (_, line) => output.Add(line),
            () => DateTimeOffset.UnixEpoch
        );

        for (var index = 0; index < 5; index++)
            pipeline.Emit(BppLogSeverity.Warning, Degraded, [("reason_code", "offline")]);
        pipeline.Emit(BppLogSeverity.Warning, Degraded, [("reason_code", "timeout")]);
        pipeline.Flush();

        Assert.Equal(3, output.Count);
        Assert.Equal(
            "[BPP][Upload] event=upload.failure_mode.degraded reason_code=offline",
            output[0]
        );
        Assert.Equal(
            "[BPP][Upload] event=upload.failure_mode.degraded reason_code=timeout",
            output[1]
        );
        Assert.Equal(
            "[BPP][Logger] event=logging.storm.suppressed "
                + "source_event=upload.failure_mode.degraded suppressed_count=4 "
                + "window_ms=30000 flush_reason=shutdown",
            output[2]
        );
    }

    private static string Render(params BppLogField[] fields) =>
        BppLogEventRenderer.Render(Degraded, fields, exception: null);
}
