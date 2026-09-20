#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.AccountLink;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.ModApi.Clients;
using Xunit;

namespace HistoryPanelAccountLinkLogging.Tests;

public sealed class AccountLinkLogRequestTests
{
    private const string RequestId = "01JABCDEFGHJKMNPQRSTVWXYZ";

    [Fact]
    public void Redeem_success_emits_one_structured_terminal()
    {
        BppLog.Reset();
        var request = new AccountLinkLogRequest(RequestId, AccountLinkMethod.Redeem);

        request.Succeeded();
        request.Failed(AccountLinkReason.UnexpectedException);

        var captured = Assert.Single(BppLog.Events);
        Assert.Equal("Info", captured.Severity);
        Assert.Equal("history_panel.account_link.succeeded", captured.Definition.EventId);
        AssertFields(captured, ("request_id", RequestId), ("method", AccountLinkMethod.Redeem));
    }

    [Fact]
    public void Manual_success_uses_the_shared_success_vocabulary()
    {
        BppLog.Reset();
        var request = new AccountLinkLogRequest(RequestId, AccountLinkMethod.Manual);

        request.Succeeded();

        var captured = Assert.Single(BppLog.Events);
        Assert.Equal("Info", captured.Severity);
        Assert.Equal("history_panel.account_link.succeeded", captured.Definition.EventId);
        AssertFields(captured, ("request_id", RequestId), ("method", AccountLinkMethod.Manual));
    }

    [Theory]
    [InlineData(BazaarDbLinkOutcome.InvalidOrExpired, (int)AccountLinkReason.InvalidOrExpired)]
    [InlineData(BazaarDbLinkOutcome.AlreadyLinked, (int)AccountLinkReason.AlreadyLinked)]
    [InlineData(BazaarDbLinkOutcome.MissingFields, (int)AccountLinkReason.MissingFields)]
    [InlineData(BazaarDbLinkOutcome.ServerError, (int)AccountLinkReason.ServerError)]
    [InlineData(BazaarDbLinkOutcome.Transport, (int)AccountLinkReason.Transport)]
    public void Non_success_redeem_outcomes_emit_one_normalized_error(
        BazaarDbLinkOutcome outcome,
        int expectedReasonValue
    )
    {
        var expectedReason = (AccountLinkReason)expectedReasonValue;
        BppLog.Reset();
        var request = new AccountLinkLogRequest(RequestId, AccountLinkMethod.Redeem);

        request.Failed(outcome);

        var captured = Assert.Single(BppLog.Events);
        Assert.Equal("Error", captured.Severity);
        Assert.Equal("history_panel.account_link.failed", captured.Definition.EventId);
        AssertFields(
            captured,
            ("request_id", RequestId),
            ("method", AccountLinkMethod.Redeem),
            ("reason_code", expectedReason)
        );
    }

    [Theory]
    [InlineData((int)AccountLinkReason.SignedOut)]
    [InlineData((int)AccountLinkReason.EmptyCode)]
    [InlineData((int)AccountLinkReason.ClientUnavailable)]
    [InlineData((int)AccountLinkReason.AccountChanged)]
    public void Validation_and_account_switch_skips_emit_one_debug_terminal(int reasonValue)
    {
        var reason = (AccountLinkReason)reasonValue;
        BppLog.Reset();
        var request = new AccountLinkLogRequest(RequestId, AccountLinkMethod.Redeem);

        request.Skipped(reason);
        request.Succeeded();

        var captured = Assert.Single(BppLog.Events);
        Assert.Equal("Debug", captured.Severity);
        Assert.Equal("history_panel.account_link.skipped", captured.Definition.EventId);
        AssertFields(captured, ("request_id", RequestId), ("reason_code", reason));
    }

    [Fact]
    public void Abandoned_session_request_is_silent_and_terminal()
    {
        BppLog.Reset();
        var request = new AccountLinkLogRequest(RequestId, AccountLinkMethod.Redeem);

        request.Abandon();
        request.Failed(AccountLinkReason.RequestTimeout);

        Assert.Empty(BppLog.Events);
    }

    [Theory]
    [InlineData((int)AccountLinkReason.RequestTimeout)]
    [InlineData((int)AccountLinkReason.UnexpectedException)]
    public void Exception_failure_carries_the_exception_separately_from_closed_fields(
        int reasonValue
    )
    {
        var reason = (AccountLinkReason)reasonValue;
        var exception = new InvalidOperationException("safe diagnostic");
        BppLog.Reset();
        var request = new AccountLinkLogRequest(RequestId, AccountLinkMethod.Redeem);

        request.Failed(reason, exception);

        var captured = Assert.Single(BppLog.Events);
        Assert.Same(exception, captured.Exception);
        AssertFields(
            captured,
            ("request_id", RequestId),
            ("method", AccountLinkMethod.Redeem),
            ("reason_code", reason)
        );
    }

    [Fact]
    public void Raw_server_error_account_code_token_and_body_never_enter_the_event()
    {
        const string privateText =
            "account-secret link-code-secret token-secret response-body-secret";
        var result = BazaarDbLinkResult.From(BazaarDbLinkOutcome.ServerError, 500, privateText);
        BppLog.Reset();
        var request = new AccountLinkLogRequest(RequestId, AccountLinkMethod.Redeem);

        request.Failed(result.Outcome);

        var captured = Assert.Single(BppLog.Events);
        var rendered = new BppLogEventRenderer().Render(
            captured.Definition,
            captured.Values,
            captured.Exception
        );
        Assert.DoesNotContain(privateText, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("account-secret", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("link-code-secret", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("token-secret", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("response-body-secret", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Event_schemas_are_closed_to_the_manifest_fields()
    {
        Assert.Equal(
            new[] { "request_id", "method", "reason_code" },
            HistoryPanelAccountLinkLogEvents.Failed.Fields.Select(field => field.Name)
        );
        Assert.Equal(
            new[] { "request_id", "method" },
            HistoryPanelAccountLinkLogEvents.Succeeded.Fields.Select(field => field.Name)
        );
        Assert.Equal(
            new[] { "request_id", "reason_code" },
            HistoryPanelAccountLinkLogEvents.Skipped.Fields.Select(field => field.Name)
        );
        AssertField(
            HistoryPanelAccountLinkLogEvents.RequestId,
            BppLogCardinality.High,
            BppLogCorrelationPolicy.Short
        );
        AssertField(
            HistoryPanelAccountLinkLogEvents.Method,
            BppLogCardinality.Low,
            BppLogCorrelationPolicy.None
        );
        AssertField(
            HistoryPanelAccountLinkLogEvents.FailureReasonCode,
            BppLogCardinality.Low,
            BppLogCorrelationPolicy.None
        );
        AssertField(
            HistoryPanelAccountLinkLogEvents.SkippedReasonCode,
            BppLogCardinality.Low,
            BppLogCorrelationPolicy.None
        );
    }

    private static void AssertField(
        BppLogFieldDefinition field,
        BppLogCardinality cardinality,
        BppLogCorrelationPolicy correlation
    )
    {
        Assert.Equal(cardinality, field.Cardinality);
        Assert.Equal(correlation, field.Correlation);
    }

    private static void AssertFields(
        CapturedBppLogEvent captured,
        params (string Name, object Value)[] expected
    )
    {
        Assert.Equal(expected.Length, captured.Values.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].Name, captured.Values[index].Field.Name);
            Assert.Equal(expected[index].Value, captured.Values[index].Value);
        }
    }
}
