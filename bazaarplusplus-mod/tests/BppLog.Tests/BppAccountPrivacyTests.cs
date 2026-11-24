#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.BppAccount;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class BppAccountPrivacyTests
{
    private const string RequestId = "01JABCDEFGHJKMNPQRSTVWXYZ";

    [Fact]
    public void Game_token_device_token_email_and_code_never_enter_bpp_account_logs()
    {
        const string gameToken = "jwe.super-secret-game-token.12345";
        const string deviceToken = "bppd_super-secret-device-token-123456789012";
        const string email = "private.player@example.com";
        const string verificationCode = "987654";

        BppLog.Reset();
        BppAccountLog.Succeeded(RequestId, BppAccountAction.BindStart);
        BppAccountLog.Succeeded(RequestId, BppAccountAction.BindConfirm);
        BppAccountLog.Succeeded(RequestId, BppAccountAction.ActivateDevice);
        BppAccountLog.Failed(
            RequestId,
            BppAccountAction.BindStart,
            BppAccountLog.MapServerError("game_token_rejected")
        );
        BppAccountLog.Failed(
            RequestId,
            BppAccountAction.BindConfirm,
            BppAccountLog.MapServerError("invalid_verification_code")
        );
        BppAccountLog.TokenCleared(RequestId, BppAccountLogReason.Unauthorized);

        Assert.Equal(6, BppLog.Events.Count);
        foreach (var captured in BppLog.Events)
        {
            var rendered = BppLogEventRenderer.Render(
                captured.Event,
                captured.Fields,
                captured.Exception
            );
            Assert.DoesNotContain(gameToken, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(deviceToken, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(email, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(verificationCode, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("bppd_", rendered, StringComparison.Ordinal);
        }
    }
}
