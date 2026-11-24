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
    public void Game_token_device_token_and_bind_code_never_enter_bpp_account_logs()
    {
        const string gameToken = "jwe.super-secret-game-token.12345";
        const string deviceToken = "bppd_super-secret-device-token-123456789012";
        const string bindCode = "K7M2-Q9XR";

        BppLog.Reset();
        BppAccountLog.Succeeded(RequestId, BppAccountAction.Bind);
        BppAccountLog.Succeeded(RequestId, BppAccountAction.ActivateDevice);
        BppAccountLog.Failed(
            RequestId,
            BppAccountAction.Bind,
            BppAccountLog.MapServerError("game_token_rejected")
        );
        BppAccountLog.Failed(
            RequestId,
            BppAccountAction.Bind,
            BppAccountLog.MapServerError("invalid_bind_code")
        );
        BppAccountLog.Failed(
            RequestId,
            BppAccountAction.Bind,
            BppAccountLog.MapServerError("game_account_already_bound")
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
            Assert.DoesNotContain(bindCode, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("K7M2", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("bppd_", rendered, StringComparison.Ordinal);
        }
    }
}
