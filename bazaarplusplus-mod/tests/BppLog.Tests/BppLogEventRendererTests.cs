using System.Globalization;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class BppLogEventRendererTests
{
    private static readonly BppLogEvent Succeeded = new(
        BppLogFeatureScope.Logger,
        "logging.renderer.succeeded"
    );

    [Fact]
    public void Render_formats_values_invariantly_in_call_order_and_on_one_line()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

            var rendered = Render(
                ("count", 123),
                ("ratio", 12.5m),
                ("enabled", true),
                ("status", RenderState.ReadyAtDawn),
                ("occurred_at", new DateTime(2026, 7, 13, 4, 5, 6, 789, DateTimeKind.Utc)),
                ("missing", null),
                ("quoted", "say \"hi\""),
                ("equation", "a=b"),
                ("control", "a\r\nb\tc\u0001"),
                ("cjk", "中文テスト한글")
            );

            Assert.Equal(
                "[BPP][Logger] event=logging.renderer.succeeded count=123 ratio=12.5 enabled=true "
                    + "status=ready_at_dawn occurred_at=2026-07-13T04:05:06.789Z missing=null "
                    + "quoted=\"say \\\"hi\\\"\" equation=\"a=b\" control=\"a\\r\\nb\\tc\\u0001\" "
                    + "cjk=中文テスト한글",
                rendered
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public void Render_treats_unspecified_times_as_utc_and_offsets_as_utc()
    {
        var rendered = Render(
            ("unspecified_at", new DateTime(2026, 7, 13, 4, 5, 6, DateTimeKind.Unspecified)),
            ("offset_at", new DateTimeOffset(2026, 7, 13, 12, 5, 6, TimeSpan.FromHours(8)))
        );

        Assert.Contains("unspecified_at=2026-07-13T04:05:06.000Z", rendered);
        Assert.Contains("offset_at=2026-07-13T04:05:06.000Z", rendered);
    }

    [Theory]
    [InlineData("BadField", "logging.renderer.succeeded")]
    [InlineData("bad-field", "logging.renderer.succeeded")]
    [InlineData("field", "logging..failed")]
    [InlineData("field", "logging.Bad-Failed")]
    [InlineData("field", "logging.failed")]
    [InlineData("field", "upload.renderer.succeeded")]
    public void Render_fails_safe_for_invalid_identifiers(string fieldName, string eventId)
    {
        var rendered = BppLogEventRenderer.Render(
            new BppLogEvent(BppLogFeatureScope.Logger, eventId),
            [(fieldName, "unsafe\r\nvalue")],
            exception: null
        );

        Assert.Equal(BppLogEventRenderer.FallbackRecord, rendered);
    }

    [Fact]
    public void Render_fails_closed_for_an_unknown_correlation_policy()
    {
        var rendered = Render(("value", "must-not-appear", (BppLogCorrelationPolicy)999));

        Assert.Equal(BppLogEventRenderer.FallbackRecord, rendered);
    }

    [Fact]
    public void Render_escapes_unpaired_surrogates_and_keeps_paired_ones()
    {
        var rendered = Render(("text", "c\ud800d\udc00e😀"));

        Assert.Contains("\\uD800", rendered);
        Assert.Contains("\\uDC00", rendered);
        Assert.Contains("😀", rendered);
    }

    [Fact]
    public void Render_appends_the_exception_type_and_escaped_text_without_truncation()
    {
        var message = new string('m', 10_000) + "\nsecond line";
        var rendered = BppLogEventRenderer.Render(
            Succeeded,
            [("stage", "load")],
            new InvalidOperationException(message)
        );

        Assert.StartsWith(
            "[BPP][Logger] event=logging.renderer.succeeded stage=load "
                + "exception_type=System.InvalidOperationException exception=\"",
            rendered
        );
        Assert.Contains(new string('m', 10_000) + "\\nsecond line", rendered);
        Assert.DoesNotContain('\n', rendered);
    }

    [Fact]
    public void Render_never_throws_when_value_formatting_throws()
    {
        var exception = Record.Exception(() => Render(("value", new ThrowingFormattable())));
        var rendered = Render(("value", new ThrowingFormattable()));

        Assert.Null(exception);
        Assert.EndsWith(" value=<unrenderable>", rendered);
    }

    private static string Render(params BppLogField[] fields) =>
        BppLogEventRenderer.Render(Succeeded, fields, exception: null);

    private enum RenderState
    {
        ReadyAtDawn,
    }

    private sealed class ThrowingFormattable : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) =>
            throw new InvalidOperationException("unsafe formatter");

        public override string ToString() => throw new InvalidOperationException("unsafe value");
    }
}
