using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.RunLifecycle;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.RunLog.Replication;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus.Android;

/// <summary>
/// The Android composition root over Core's <see cref="CoreComposition"/> and the Android Data
/// Root: the record chain (Run Lifecycle and Run Logging over the IL2CPP probes, the PvP battle
/// capture with its board captures and Replay Payloads), Bundle sealing, the upload pump, and the
/// Replay Payload maintenance sweep, all on one history database. Features start in registration
/// order and stop in reverse; the pump is ticked from the melon's update and disposed first. The
/// sweep runs once per start on a pool thread, as on the desktop.
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
    private ReplayMaintenanceFlight? _replayMaintenanceFlight;
    private UploadPump? _uploads;

    public AndroidComposition(string dataRoot, GameBuildInfo gameBuild, IBppSettingsStore settings)
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
        var battles = new PvpBattleSqliteStore(DatabasePath, DatabaseOptions);
        var replays = new CombatReplayPayloadStore(PathConstants.CombatReplays(dataRoot));
        var replayGate = new ReplayPayloadOperationGate();
        // The capture saves each battle synchronously under the gate's persistence lease, so no
        // battle is ever queued between its file and its row: there is nothing to protect.
        _replayMaintenance = new ReplayPayloadMaintenanceService(
            battles,
            replays,
            replayGate,
            static () => Array.Empty<string>()
        );
        BattleCapture = new AndroidPvpBattleCapture(
            battles,
            replays,
            replayGate,
            services.EventBus,
            () => services.RunContext.CurrentServerRunId,
            static () => DateTimeOffset.UtcNow
        );

        _core.Register(
            new RunLifecycleModule(
                services.EventBus,
                services.GameStateProbe,
                services.RunContext,
                RunLifecycleSource
            )
        );
        _core.Register(
            new RunLoggingModule(
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
                // The capture saves each Replay Payload before announcing its battle.
                static () => false,
                static () => DateTimeOffset.UtcNow,
                DatabasePath,
                playerAccountIdResolver: AndroidClientCache.TryGetProfileAccountId,
                // The Android host takes no end-of-run screenshot, so its Bundles are Run-only.
                bundleScreenshotRequestedResolver: static () => false,
                modVersionResolver: static () => BppPluginVersion.Current
            )
        );
        _core.Register(
            new BundleSealCoordinator(
                services,
                new RunPayloadComposer(
                    DatabasePath,
                    PathConstants.CombatReplays(dataRoot),
                    AndroidPvpBattleEvidence.Instance,
                    DatabaseOptions
                ),
                NoScreenshots.Instance,
                faultProbe: null,
                DatabaseOptions
            )
        );
    }

    public string DatabasePath { get; }

    public BppSettings Settings => _core.Services.Config;

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

    /// <summary>Main thread, every frame.</summary>
    public void Tick(float nowSeconds) => _uploads?.Tick(nowSeconds);

    public void Stop()
    {
        _uploads?.Dispose();
        _uploads = null;
        _replayMaintenanceFlight?.Dispose();
        _replayMaintenanceFlight = null;
        _core.Stop();
    }

    // Encounter Preview is not composed on Android; nothing here reads encounter ids.
    private sealed class NoEncounterState : IEncounterStateProbe
    {
        public static NoEncounterState Instance { get; } = new();

        public EncounterIdsProbeOutcome GetEncounterIdsOutcome() =>
            EncounterIdsProbeOutcome.Failure(
                new NotSupportedException("The Android host reads no encounter ids.")
            );

        public ChoicePedestalProbeOutcome GetChoicePedestalOutcome() =>
            ChoicePedestalProbeOutcome.Failure(
                EncounterProbeFailureReason.ChoiceResolutionException,
                new NotSupportedException("The Android host reads no choice pedestals.")
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
