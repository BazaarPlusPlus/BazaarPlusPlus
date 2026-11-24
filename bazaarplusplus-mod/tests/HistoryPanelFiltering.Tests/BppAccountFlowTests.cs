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
using BazaarPlusPlus.ModApi.Clients;
using Newtonsoft.Json.Linq;
using Xunit;

public sealed class BppAccountFlowTests
{
    private const string BindCode = "K7M2-Q9XR";

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
    public async Task CoordinatorBindCodeActivationAnd401RefreshAndAccountSwitchClearing()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bpp-acct-flow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var responses = new Queue<HttpResponseMessage>();
            var requests = new List<(string Path, JObject? Body)>();
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
                    new StubHandler(
                        async (request, _) =>
                        {
                            var body =
                                request.Content == null
                                    ? null
                                    : JObject.Parse(
                                        await request
                                            .Content.ReadAsStringAsync()
                                            .ConfigureAwait(false)
                                    );
                            requests.Add((request.RequestUri!.AbsolutePath, body));
                            return responses.Dequeue();
                        }
                    )
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

            // Opening the form shows the bind code input; an empty code never reaches the server.
            coordinator.ToggleBppAccountForm();
            Assert.Equal(BppAccountFlowPhase.EnterBindCode, state.BppAccountPhase);
            await coordinator.TrySubmitBppAccountInputAsync("   ");
            Assert.Equal(0, authReads);
            Assert.Equal(BppAccountFlowPhase.EnterBindCode, state.BppAccountPhase);

            // Switching game account closes the form immediately.
            currentAccount = "acct-2";
            coordinator.Tick(2f);
            Assert.Equal(BppAccountFlowPhase.Idle, state.BppAccountPhase);

            // invalid_bind_code and game_account_already_bound keep the input open with a banner.
            currentAccount = "acct-1";
            coordinator.Tick(4f);
            coordinator.ToggleBppAccountForm();
            responses.Enqueue(
                Json(
                    HttpStatusCode.BadRequest,
                    """{"error":{"code":"invalid_bind_code","message":"bad code","retryable":false,"request_id":"r0"}}"""
                )
            );
            await coordinator.TrySubmitBppAccountInputAsync(" WRONG777 ");
            Assert.Equal(BppAccountFlowPhase.EnterBindCode, state.BppAccountPhase);
            Assert.Contains("invalid or expired", state.BppAccountBannerMessage);

            responses.Enqueue(
                Json(
                    HttpStatusCode.Conflict,
                    """{"error":{"code":"game_account_already_bound","message":"bound","retryable":false,"request_id":"r1"}}"""
                )
            );
            await coordinator.TrySubmitBppAccountInputAsync(BindCode);
            Assert.Equal(BppAccountFlowPhase.EnterBindCode, state.BppAccountPhase);
            Assert.Contains("another BPP Account", state.BppAccountBannerMessage);
            Assert.False(state.BppAccountBound);

            // A valid bind code posts the trimmed code with the game token and persists the device token.
            responses.Enqueue(
                Json(
                    HttpStatusCode.OK,
                    """{"device_token":"bppd_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","user_name":"OfficialAlice","email_masked":"a***@example.com"}"""
                )
            );
            await coordinator.TrySubmitBppAccountInputAsync($"  {BindCode}  ");
            Assert.Equal(3, authReads);
            Assert.True(state.BppAccountBound);
            Assert.Equal(BppAccountFlowPhase.Idle, state.BppAccountPhase);
            Assert.Equal("OfficialAlice", state.BppAccountUserName);
            Assert.Equal("a***@example.com", state.BppAccountEmailMasked);
            Assert.Equal(
                "bppd_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                store.TryReadDeviceTokenForAccount("acct-1")
            );
            var bindRequest = requests[^1];
            Assert.Equal("/account/bind", bindRequest.Path);
            Assert.Equal(BindCode, bindRequest.Body!["bind_code"]!.ToString());
            Assert.Equal("jwe.secret.token", bindRequest.Body["game_token"]!.ToString());
            Assert.Equal("acct-1", bindRequest.Body["game_account_id"]!.ToString());

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
            await coordinator.TrySubmitBppAccountInputAsync(BindCode);
            Assert.Contains("restart the game", state.BppAccountBannerMessage);

            // Activating this device calls POST /account/devices and persists the device token.
            coordinator.ToggleBppAccountForm();
            coordinator.OpenBppAccountActivateDevice();
            Assert.Equal(BppAccountFlowPhase.ConfirmActivateDevice, state.BppAccountPhase);
            responses.Enqueue(
                Json(
                    HttpStatusCode.OK,
                    """{"device_token":"bppd_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB","user_name":"OfficialAlice","email_masked":"a***@example.com"}"""
                )
            );
            await coordinator.TrySubmitBppAccountInputAsync(null);
            Assert.Equal("/account/devices", requests[^1].Path);
            Assert.True(state.BppAccountBound);
            Assert.Equal(BppAccountFlowPhase.Idle, state.BppAccountPhase);
            Assert.Equal(
                "bppd_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                store.TryReadDeviceTokenForAccount("acct-1")
            );

            // Privacy: game token, bind code, and device token never enter BppLog.
            BppLog.Flush();
            Assert.NotEmpty(logSink.Messages);
            foreach (var rendered in logSink.Messages)
            {
                Assert.DoesNotContain("jwe.secret.token", rendered, StringComparison.Ordinal);
                Assert.DoesNotContain(BindCode, rendered, StringComparison.Ordinal);
                Assert.DoesNotContain("WRONG777", rendered, StringComparison.Ordinal);
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
