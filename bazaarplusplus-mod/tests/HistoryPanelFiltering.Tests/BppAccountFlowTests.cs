#nullable enable
using System.Net;
using System.Text;
using BazaarPlusPlus.Core.GameAuth;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.AccountLink;
using BazaarPlusPlus.Game.HistoryPanel.BppAccount;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.ModApi.Clients;
using Xunit;

public sealed class BppAccountFlowTests
{
    public BppAccountFlowTests() => L.Install(() => "zh-Hans", () => BppChineseLocaleMode.Mainland);

    [Fact]
    public void SessionStoreRoundTripsAtomicallyAndMatchesOnlyStoredAccount()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bpp-acct-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "bpp-account.json");
            var store = new BppAccountSessionStore(path);
            Assert.Null(store.TryRead());
            Assert.Null(store.TryReadDeviceTokenForAccount("acct-1"));

            store.Save(
                new BppStoredAccountSession(
                    "acct-1",
                    "bppd_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                    "Alice",
                    "a***@example.com"
                )
            );
            var loaded = store.TryRead();
            Assert.NotNull(loaded);
            Assert.Equal("acct-1", loaded!.GameAccountId);
            Assert.Equal("bppd_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", loaded.DeviceToken);
            Assert.Equal("Alice", loaded.UserName);
            Assert.Equal("a***@example.com", loaded.EmailMasked);
            Assert.Equal(
                "bppd_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                store.TryReadDeviceTokenForAccount("acct-1")
            );
            Assert.Null(store.TryReadDeviceTokenForAccount("acct-2"));

            store.Clear();
            Assert.Null(store.TryRead());
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CoordinatorTwoStepBindAnd401RefreshAndAccountSwitchClearing()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bpp-acct-flow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var responses = new Queue<HttpResponseMessage>();
            var authReads = 0;
            var currentAccount = "acct-1";
            var store = new BppAccountSessionStore(Path.Combine(dir, "bpp-account.json"));
            using var session =
                ModApiSession.TryCreate(
                    "https://mod-api-v6.bazaarplusplus.com",
                    "6.0.0",
                    "macos-arm64",
                    "Test",
                    TimeSpan.FromSeconds(15),
                    new StubHandler((_, _) => Task.FromResult(responses.Dequeue()))
                ) ?? throw new InvalidOperationException("valid session");

            var state = new HistoryPanelState();
            var repository = new HistoryPanelRepository(Path.Combine(dir, "unused.sqlite3"));
            var deps = new HistoryPanelDependencies(
                new StubRunContext(),
                new HistoryPanelDataService(repository, null),
                new AcceptingReplay(),
                session,
                null,
                null,
                new BazaarDbAccountLinkStore(_ => false, _ => { }),
                () => 0f,
                () => currentAccount
            )
            {
                BppAccountStore = store,
                GameAuthReader = () =>
                {
                    authReads++;
                    return new BppGameAuthSnapshot("jwe.secret.token", "Steam", "Paid");
                },
                ProfileDisplayName = () => "LocalAlice",
            };

            var logSink = new CapturingLogSink();
            BppLog.Install(logSink);
            using var coordinator = new HistoryPanelCoordinator(
                state,
                deps,
                () => { },
                () => { },
                new HistoryPanelReplayReturnPort(() => { }, () => { }, () => null, () => false)
            );

            coordinator.OnPanelShown();
            Assert.Equal(BppAccountFlowPhase.Idle, state.BppAccountPhase);
            Assert.False(state.BppAccountBound);

            // Step 1 -> bind/start -> Step 2 (EnterCode)
            coordinator.ToggleBppAccountForm();
            Assert.Equal(BppAccountFlowPhase.EnterEmail, state.BppAccountPhase);
            responses.Enqueue(
                Json(
                    HttpStatusCode.OK,
                    """{"pending_id":"pid-1","user_name":"OfficialAlice","email_masked":"a***@example.com","expires_at_ms":1785629400000}"""
                )
            );
            await coordinator.TrySubmitBppAccountInputAsync("secret.alice@example.com");
            Assert.Equal(1, authReads);
            Assert.Equal(BppAccountFlowPhase.EnterCode, state.BppAccountPhase);
            Assert.Equal("pid-1", state.BppAccountPendingId);
            Assert.Equal("OfficialAlice", state.BppAccountPendingUserName);
            Assert.Equal("a***@example.com", state.BppAccountPendingEmailMasked);

            // Switching game account clears pending state immediately.
            currentAccount = "acct-2";
            coordinator.Tick(2f);
            Assert.Equal(BppAccountFlowPhase.Idle, state.BppAccountPhase);
            Assert.Null(state.BppAccountPendingId);

            // Re-open on acct-1: 409 game_account_already_bound transitions to ConfirmActivateDevice with email_masked.
            currentAccount = "acct-1";
            coordinator.Tick(4f);
            coordinator.ToggleBppAccountForm();
            responses.Enqueue(
                Json(
                    HttpStatusCode.Conflict,
                    """{"error":{"code":"game_account_already_bound","message":"bound","retryable":false,"request_id":"r1","details":{"email_masked":"a***@example.com"}}}"""
                )
            );
            await coordinator.TrySubmitBppAccountInputAsync("secret.alice@example.com");
            Assert.Equal(BppAccountFlowPhase.ConfirmActivateDevice, state.BppAccountPhase);
            Assert.Equal("a***@example.com", state.BppAccountPendingEmailMasked);

            // Confirming activation calls POST /account/devices and persists device token.
            responses.Enqueue(
                Json(
                    HttpStatusCode.OK,
                    """{"device_token":"bppd_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","user_name":"OfficialAlice","email_masked":"a***@example.com"}"""
                )
            );
            await coordinator.TrySubmitBppAccountInputAsync(null);
            Assert.True(state.BppAccountBound);
            Assert.Equal(BppAccountFlowPhase.Idle, state.BppAccountPhase);
            Assert.Equal(
                "bppd_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                store.TryReadDeviceTokenForAccount("acct-1")
            );

            // GET /account/me 401 clears the stored device token and bound state.
            responses.Enqueue(
                Json(
                    HttpStatusCode.Unauthorized,
                    """{"error":{"code":"unauthorized","message":"revoked","retryable":false,"request_id":"r2"}}"""
                )
            );
            await coordinator.RefreshBppAccountSessionAsync();
            Assert.False(state.BppAccountBound);
            Assert.Null(store.TryRead());

            // Error mapping for account_mismatch tells player to restart the game.
            coordinator.ToggleBppAccountForm();
            responses.Enqueue(
                Json(
                    HttpStatusCode.Conflict,
                    """{"error":{"code":"account_mismatch","message":"mismatch","retryable":false,"request_id":"r3"}}"""
                )
            );
            await coordinator.TrySubmitBppAccountInputAsync("secret.alice@example.com");
            Assert.Contains("重启游戏", state.BppAccountBannerMessage);

            // Privacy: game token, device token, and raw email never enter BppLog.
            BppLog.Flush();
            foreach (var rendered in logSink.Messages)
            {
                Assert.DoesNotContain("jwe.secret.token", rendered, StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "secret.alice@example.com",
                    rendered,
                    StringComparison.Ordinal
                );
                Assert.DoesNotContain("bppd_", rendered, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class CapturingLogSink : IBppLogSink
    {
        public List<string> Messages { get; } = new();

        public void Write(BppLogSeverity severity, string message) => Messages.Add(message);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send
    ) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => send(request, cancellationToken);
    }

    private sealed class StubRunContext : IRunContext
    {
        public bool IsInGameRun { get; set; }
        public string? CurrentServerRunId { get; set; }
        public RunExitKind LastRunExitKind { get; set; }
    }
}
