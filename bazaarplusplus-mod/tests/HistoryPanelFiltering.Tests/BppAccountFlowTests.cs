#nullable enable
using System.Net;
using System.Text;
using BazaarPlusPlus.Core.GameAuth;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.AccountLink;
using BazaarPlusPlus.Game.HistoryPanel.Data;
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
    public async Task CoordinatorBindsWithCodeAndKeepsBindAvailable()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bpp-acct-flow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var responses = new Queue<HttpResponseMessage>();
            var requests = new List<(string Path, JObject? Body)>();
            var authReads = 0;
            var currentAccount = "acct-1";
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
                GameAuthReader = () =>
                {
                    authReads++;
                    return new BppGameAuthSnapshot("jwe.secret.token", "Steam", "Paid");
                },
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

            HistoryPanelViewModel Model() =>
                HistoryPanelViewModelBuilder.Build(
                    state,
                    coordinator,
                    new StubRunContext(),
                    null,
                    0f,
                    Array.Empty<BazaarPlusPlus.Game.Supporters.BPPSupporterSample>(),
                    default,
                    default,
                    hero => hero ?? string.Empty,
                    DateTimeOffset.Now
                );

            coordinator.OnPanelShown();
            Assert.False(state.BppAccountFormExpanded);
            Assert.True(Model().BppAccountActionVisible);

            // Opening the form shows the bind code input; an empty code never reaches the server.
            coordinator.ToggleBppAccountForm();
            Assert.True(state.BppAccountFormExpanded);
            await coordinator.TryBindBppAccountAsync("   ");
            Assert.Equal(0, authReads);
            Assert.True(state.BppAccountFormExpanded);

            // Switching game account closes the form immediately.
            currentAccount = "acct-2";
            coordinator.Tick(2f);
            Assert.False(state.BppAccountFormExpanded);

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
            await coordinator.TryBindBppAccountAsync(" WRONG777 ");
            Assert.True(state.BppAccountFormExpanded);
            Assert.Contains("invalid or expired", state.BppAccountBannerMessage);

            responses.Enqueue(
                Json(
                    HttpStatusCode.Conflict,
                    """{"error":{"code":"game_account_already_bound","message":"bound","retryable":false,"request_id":"r1"}}"""
                )
            );
            await coordinator.TryBindBppAccountAsync(BindCode);
            Assert.True(state.BppAccountFormExpanded);
            Assert.Contains("another BPP Account", state.BppAccountBannerMessage);

            // A valid bind code posts the trimmed code with the game token; the banner names the linked account.
            responses.Enqueue(
                Json(
                    HttpStatusCode.OK,
                    """{"user_name":"OfficialAlice","email_masked":"a***@example.com"}"""
                )
            );
            await coordinator.TryBindBppAccountAsync($"  {BindCode}  ");
            Assert.Equal(3, authReads);
            Assert.False(state.BppAccountFormExpanded);
            Assert.Equal(StatusSeverity.Success, state.BppAccountBannerSeverity);
            Assert.Contains("a***@example.com", state.BppAccountBannerMessage);
            Assert.Contains("OfficialAlice", state.BppAccountBannerMessage);
            Assert.Contains("bazaarplusplus.com/records", state.BppAccountBannerMessage);
            var bound = Model();
            Assert.False(bound.BppAccountFormVisible);
            Assert.True(bound.BppAccountActionVisible);
            Assert.Equal(state.BppAccountBannerMessage, bound.BppAccountBannerText);
            var bindRequest = requests[^1];
            Assert.Equal("/account/bind", bindRequest.Path);
            Assert.Equal(BindCode, bindRequest.Body!["bind_code"]!.ToString());
            Assert.Equal("jwe.secret.token", bindRequest.Body["game_token"]!.ToString());
            Assert.Equal("acct-1", bindRequest.Body["game_account_id"]!.ToString());

            // The mod keeps no link state, so Bind opens again; account_mismatch tells the player to restart the game.
            coordinator.ToggleBppAccountForm();
            Assert.True(state.BppAccountFormExpanded);
            responses.Enqueue(
                Json(
                    HttpStatusCode.Conflict,
                    """{"error":{"code":"account_mismatch","message":"mismatch","retryable":false,"request_id":"r3"}}"""
                )
            );
            await coordinator.TryBindBppAccountAsync(BindCode);
            Assert.Contains("restart the game", state.BppAccountBannerMessage);

            // Privacy: the game token and bind codes never enter BppLog.
            BppLog.Flush();
            Assert.NotEmpty(logSink.Messages);
            foreach (var rendered in logSink.Messages)
            {
                Assert.DoesNotContain("jwe.secret.token", rendered, StringComparison.Ordinal);
                Assert.DoesNotContain(BindCode, rendered, StringComparison.Ordinal);
                Assert.DoesNotContain("WRONG777", rendered, StringComparison.Ordinal);
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
