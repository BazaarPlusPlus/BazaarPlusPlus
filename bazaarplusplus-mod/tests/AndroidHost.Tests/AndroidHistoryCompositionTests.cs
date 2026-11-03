using System.Net;
using System.Reflection;
using System.Text;
using BazaarPlusPlus.Android;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.AccountLink;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.Storage.Paths;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// History as <see cref="AndroidComposition"/> composes it, over a temporary history database in
/// the host's DELETE journal mode: Ghost sync over a Mod API session served by a stub handler
/// with the server's own Ghost summary contract, and the replay service over a stand-in runtime
/// with the host's no-recording port.
/// </summary>
public sealed class AndroidHistoryCompositionTests : IDisposable
{
    private const string LocalAccount = "account-local";

    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "bpp-android-history-" + Guid.NewGuid().ToString("N")
    );

    public AndroidHistoryCompositionTests()
    {
        Directory.CreateDirectory(_dataRoot);
        // The record chain opens the database first and sets its journal mode.
        _ = new PvpBattleSqliteStore(DatabasePath, AndroidComposition.DatabaseOptions);
    }

    private string DatabasePath => PathConstants.RunLogDatabase(_dataRoot);

    private string ReplayDirectory => PathConstants.CombatReplays(_dataRoot);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dataRoot, recursive: true);
    }

    [Fact]
    public void Without_a_session_ghost_sync_is_unavailable_and_runs_still_read()
    {
        var dependencies = Compose(session: null, new StandInRuntime());

        Assert.False(dependencies.DataService.CanSyncGhostBattles);
        Assert.Empty(dependencies.DataService.LoadRuns(new(), hero: null).Rows);
        Assert.Null(dependencies.AccountLinkClient);
        Assert.Equal("delete", JournalMode());
    }

    [Fact]
    public void Favorites_and_the_account_link_compose_as_on_the_desktop()
    {
        using var client = new BazaarDbLinkClient(
            new HttpClient(),
            new Uri(BazaarDbLinkClient.DefaultRedeemEndpoint)
        );
        var dependencies = Compose(
            session: null,
            new StandInRuntime(),
            client,
            static () => AccountLinkGate.Available
        );

        Assert.NotNull(dependencies.Favorites);
        Assert.Empty(dependencies.Favorites!.LoadPage(new HistoryPageRequest()).Rows);
        Assert.Same(client, dependencies.AccountLinkClient);
        Assert.Equal(AccountLinkGate.Available, dependencies.AccountLinkGate!());
    }

    [Fact]
    public async Task Ghost_sync_imports_the_contract_battle_and_keeps_the_delete_journal()
    {
        using var session = Session(_ => Json(GhostSummaryContract()));
        var dependencies = Compose(session, new StandInRuntime());
        Assert.True(dependencies.DataService.CanSyncGhostBattles);

        var synced = await dependencies.DataService.SyncGhostBattlesAsync(CancellationToken.None);

        Assert.True(synced.Succeeded, synced.StatusMessage);
        Assert.Equal(1, synced.ImportedCount);
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM battles WHERE source = 'GHOST';"));
        // A Ghost row is no local battle: the saved-replay probe does not see it.
        var ghostBattleId = (string)
            Scalar("SELECT battle_id FROM battles WHERE source = 'GHOST';")!;
        Assert.False(
            new PvpBattleSqliteStore(DatabasePath, AndroidComposition.DatabaseOptions).Exists(
                ghostBattleId
            )
        );
        Assert.Equal("delete", JournalMode());
    }

    [Fact]
    public void The_host_records_no_video_with_the_desktops_reason_code()
    {
        var dependencies = Compose(session: null, new StandInRuntime());
        var battle = Battle(HistoryBattleSource.Local, ReplayAvailability.Saved);

        Assert.True(dependencies.ReplayService.CanReplayBattle(battle, out _));
        Assert.False(dependencies.ReplayService.CanRecordReplay(battle, out _, out var code));
        Assert.Equal(HistoryPanelReplayReasonCode.RecordingUnavailable, code);
        Assert.False(dependencies.ReplayService.CanRecordLoadedReplay(out _));
    }

    [Fact]
    public async Task History_waits_for_the_local_read_and_preserves_a_missing_payload_failure()
    {
        var read = new TaskCompletionSource<bool>();
        var runtime = new StandInRuntime { Read = read.Task };
        var dependencies = Compose(session: null, runtime);
        using var cancellation = new CancellationTokenSource();
        Func<bool> canCommit = () => true;
        var pending = dependencies.ReplayService.ReplayBattleAsync(
            Battle(HistoryBattleSource.Local, ReplayAvailability.Saved),
            false,
            cancellation.Token,
            canCommit
        );

        Assert.False(pending.IsCompleted);
        Assert.Equal(cancellation.Token, runtime.ReadCancellation);
        Assert.Same(canCommit, runtime.ReadCanCommit);
        read.SetResult(false);
        var result = await pending;
        Assert.False(result.Succeeded);
        Assert.Equal(HistoryPanelReplayReasonCode.ReplayRejected, result.ReasonCode);
        Assert.NotEmpty(result.StatusMessage);
    }

    // Core's view model hides Record on a host whose recording port records no video at all, and
    // shows it on one that does (the desktop's). A build asks the runtime once whether the
    // selected saved battle can replay, and Record reuses that answer.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_record_action_shows_only_where_the_host_records_video(bool supported)
    {
        var runtime = new StandInRuntime();
        var composed = Compose(session: null, runtime);
        var dependencies = supported
            ? new HistoryPanelDependencies(
                composed.RunContext,
                composed.DataService,
                new HistoryPanelReplayService(
                    () => runtime,
                    new RecordingPort(),
                    ReplayDirectory,
                    static () => LocalAccount
                ),
                composed.ModApiSession,
                composed.AccountLinkClient,
                composed.AccountLinkGate,
                composed.AccountLinkStore,
                composed.UnscaledTime,
                composed.ProfileAccountId
            )
            : composed;
        var battle = Battle(HistoryBattleSource.Local, ReplayAvailability.Saved);
        var anchor = new HistoryCursor(0, battle.BattleId);
        var state = new HistoryPanelState
        {
            BattlePage = new HistoryCursorPage<HistoryBattleRecord>(
                [battle],
                anchor,
                anchor,
                hasNewer: false,
                hasOlder: false
            ),
        };
        using var coordinator = new HistoryPanelCoordinator(
            state,
            dependencies,
            static () => { },
            static () => { },
            new(static () => { }, static () => { }, static () => null, static () => false)
        );

        HistoryPanelViewModel Model() =>
            HistoryPanelViewModelBuilder.Build(
                state,
                coordinator,
                dependencies.RunContext,
                dependencies.AccountLinkGate,
                0f,
                [],
                default,
                default
            );

        var model = Model();
        Assert.Equal(supported, dependencies.ReplayService.RecordingSupported);
        Assert.Equal(supported, model.RecordAndReplayButtonVisible);
        Assert.True(model.ReplayButtonEnabled);
        Assert.Equal(supported, model.RecordAndReplayButtonEnabled);
        Assert.Equal(new[] { battle.BattleId }, runtime.SavedBattleProbes);

        runtime.Busy = true;
        Assert.False(Model().ReplayButtonEnabled);
        Assert.False(Model().ReplayButtonBusy); // Ordinary unavailable actions still explain why.
        state.DeletingRunId = battle.RunId;
        Assert.True(Model().ReplayButtonBusy);
        Assert.True(Model().DeleteButtonBusy);
        state.DeletingRunId = "another-run";
        Assert.False(Model().ReplayButtonBusy);
        Assert.True(Model().DeleteButtonBusy);
        state.SectionMode = HistorySectionMode.Favorites;
        Assert.False(Model().ReplayButtonBusy);
        Assert.False(Model().DeleteButtonBusy); // A favorite owns an independent copy.
        state.FavoriteActionInProgress = true;
        Assert.True(Model().DeleteButtonBusy);
    }

    [Fact]
    public void A_remote_ghost_offers_its_download_and_needs_the_sync_service()
    {
        var dependencies = Compose(session: null, new StandInRuntime());
        var ghost = Battle(HistoryBattleSource.Ghost, ReplayAvailability.Remote);

        Assert.Equal(
            HistoryPanelText.DownloadReplay(),
            dependencies.ReplayService.GetReplayActionLabel(ghost)
        );
        Assert.Equal(
            HistoryPanelText.Replay(),
            dependencies.ReplayService.GetReplayActionLabel(
                Battle(HistoryBattleSource.Local, ReplayAvailability.Saved)
            )
        );
    }

    [Fact]
    public async Task A_local_replay_reaches_the_runtime_and_a_running_replay_blocks_another()
    {
        var runtime = new StandInRuntime();
        var dependencies = Compose(session: null, runtime);
        var battle = Battle(HistoryBattleSource.Local, ReplayAvailability.Saved);

        var started = await dependencies.ReplayService.ReplayBattleAsync(
            battle,
            recordVideo: false,
            CancellationToken.None
        );
        Assert.True(started.Succeeded);
        Assert.Equal(new[] { battle.BattleId }, runtime.Replayed);

        runtime.Busy = true;
        Assert.False(dependencies.ReplayService.CanReplayBattle(battle, out var reason));
        Assert.Equal(StandInRuntime.BusyReason, reason);
    }

    [Fact]
    public async Task Replay_payload_cleanup_deletes_the_local_and_ghost_files_of_deleted_battles()
    {
        var runtime = new StandInRuntime();
        var dependencies = Compose(session: null, runtime);
        var acquire = dependencies.ReplayService.PreparePayloadMaintenance();
        using var lease = await Task.Run(acquire);
        Assert.NotNull(lease);
        Assert.Null(runtime.PayloadGate.TryAcquirePlayback());
        var payloads = new CombatReplayPayloadStore(ReplayDirectory);
        payloads.Save(ReplayMessages.Payload("deleted"));
        payloads.Save(ReplayMessages.Payload("kept"));

        var result = dependencies.ReplayService.CleanupReplayPayloads(["deleted"]);

        Assert.Equal(0, result.FailedBattleCount);
        Assert.Null(result.Exception);
        Assert.Equal(new[] { "kept" }, payloads.ListBattleIds());
    }

    private HistoryPanelDependencies Compose(
        ModApiSession? session,
        ISavedReplayRuntime runtime,
        BazaarDbLinkClient? accountLinkClient = null,
        Func<AccountLinkGate>? accountLinkGate = null
    ) =>
        AndroidComposition.CreateHistoryDependencies(
            new RunContextStore(),
            DatabasePath,
            ReplayDirectory,
            session,
            () => runtime,
            static _ => true,
            static () => LocalAccount,
            static () => 0f,
            new BazaarDbAccountLinkStore(static _ => false, static _ => { }),
            accountLinkClient,
            accountLinkGate
        );

    private static ModApiSession Session(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        ModApiSession.TryCreate(
            "https://api.example",
            "test",
            platformKey: null,
            "OnlineClient",
            TimeSpan.FromSeconds(30),
            new StubHandler(respond)
        ) ?? throw new InvalidOperationException("test Mod API session");

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static string GhostSummaryContract()
    {
        using var stream =
            Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream("AndroidHost.Tests.ghost-summary.response.json")
            ?? throw new InvalidOperationException("The server Ghost summary contract is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private string JournalMode() => (string)Scalar("PRAGMA journal_mode;")!;

    private object? Scalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static HistoryBattleRecord Battle(
        HistoryBattleSource source,
        ReplayAvailability replay
    ) =>
        new(
            battleId: $"{source}-{replay}".ToLowerInvariant(),
            runId: "run",
            recordedAtUtc: DateTimeOffset.UnixEpoch,
            day: 3,
            hour: 2,
            encounterId: null,
            playerHero: "Vanessa",
            playerRank: null,
            playerRating: null,
            playerLevel: null,
            playerPrestige: null,
            playerVictories: null,
            opponentName: "Opponent",
            opponentHero: "Dooley",
            opponentRank: null,
            opponentRating: null,
            opponentLevel: null,
            opponentPrestige: null,
            opponentVictories: null,
            opponentAccountId: "account-opponent",
            combatKind: "PVPCombat",
            result: "win",
            winnerCombatantId: null,
            loserCombatantId: null,
            snapshotCounts: default,
            isFinalBattle: false,
            source: source,
            replay: replay
        );

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(respond(request));
    }

    // A host that records video, as the desktop's port does.
    private sealed class RecordingPort : IReplayRecordingAvailability
    {
        public bool Supported => true;

        public void Prewarm() { }

        public bool CanRecord() => true;
    }

    // The replay runtime History drives, without the game.
    private sealed class StandInRuntime : ISavedReplayRuntime
    {
        public const string BusyReason = "A saved replay is already starting.";

        public List<string> Replayed { get; } = new();

        public bool Busy { get; set; }
        public Task<bool>? Read { get; set; }
        public CancellationToken ReadCancellation { get; private set; }
        public Func<bool>? ReadCanCommit { get; private set; }

        public bool HasSavedReplaySession => Busy;

        public ReplayPayloadOperationGate PayloadGate { get; } = new();

        public IDisposable? AcquirePayloadMaintenance() =>
            PayloadGate.AcquireMaintenance(CancellationToken.None);

        public bool CanReplaySavedCombats(out string reason)
        {
            reason = Busy ? BusyReason : string.Empty;
            return !Busy;
        }

        // Each call is one saved-replay catalog probe on a real host.
        public List<string> SavedBattleProbes { get; } = new();

        public bool CanReplaySavedBattle(string battleId, out string reason)
        {
            SavedBattleProbes.Add(battleId);
            return CanReplaySavedCombats(out reason);
        }

        public Task<bool> ReplaySavedAsync(
            string battleId,
            bool recordVideo,
            CancellationToken cancellationToken = default,
            Func<bool>? canCommit = null
        )
        {
            Replayed.Add(battleId);
            ReadCancellation = cancellationToken;
            ReadCanCommit = canCommit;
            return Read ?? Task.FromResult(true);
        }

        public bool ReplayLoadedBattle(
            PvpBattleManifest manifest,
            PvpReplayPayload payload,
            bool recordVideo,
            CombatReplayPlaybackSource source
        ) => true;

        public SavedBattleLoadOutcome TryLoadSavedBattle(
            string battleId,
            out PvpBattleManifest? manifest,
            out PvpReplayPayload? payload
        )
        {
            manifest = null;
            payload = null;
            return SavedBattleLoadOutcome.Missing;
        }
    }
}
