using System.Globalization;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class BppLogEventRendererTests
{
    [Fact]
    public void Render_formats_schema_ordered_values_invariantly_and_on_one_line()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

            var count = Field(0, "count");
            var ratio = Field(1, "ratio");
            var enabled = Field(2, "enabled");
            var status = Field(3, "status");
            var occurredAt = Field(4, "occurred_at");
            var missing = Field(5, "missing");
            var quoted = Field(6, "quoted");
            var equation = Field(7, "equation");
            var control = Field(8, "control");
            var cjk = Field(9, "cjk");
            var definition = Define(
                count,
                ratio,
                enabled,
                status,
                occurredAt,
                missing,
                quoted,
                equation,
                control,
                cjk
            );

            var rendered = Renderer()
                .Render(
                    definition,
                    cjk.Bind("中文テスト한글"),
                    control.Bind("a\r\nb\tc\u0001"),
                    equation.Bind("a=b"),
                    quoted.Bind("say \"hi\""),
                    missing.Bind(null),
                    occurredAt.Bind(new DateTime(2026, 7, 13, 4, 5, 6, 789, DateTimeKind.Utc)),
                    status.Bind(RenderState.ReadyAtDawn),
                    enabled.Bind(true),
                    ratio.Bind(12.5m),
                    count.Bind(123)
                );

            Assert.Equal(
                "[BPP][Logger] event=logging.renderer.succeeded count=123 ratio=12.5 enabled=true "
                    + "status=ready_at_dawn occurred_at=2026-07-13T04:05:06.789Z missing=null "
                    + "quoted=\"say \\\"hi\\\"\" equation=\"a=b\" control=\"a\\r\\nb\\tc\\u0001\" "
                    + "cjk=中文テスト한글",
                rendered
            );
            Assert.DoesNotContain('\r', rendered);
            Assert.DoesNotContain('\n', rendered);
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
        var unspecified = Field(0, "unspecified_at");
        var offset = Field(1, "offset_at");
        var rendered = Renderer()
            .Render(
                Define(unspecified, offset),
                unspecified.Bind(new DateTime(2026, 7, 13, 4, 5, 6, DateTimeKind.Unspecified)),
                offset.Bind(new DateTimeOffset(2026, 7, 13, 12, 5, 6, TimeSpan.FromHours(8)))
            );

        Assert.Contains("unspecified_at=2026-07-13T04:05:06.000Z", rendered);
        Assert.Contains("offset_at=2026-07-13T04:05:06.000Z", rendered);
    }

    [Fact]
    public void Render_budgets_fields_after_escaping_and_budgets_the_final_record()
    {
        var fields = Enumerable
            .Range(0, 12)
            .Select(index => Field(index, $"field_{index}"))
            .ToArray();
        var values = fields
            .Select(field => field.Bind(new string('\u0001', 300) + "secret-tail"))
            .ToArray();

        var rendered = Renderer().Render(Define(fields), values);

        Assert.True(rendered.Length <= BppLogEventRenderer.RecordCharacterBudget);
        Assert.Contains("field_truncated=true", rendered);
        Assert.Contains("record_truncated=true", rendered);
        Assert.DoesNotContain("secret-tail", rendered);
        Assert.DoesNotContain('\u0001', rendered);
    }

    [Fact]
    public void Render_budgets_long_ascii_fields()
    {
        var text = Field(0, "text");

        var rendered = Renderer()
            .Render(Define(text), text.Bind(new string('a', 300) + "secret-tail"));

        Assert.True(rendered.Length <= BppLogEventRenderer.RecordCharacterBudget);
        Assert.Contains("field_truncated=true", rendered);
        Assert.DoesNotContain("secret-tail", rendered);
    }

    [Fact]
    public void Render_uses_declared_field_order_even_when_the_schema_list_is_out_of_order()
    {
        var first = Field(0, "first");
        var second = Field(1, "second");

        var rendered = Renderer()
            .Render(Define(second, first), second.Bind("two"), first.Bind("one"));

        Assert.EndsWith(" first=one second=two", rendered);
    }

    [Theory]
    [InlineData("BadField", "logging.renderer.succeeded")]
    [InlineData("bad-field", "logging.renderer.succeeded")]
    [InlineData("field", "logging..failed")]
    [InlineData("field", "logging.Bad-Failed")]
    [InlineData("field", "logging.failed")]
    public void Render_fails_safe_for_invalid_schema_identifiers(string fieldName, string eventId)
    {
        var field = Field(0, fieldName);
        var definition = new BppLogEventDefinition(
            BppLogFeatureScope.Logger,
            eventId,
            [field],
            null
        );

        var rendered = Renderer().Render(definition, field.Bind("unsafe\r\nvalue"));

        Assert.Equal("[BPP][Logger] event=logging.render.failed", rendered);
    }

    [Theory]
    [InlineData(999, 0)]
    [InlineData(0, 999)]
    public void Render_fails_closed_for_invalid_field_governance(int correlation, int cardinality)
    {
        var field = Field(
            0,
            "value",
            (BppLogCorrelationPolicy)correlation,
            (BppLogCardinality)cardinality
        );

        var rendered = Renderer().Render(Define(field), field.Bind("must-not-appear"));

        Assert.Equal("[BPP][Logger] event=logging.render.failed", rendered);
    }

    [Fact]
    public void Render_escapes_unicode_line_separators_and_unpaired_surrogates()
    {
        var text = Field(0, "text");
        var rendered = Renderer()
            .Render(Define(text), text.Bind("a\u2028b\u2029c\ud800d\udc00e😀"));

        Assert.Contains("\\u2028", rendered);
        Assert.Contains("\\u2029", rendered);
        Assert.Contains("\\uD800", rendered);
        Assert.Contains("\\uDC00", rendered);
        Assert.Contains("😀", rendered);
        Assert.DoesNotContain('\u2028', rendered);
        Assert.DoesNotContain('\u2029', rendered);
    }

    [Fact]
    public void Render_ignores_values_not_declared_by_the_event_schema()
    {
        var approved = Field(0, "approved");
        var undeclared = Field(0, "token");

        var rendered = Renderer()
            .Render(Define(approved), undeclared.Bind("must-not-appear"), approved.Bind("ok"));

        Assert.EndsWith(" approved=ok", rendered);
        Assert.DoesNotContain("token", rendered);
        Assert.DoesNotContain("must-not-appear", rendered);
    }

    [Fact]
    public void Render_never_throws_when_public_value_formatting_throws()
    {
        var value = Field(0, "value");

        var exception = Record.Exception(() =>
            Renderer().Render(Define(value), value.Bind(new ThrowingFormattable()))
        );
        var rendered = Renderer().Render(Define(value), value.Bind(new ThrowingFormattable()));

        Assert.Null(exception);
        Assert.EndsWith(" value=<unrenderable>", rendered);
    }

    private static BppLogEventRenderer Renderer() => new();

    private static BppLogEventDefinition Define(params BppLogFieldDefinition[] fields) =>
        new(
            BppLogFeatureScope.Logger,
            eventId: "logging.renderer.succeeded",
            fields,
            stormPolicy: null
        );

    private static BppLogFieldDefinition Field(
        int order,
        string name,
        BppLogCorrelationPolicy correlation = BppLogCorrelationPolicy.None,
        BppLogCardinality cardinality = BppLogCardinality.Low
    ) => new(order, name, correlation, cardinality);

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
