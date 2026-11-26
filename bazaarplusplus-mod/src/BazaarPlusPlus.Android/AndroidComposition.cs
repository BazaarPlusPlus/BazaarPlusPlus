using BazaarPlusPlus.Android.CombatReplay;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.LiveBuild;
using BazaarPlusPlus.Android.Supporters;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameAuth;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.AccountLink;
using BazaarPlusPlus.Game.HistoryPanel.Favorites;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.RunLifecycle;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using BazaarPlusPlus.Mobile.LiveBuild;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.RunLog.Replication;
using BazaarPlusPlus.Storage.Sqlite;
using Il2CppBazaarGameShared.Infra.Messages;
using Il2CppTheBazaar.DataManagement;

namespace BazaarPlusPlus.Android;

internal sealed class AndroidComposition
{
    private static readonly TimeSpan PauseFlushTimeout = TimeSpan.FromMilliseconds(500);

    // The Data Root is on FUSE external storage. DELETE keeps the database in one file for adb
    // pull.
    public static readonly SqliteOpenOptions DatabaseOptions = new(SqliteJournalMode.Delete);

    private readonly CoreComposition _core;
    private readonly ReplayPayloadMaintenanceService _replayMaintenance;
    private readonly ModApiSession _historySession;
    private readonly IRemoteEmbeddedCatalog<TenWinBuildCorpus> _buildCatalog;
    private readonly TouchLiveBuildData _liveBuild;
    private readonly AndroidSupporterData _supporters;
    private readonly BazaarDbLinkClient _accountLink;
    private readonly RunLoggingModule _runLoggingModule;
    private readonly BundleSealCoordinator _sealCoordinator;
    private ReplayMaintenanceFlight? _replayMaintenanceFlight;
    private UploadPump? _uploads;

    public AndroidComposition(
        string dataRoot,
        GameBuildInfo gameBuild,
        IBppSettingsStore settings,
        bool endOfRunCapture
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(gameBuild);
        ArgumentNullException.ThrowIfNull(settings);
        var paths = AndroidPaths.Provider(dataRoot);
        DatabasePath = PathConstants.RunLogDatabase(dataRoot);
        RunLifecycleSource = new AndroidRunLifecycleSource();
        _core = new CoreComposition(
            settings,
            paths,
            new AndroidGameStateProbe(),
            NoEncounterState.Instance,
            new AndroidRunSnapshotProbe(),
            gameBuild
        );
        var services = _core.Services;
        CombatReplayDirectory = PathConstants.CombatReplays(dataRoot);
        var battles = new PvpBattleSqliteStore(DatabasePath, DatabaseOptions);
        var replays = new CombatReplayPayloadStore(CombatReplayDirectory);
        var replayGate = new ReplayPayloadOperationGate();
        Battles = new PvpBattleCatalog(battles);
        Replays = replays;
        ReplayGate = replayGate;
        var reads = new AndroidPvpBattleReads();
        BattleCapture = new AndroidPvpBattleCapture(
            new PvpBattleCaptureWindow<INetMessage>(
                reads,
                services.RunContext,
                services.RunSnapshot,
                static () => DateTimeOffset.UtcNow,
                PvpOpeningKinds.PvpCombat
            ),
            new PvpBattleReadsRehearsal<INetMessage>(
                reads,
                AndroidPvpBattleReads.PlayerReadable,
                static () => UnityEngine.Time.frameCount,
                AndroidPvpBattleReads.SerializeGameSim
            ),
            Battles,
            replays,
            replayGate,
            services.EventBus
        );
        _replayMaintenance = new ReplayPayloadMaintenanceService(
            battles,
            replays,
            replayGate,
            BattleCapture.ProtectedBattleIds
        );

        RunLifecycle = new RunLifecycleModule(
            services.EventBus,
            services.GameStateProbe,
            services.RunContext,
            RunLifecycleSource
        );
        _core.Register(RunLifecycle);
        var gameDataCache = TryGetGameDataCacheDirectory();
        _runLoggingModule = new RunLoggingModule(
            services.EventBus,
            services.RunContext,
            services.RunSnapshot,
            gameBuild.Channel.ToString(),
            () =>
                new QueuedRunLogStore(
                    new RunLogStore(paths, DatabaseOptions),
                    new RunLogStoreLoggerBridge()
                ),
            battles.AttachToRun,
            () => BattleCapture.HasPendingPersistence,
            static () => DateTimeOffset.UtcNow,
            DatabasePath,
            playerAccountIdResolver: AndroidClientCache.TryGetProfileAccountId,
            bundleScreenshotRequestedResolver: () =>
                endOfRunCapture && services.Config.BazaarDbUploadEnabled.Value,
            modVersionResolver: static () => BppPluginVersion.Current,
            subscribeRankRefreshed: static onRefreshed =>
                AndroidClientCache.SubscribeRankRefreshed(
                    (rank, rating) => onRefreshed(new RankSnapshot { Rank = rank, Rating = rating })
                ),
            gameDataEtagResolver: () => GameDataManifest.ReadGameDataEtag(gameDataCache)
        );
        _core.Register(_runLoggingModule);
        _sealCoordinator = new BundleSealCoordinator(
            services,
            new RunPayloadComposer(
                DatabasePath,
                CombatReplayDirectory,
                AndroidPvpBattleEvidence.Instance,
                DatabaseOptions,
                bundlerVersion: BppPluginVersion.Current,
                platform: "android"
            ),
            new BundleScreenshotEncoder(),
            faultProbe: null,
            DatabaseOptions,
            bundlerVersion: BppPluginVersion.Current,
            platform: "android"
        );
        _core.Register(_sealCoordinator);
        _accountLink = new BazaarDbLinkClient(
            BppHttpClientFactory.Create(
                productVersion: BppPluginVersion.Current,
                platformKey: ReleaseManifestEndpoints.AndroidPlatformKey,
                userAgentSuffix: "BazaarDbLink",
                timeout: TimeSpan.FromSeconds(30)
            ),
            new Uri(BazaarDbLinkClient.DefaultRedeemEndpoint)
        );
        var historySession = ModApiSession.TryCreate(
            ModApiUploadDefaults.ApiBaseUrl,
            BppPluginVersion.Current,
            ReleaseManifestEndpoints.AndroidPlatformKey,
            "OnlineClient",
            TimeSpan.FromSeconds(ModApiUploadDefaults.RequestTimeoutSeconds)
        );
        if (historySession == null)
        {
            _accountLink.Dispose();
            throw new InvalidOperationException(
                $"Mod API base URL '{ModApiUploadDefaults.ApiBaseUrl}' is not an absolute URL."
            );
        }
        _historySession = historySession;
        IRemoteEmbeddedCatalog<TenWinBuildCorpus>? buildCatalog = null;
        try
        {
            buildCatalog = AndroidTenWinBuildCatalog.Create(
                dataRoot,
                new AndroidDevSettings(settings).BuildCorpusPort.Value
            );
            _supporters = new AndroidSupporterData(_core.Services.Config, dataRoot);
            _liveBuild = new TouchLiveBuildData(
                new BuildRecommendationRepository(buildCatalog, AndroidCardTemplateSize.Resolve),
                _supporters
            );
            _buildCatalog = buildCatalog;
        }
        catch
        {
            buildCatalog?.Dispose();
            _accountLink.Dispose();
            _historySession.Dispose();
            throw;
        }
    }

    public string DatabasePath { get; }

    public string CombatReplayDirectory { get; }

    public BppSettings Settings => _core.Services.Config;

    public IBppServices Services => _core.Services;

    public RunLifecycleModule RunLifecycle { get; }

    public PvpBattleCatalog Battles { get; }

    public CombatReplayPayloadStore Replays { get; }

    // Capture, maintenance and playback must share this gate so maintenance cannot remove a payload
    // in use.
    public ReplayPayloadOperationGate ReplayGate { get; }

    public TouchLiveBuildData LiveBuild => _liveBuild;

    public AndroidSupporterData Supporters => _supporters;

    public IBppEventBus EventBus => _core.Services.EventBus;

    public IRunContext RunContext => _core.Services.RunContext;

    public AndroidRunLifecycleSource RunLifecycleSource { get; }

    public AndroidPvpBattleCapture BattleCapture { get; }

    public void Start(float nowSeconds)
    {
        _core.Start();
        _replayMaintenanceFlight = new ReplayMaintenanceFlight();
        _replayMaintenanceFlight.Start(token =>
            _replayMaintenance.RunAndReport(DateTimeOffset.UtcNow, token)
        );
        _uploads = UploadPump.Start(
            _core.Services,
            ReleaseManifestEndpoints.AndroidPlatformKey,
            nowSeconds,
            DatabaseOptions
        );
    }

    // Unity stops the frames that tick the upload pump when the app leaves the foreground.
    public void Pause()
    {
        AndroidRunMessageStream.Tick(EventBus);
        _runLoggingModule.FlushPendingWrites(PauseFlushTimeout);
        _sealCoordinator.Pause();
    }

    public void Resume() => _sealCoordinator.Resume();

    public void Tick(float nowSeconds)
    {
        AndroidRunMessageStream.Tick(EventBus);
        BattleCapture.Tick();
        _uploads?.Tick(nowSeconds);
    }

    private static string? TryGetGameDataCacheDirectory()
    {
        try
        {
            return DataManifestActions.Default.GetCachePath();
        }
        catch
        {
            return null;
        }
    }

    public void Stop()
    {
        _uploads?.Dispose();
        _uploads = null;
        _replayMaintenanceFlight?.Dispose();
        _replayMaintenanceFlight = null;
        BattleCapture.Dispose();
        _core.Stop();
        _liveBuild.Stop();
        _supporters.Stop();
        _buildCatalog.Dispose();
        _accountLink.Dispose();
        _historySession.Dispose();
    }

    public HistoryPanelDependencies CreateHistoryDependencies(
        Func<ISavedReplayRuntime?> runtime,
        Func<Guid, bool> hasCardTemplate
    ) =>
        CreateHistoryDependencies(
            RunContext,
            DatabasePath,
            CombatReplayDirectory,
            _historySession,
            runtime,
            hasCardTemplate,
            AndroidHistoryPorts.ProfileAccountId,
            AndroidHistoryPorts.UnscaledTime,
            AndroidHistoryPorts.AccountLinkStore(),
            _accountLink,
            () =>
                HistoryPanelDecisions.ResolveAccountLinkGate(
                    Settings.BazaarDbUploadEnabled.Value,
                    _core.Services.GameBuild.Channel
                ),
            AndroidHistoryPorts.ReadGameAuthSnapshot
        );

    internal static HistoryPanelDependencies CreateHistoryDependencies(
        IRunContext runContext,
        string databasePath,
        string combatReplayDirectory,
        ModApiSession? session,
        Func<ISavedReplayRuntime?> runtime,
        Func<Guid, bool> hasCardTemplate,
        Func<string?> profileAccountId,
        Func<float> unscaledTime,
        BazaarDbAccountLinkStore accountLinkStore,
        BazaarDbLinkClient? accountLinkClient,
        Func<AccountLinkGate>? accountLinkGate,
        BppGameAuthReader? gameAuthReader = null
    )
    {
        ArgumentNullException.ThrowIfNull(runContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(hasCardTemplate);
        var repository = new HistoryPanelRepository(databasePath);
        var ghostBattles =
            session == null
                ? null
                : new GhostBattleSyncService(
                    repository,
                    session,
                    profileAccountId,
                    AndroidPvpBattleEvidence.Instance,
                    hasCardTemplate
                );
        var data = new HistoryPanelDataService(
            repository,
            ghostBattles,
            () => combatReplayDirectory
        );
        var replay = new HistoryPanelReplayService(
            runtime,
            AndroidNoRecording.Instance,
            combatReplayDirectory,
            profileAccountId,
            ghostBattles
        );
        return new HistoryPanelDependencies(
            runContext,
            data,
            replay,
            session,
            accountLinkClient,
            accountLinkGate,
            accountLinkStore,
            unscaledTime,
            profileAccountId
        )
        {
            Favorites = HistoryFavoriteService.OverRuntime(
                repository,
                combatReplayDirectory,
                runtime,
                replay
            ),
            GameAuthReader = gameAuthReader,
        };
    }

    internal sealed class NoEncounterState : IEncounterStateProbe
    {
        public static NoEncounterState Instance { get; } = new();

        public EncounterIdsProbeOutcome GetEncounterIdsOutcome() =>
            EncounterIdsProbeOutcome.Failure(
                new NotSupportedException("The Android host reads no encounter ids.")
            );

        public ChoicePedestalProbeOutcome GetChoicePedestalOutcome() =>
            ChoicePedestalProbeOutcome.Failure(
                EncounterProbeFailureReason.ChoiceResolutionException,
                new NotSupportedException("The Android host reads no offered pedestals.")
            );
    }
}
