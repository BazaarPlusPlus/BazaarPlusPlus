using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.RunLifecycle;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.RunLog.Replication;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus.Android;

/// <summary>
/// The Android composition root: the record chain over the Android Data Root. It binds the IL2CPP
/// probes and run lifecycle signals into Core's Run Lifecycle and Run Logging modules, and the
/// PvP battle capture into Storage's battle store, all on one history database. Features start in
/// registration order and stop in reverse.
/// </summary>
internal sealed class AndroidComposition
{
    /// <summary>
    /// The journal mode every Android store opens the database with. The Data Root is on FUSE
    /// external storage; DELETE keeps one self-contained file that <c>adb pull</c> reads whole.
    /// </summary>
    public static readonly SqliteOpenOptions DatabaseOptions = new(SqliteJournalMode.Delete);

    private readonly BppFeatureRegistry _features = new();

    public AndroidComposition(string dataRoot, GameBuildInfo gameBuild)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(gameBuild);
        var paths = AndroidPaths.Provider(dataRoot);
        DatabasePath = PathConstants.RunLogDatabase(dataRoot);
        EventBus = new InMemoryBppEventBus();
        RunContext = new RunContextStore();
        RunLifecycleSource = new AndroidRunLifecycleSource();
        var battles = new PvpBattleSqliteStore(DatabasePath, DatabaseOptions);
        BattleCapture = new AndroidPvpBattleCapture(
            battles,
            EventBus,
            () => RunContext.CurrentServerRunId,
            static () => DateTimeOffset.UtcNow
        );

        _features.Register(
            new RunLifecycleModule(
                EventBus,
                new AndroidGameStateProbe(),
                RunContext,
                RunLifecycleSource
            )
        );
        _features.Register(
            new RunLoggingModule(
                EventBus,
                RunContext,
                new AndroidRunSnapshotProbe(),
                gameBuild.Channel.ToString(),
                () =>
                    new QueuedRunLogStore(
                        new RunLogStore(paths, DatabaseOptions),
                        new RunLogStoreLoggerBridge()
                    ),
                battles.AttachToRun,
                // No Replay Payload is persisted on Android yet (#360), so completion never waits.
                static () => false,
                static () => DateTimeOffset.UtcNow,
                DatabasePath,
                playerAccountIdResolver: AndroidClientCache.TryGetProfileAccountId,
                // BazaarDB upload is not wired on Android yet (#360).
                bundleScreenshotRequestedResolver: static () => false,
                modVersionResolver: static () => BppPluginVersion.Current
            )
        );
    }

    public string DatabasePath { get; }

    public IBppEventBus EventBus { get; }

    public IRunContext RunContext { get; }

    public AndroidRunLifecycleSource RunLifecycleSource { get; }

    public AndroidPvpBattleCapture BattleCapture { get; }

    public void Start() => _features.Start();

    public void Stop() => _features.Stop();
}
