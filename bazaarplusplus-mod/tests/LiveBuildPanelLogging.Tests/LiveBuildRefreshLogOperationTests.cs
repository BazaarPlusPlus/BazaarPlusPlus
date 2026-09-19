#nullable enable
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Infrastructure;
using Xunit;

namespace LiveBuildPanelLogging.Tests;

public sealed class LiveBuildRefreshLogOperationTests : IDisposable
{
    public LiveBuildRefreshLogOperationTests() => BppLog.Reset();

    public void Dispose() => BppLog.Reset();

    [Fact]
    public void Updated_success_is_the_single_request_terminal()
    {
        AssertSuccessIsSingleTerminal(LiveBuildRefreshResultCode.Updated);
    }

    [Fact]
    public void No_change_success_is_the_single_request_terminal()
    {
        AssertSuccessIsSingleTerminal(LiveBuildRefreshResultCode.NoChange);
    }

    private static void AssertSuccessIsSingleTerminal(LiveBuildRefreshResultCode result)
    {
        var requestId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var operation = new LiveBuildRefreshLogOperation(requestId);

        Assert.True(operation.TrySucceed(result));
        Assert.False(operation.TryFail(LiveBuildRefreshFailureReasonCode.RefreshException));

        var captured = Assert.Single(BppLog.Events);
        Assert.Equal("Info", captured.Severity);
        Assert.Equal("live_build_panel.refresh.succeeded", captured.Definition.EventId);
        Assert.Equal(requestId, Value(captured, "request_id"));
        Assert.Equal(result, Value(captured, "result"));
    }

    [Fact]
    public void Failure_projects_exception_without_binding_remote_error_text()
    {
        var exception = new InvalidOperationException("server response body secret");
        var operation = new LiveBuildRefreshLogOperation(Guid.NewGuid());

        Assert.True(
            operation.TryFail(LiveBuildRefreshFailureReasonCode.RemoteRequestFailed, exception)
        );

        var captured = Assert.Single(BppLog.Events);
        Assert.Equal("Error", captured.Severity);
        Assert.Equal("live_build_panel.refresh.failed", captured.Definition.EventId);
        Assert.Equal(
            LiveBuildRefreshFailureReasonCode.RemoteRequestFailed,
            Value(captured, "reason_code")
        );
        Assert.Same(exception, captured.Exception);
        Assert.DoesNotContain(
            captured.Values,
            value =>
                value.Value is string text
                && text.Contains("server response", StringComparison.Ordinal)
        );
    }

    private static object? Value(CapturedBppLogEvent captured, string fieldName) =>
        captured.Values.Single(value => value.Field.Name == fieldName).Value;
}
