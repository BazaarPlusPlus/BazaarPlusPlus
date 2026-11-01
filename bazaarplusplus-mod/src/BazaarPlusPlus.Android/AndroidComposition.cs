using BazaarPlusPlus.Android.CombatReplay;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.LiveBuild;
using BazaarPlusPlus.Android.Supporters;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
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
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using BazaarPlusPlus.Mobile.LiveBuild;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.RunLog.Replication;
using BazaarPlusPlus.Storage.Sqlite;
using Il2CppBazaarGameShared.Infra.Messages;

namespace BazaarPlusPlus.Android;

/// <summary>
/// The Android composition root over Core's <see cref="CoreComposition"/> and the Android Data
/// Root: the record chain (Run Lifecycle and Run Logging over the IL2CPP probes, the PvP battle
/// capture with its board captures and Replay Payloads), Bundle sealing, the upload pump (not in
/// Compatibility Mode, <see cref="AndroidLaunch"/>), and the Replay Payload maintenance sweep,
/// all on one history database. Features start in registration order and stop in reverse; the
/// pump is ticked from the melon's update and disposed first. The sweep runs once per start on a
/// pool thread, as on the desktop. It also owns what History and
/// the replay runtime share with the record chain: the battle store and catalog, the payload
/// store and its operation gate, History's BazaarDB link client, and History's own Mod API
/// session (ADR-0006), which outlives the upload pump's per-activation sessions and is disposed
/// last. It owns the ten-win build catalog, a Remote Embedded Catalog, and the Ten-Win Builds
/// panel's recommendation repository (<see cref="TouchLiveBuildData"/>), plus the shared
/// <see cref="AndroidSupporterData"/> used by Collection and Ten-Win Builds. Each loads on demand;
/// the composition stops them and disposes the build catalog on stop.
/// </summary>
internal sealed class AndroidComposition
{
    /// <summary>
    /// The journal mode every Android store opens the database with. The Data Root is on FUSE
    /// external storage; DELETE keeps one self-contained file that <c>adb pull</c> reads whole.
    /// </summary>
    public static readonly SqliteOpenOptions DatabaseOptions = new(SqliteJournalMode.Delete);

    private readonly CoreComposition _core;
    private readonly ReplayPayloadMaintenanceService _replayMaintenance;
    private readonly ModApiSession _historySession;
    private readonly IRemoteEmbeddedCatalog<TenWinBuildCorpus> _buildCatalog;
    private readonly TouchLiveBuildData _liveBuild;
    private readonly AndroidSupporterData _supporters;
    private readonly BazaarDbLinkClient _accountLink;
    private readonly BundleSealCoordinator _sealCoordinator;
    private readonly AndroidLaunch _launch;
    private readonly IPathProvider _paths;
    private ReplayMaintenanceFlight? _replayMaintenanceFlight;
    private UploadPump? _uploads;

    // launch: what the Launch Record allows. In Compatibility Mode the upload pump never starts
    // and History syncs no Ghost battles; everything else runs.
    public AndroidComposition(
        string dataRoot,
        GameBuildInfo gameBuild,
        IBppSettingsStore settings,
        AndroidLaunch launch
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(gameBuild);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(launch);
        _launch = launch;
        var paths = AndroidPaths.Provider(dataRoot);
        _paths = paths;
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
        // The capture saves each battle synchronously under the gate's persistence lease, so no
        // battle is ever queued between its file and its row: there is nothing to protect.
        _replayMaintenance = new ReplayPayloadMaintenanceService(
            battles,
            replays,
            replayGate,
            static () => Array.Empty<string>()
        );
        // Android records PvP battles only; a monster fight opens no window.
        BattleCapture = new AndroidPvpBattleCapture(
            new PvpBattleCaptureWindow<INetMessage>(
                new AndroidPvpBattleReads(),
                services.RunContext,
                services.RunSnapshot,
                static () => DateTimeOffset.UtcNow,
                PvpOpeningKinds.PvpCombat
            ),
            Battles,
            replays,
            replayGate,
            services.EventBus
        );

        RunLifecycle = new RunLifecycleModule(
            services.EventBus,
            services.GameStateProbe,
            services.RunContext,
            RunLifecycleSource
        );
        _core.Register(RunLifecycle);
        _core.Register(
            new RunLoggingModule(
                services.EventBus,
                services.RunContext,
                services.RunSnapshot,
                gameBuild.Channel.ToString(),
                () =>
                    new QueuedRunLogStore(
                        CreateRunLogStore(paths, launch),
                        new RunLogStoreLoggerBridge()
                    ),
                battles.AttachToRun,
                // The capture saves each Replay Payload before announcing its battle.
                static () => false,
                static () => DateTimeOffset.UtcNow,
                DatabasePath,
                playerAccountIdResolver: AndroidClientCache.TryGetProfileAccountId,
                // The Android host takes no end-of-run screenshot, so its Bundles are Run-only.
                bundleScreenshotRequestedResolver: static () => false,
                modVersionResolver: static () => BppPluginVersion.Current,
                subscribeRankRefreshed: static onRefreshed =>
                    AndroidClientCache.SubscribeRankRefreshed(
                        (rank, rating) =>
                            onRefreshed(new RankSnapshot { Rank = rank, Rating = rating })
                    )
            )
        );
        _sealCoordinator = new BundleSealCoordinator(
            services,
            new RunPayloadComposer(
                DatabasePath,
                CombatReplayDirectory,
                AndroidPvpBattleEvidence.Instance,
                DatabaseOptions
            ),
            NoScreenshots.Instance,
            faultProbe: null,
            DatabaseOptions
        );
        _core.Register(_sealCoordinator);
        // BazaarDB account linking posts to bazaardb.gg, not the Mod API, over its own bare
        // client with the desktop's 30 s timeout.
        _accountLink = new BazaarDbLinkClient(
            BppHttpClientFactory.Create(
                productVersion: BppPluginVersion.Current,
                platformKey: ReleaseManifestEndpoints.AndroidPlatformKey,
                userAgentSuffix: "BazaarDbLink",
                timeout: TimeSpan.FromSeconds(30)
            ),
            new Uri(BazaarDbLinkClient.DefaultRedeemEndpoint)
        );
        // The base URL is a constant, so a null session is a build defect: the composition fails
        // to start (android.record_chain.start_failed), as the desktop plugin does (ADR-0006).
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
        // Last, and a failure here disposes the disposables made above: the catalog starts no
        // load until the touch UI warms it, so nothing is in flight to leak.
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

    /// <summary>The battle catalog over the composition's own DELETE-mode battle store.</summary>
    public PvpBattleCatalog Battles { get; }

    public CombatReplayPayloadStore Replays { get; }

    /// <summary>
    /// The one payload gate: the capture's persistence, the maintenance sweep, and replay
    /// playback each take its lease.
    /// </summary>
    public ReplayPayloadOperationGate ReplayGate { get; }

    /// <summary>The Ten-Win Builds panel's data over the composition-owned build catalog.</summary>
    public TouchLiveBuildData LiveBuild => _liveBuild;

    public AndroidSupporterData Supporters => _supporters;

    public IBppEventBus EventBus => _core.Services.EventBus;

    public IRunContext RunContext => _core.Services.RunContext;

    public AndroidRunLifecycleSource RunLifecycleSource { get; }

    public AndroidPvpBattleCapture BattleCapture { get; }

    public void Start(float nowSeconds)
    {
        // Before the record chain could resume a Run that a normal launch left active.
        PrepareRuns(_paths, _launch);
        _core.Start();
        _replayMaintenanceFlight = new ReplayMaintenanceFlight();
        _replayMaintenanceFlight.Start(token =>
            _replayMaintenance.RunAndReport(DateTimeOffset.UtcNow, token)
        );
        // Compatibility Mode records only History-Only Runs, which are never sealed, and sends
        // no Bundle a normal launch sealed either.
        if (_launch.Uploads)
            _uploads = UploadPump.Start(
                _core.Services,
                ReleaseManifestEndpoints.AndroidPlatformKey,
                nowSeconds,
                DatabaseOptions
            );
    }

    /// <summary>Whether this composition's upload pump is running.</summary>
    public bool UploadPumpStarted => _uploads != null;

    /// <summary>
    /// The record chain's Run store: in Compatibility Mode it writes History-Only Runs and
    /// resumes none.
    /// </summary>
    internal static RunLogStore CreateRunLogStore(IPathProvider paths, AndroidLaunch launch) =>
        new(paths, DatabaseOptions, launch.RunUploadPolicy);

    /// <summary>
    /// In Compatibility Mode, demotes every active <c>bundle</c> Run to History-Only before the
    /// record chain starts, so a Run a normal launch left active is never sealed with what this
    /// launch adds; it is not resumed either, and its earlier part stays local. Returns how many
    /// changed; a normal launch changes nothing.
    /// </summary>
    internal static int PrepareRuns(IPathProvider paths, AndroidLaunch launch)
    {
        if (!launch.CompatibilityMode)
            return 0;
        var demoted = new RunLogStore(paths, DatabaseOptions).DemoteActiveRunsToHistoryOnly();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.launch_record.runs_demoted"),
            ("runs", demoted)
        );
        return demoted;
    }

    /// <summary>Whether History syncs Ghost battles over its session.</summary>
    public bool GhostSync => _launch.Uploads;

    /// <summary>
    /// The app left the foreground: the seal worker parks after its current pass. The upload
    /// pump needs no call: Unity stops the frames that tick it, and an attempt already in flight
    /// is left to finish.
    /// </summary>
    public void Pause() => _sealCoordinator.Pause();

    /// <summary>The app is back in front: the seal worker scans at once.</summary>
    public void Resume() => _sealCoordinator.Resume();

    /// <summary>Main thread, every frame.</summary>
    public void Tick(float nowSeconds) => _uploads?.Tick(nowSeconds);

    public void Stop()
    {
        _uploads?.Dispose();
        _uploads = null;
        _replayMaintenanceFlight?.Dispose();
        _replayMaintenanceFlight = null;
        _core.Stop();
        _liveBuild.Stop();
        _supporters.Stop();
        _buildCatalog.Dispose();
        _accountLink.Dispose();
        _historySession.Dispose();
    }

    /// <summary>
    /// History's dependencies over this composition: the repository on the history database,
    /// Ghost sync over History's session unless the launch is in Compatibility Mode, and the
    /// replay service over the runtime the accessor returns. The panel only receives them; the
    /// composition stays their owner.
    /// </summary>
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
            GhostSync
        );

    /// <summary>
    /// The managed half of <see cref="CreateHistoryDependencies(Func{ISavedReplayRuntime?}, Func{Guid, bool})"/>:
    /// every input is a path, a session, or a delegate, so a test composes History over a
    /// temporary database without the game. A null session, or <paramref name="ghostSync"/>
    /// false (Compatibility Mode), leaves Ghost sync unavailable; the session still answers the
    /// server check. A null link client leaves out the account-link form. Favorites read and play
    /// through the runtime the accessor returns, as on the desktop.
    /// </summary>
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
        bool ghostSync = true
    )
    {
        ArgumentNullException.ThrowIfNull(runContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(hasCardTemplate);
        var repository = new HistoryPanelRepository(databasePath);
        var ghostBattles =
            session == null || !ghostSync
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
        };
    }

    // Encounter Preview is not composed on Android; nothing here reads encounter ids or the
    // offered pedestal, so the touch Collection pre-selects no source. This probe makes no Unity
    // call, so it is safe from any thread a Core service might ask it on.
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

    private sealed class NoScreenshots : IBundleScreenshotEncoder
    {
        public static NoScreenshots Instance { get; } = new();

        public Task<BundleScreenshotBuildInputV6?> EncodeAsync(
            string absolutePath,
            long capturedAtMs,
            CancellationToken cancellationToken
        ) => Task.FromResult<BundleScreenshotBuildInputV6?>(null);
    }
}
