using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class BppLogExceptionTests
{
    [Fact]
    public void Render_projects_the_outer_exception_and_at_most_three_inner_exceptions()
    {
        var exception = new InvalidOperationException(
            "outer",
            new ArgumentException(
                "inner-one",
                new FormatException(
                    "inner-two",
                    new IOException("inner-three", new Exception("inner-four"))
                )
            )
        );

        var rendered = Renderer().Render(Define(), [], exception);

        Assert.Contains("exception_type=System.InvalidOperationException", rendered);
        Assert.Contains("exception_hresult=0x80131509", rendered);
        Assert.Contains("exception_message=outer", rendered);
        Assert.Contains("exception_inner_1_type=System.ArgumentException", rendered);
        Assert.Contains("exception_inner_2_type=System.FormatException", rendered);
        Assert.Contains("exception_inner_3_type=System.IO.IOException", rendered);
        Assert.DoesNotContain("exception_inner_4", rendered);
        Assert.DoesNotContain("inner-four", rendered);
    }

    [Fact]
    public void Render_preserves_exception_stack_evidence_when_normal_fields_exhaust_their_budget()
    {
        var fields = Enumerable
            .Range(0, 12)
            .Select(index => new BppLogFieldDefinition(
                index,
                $"detail_{index}",
                BppLogCorrelationPolicy.None,
                BppLogCardinality.Low
            ))
            .ToArray();
        var values = fields.Select(field => field.Bind(new string('x', 300))).ToArray();
        var longMiddle = string.Join("\n", Enumerable.Repeat(new string('m', 200), 80));
        var exception = new ProjectedException(
            "failure",
            "STACK-HEAD\n" + longMiddle + "\nSTACK-TAIL"
        );
        var definition = new BppLogEventDefinition(
            BppLogFeatureScope.Logger,
            "logging.operation.failed",
            fields,
            null
        );

        var rendered = Renderer().Render(definition, values, exception);

        Assert.True(rendered.Length <= BppLogEventRenderer.ExceptionRecordCharacterBudget);
        Assert.Contains("STACK-HEAD", rendered);
        Assert.Contains("STACK-TAIL", rendered);
        Assert.Contains("exception_truncated=true", rendered);
        Assert.Contains("record_truncated=true", rendered);
    }

    [Fact]
    public void Render_preserves_stack_head_and_tail_with_an_in_budget_truncation_marker()
    {
        var longMiddle = string.Join("\n", Enumerable.Repeat(new string('m', 200), 80));
        var exception = new ProjectedException(
            "failure",
            "STACK-HEAD\n" + longMiddle + "\nSTACK-TAIL"
        );

        var rendered = Renderer().Render(Define(), [], exception);

        Assert.True(rendered.Length <= BppLogEventRenderer.ExceptionRecordCharacterBudget);
        Assert.Contains("STACK-HEAD", rendered);
        Assert.Contains("STACK-TAIL", rendered);
        Assert.Contains("exception_truncated=true", rendered);
    }

    [Fact]
    public void Render_preserves_the_tail_of_a_long_single_line_stack()
    {
        var exception = new ProjectedException(
            "failure",
            "STACK-HEAD " + new string('m', 20000) + " STACK-TAIL"
        );

        var rendered = Renderer().Render(Define(), [], exception);

        Assert.Contains("STACK-HEAD", rendered);
        Assert.Contains("STACK-TAIL", rendered);
        Assert.Contains("exception_truncated=true", rendered);
    }

    [Fact]
    public void Render_never_throws_when_exception_projection_accessors_throw()
    {
        var thrown = Record.Exception(() =>
            Renderer().Render(Define(), [], new HostileException())
        );
        var rendered = Renderer().Render(Define(), [], new HostileException());

        Assert.Null(thrown);
        Assert.Contains("exception_type=", rendered);
        Assert.Contains("exception_message=<unavailable>", rendered);
        Assert.Contains("exception_stack=<unavailable>", rendered);
    }

    private static BppLogEventRenderer Renderer() => new();

    private static BppLogEventDefinition Define() =>
        new(BppLogFeatureScope.Logger, "logging.operation.failed", [], null);

    private class ProjectedException(string message, string stackTrace) : Exception(message)
    {
        public override string? StackTrace => stackTrace;
    }

    private sealed class HostileException : Exception
    {
        public override string Message => throw new InvalidOperationException("unsafe message");

        public override string? StackTrace => throw new InvalidOperationException("unsafe stack");
    }
}
