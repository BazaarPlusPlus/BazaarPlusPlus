#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.AccountLink;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.ModApi.Clients;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class AccountLinkPrivacyTests
{
    private const string RequestId = "01JABCDEFGHJKMNPQRSTVWXYZ";

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
        var rendered = BppLogEventRenderer.Render(
            captured.Event,
            captured.Fields,
            captured.Exception
        );
        Assert.DoesNotContain(privateText, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("account-secret", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("link-code-secret", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("token-secret", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("response-body-secret", rendered, StringComparison.Ordinal);
    }
}
