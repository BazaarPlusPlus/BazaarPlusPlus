#nullable enable
using System.Runtime.CompilerServices;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.RunLog.Replication;
using BepInEx.Logging;
using Microsoft.Data.Sqlite;

// The Run Logging anchor: InMemoryBppEventBus -> RunLoggingModule -> QueuedRunLogStore ->
// RunLogStore and PvpBattleSqliteStore -> one SQLite file, the rows the installer's History reads.
// Only IRunContext, IRunSnapshotProbe, the clock, and the deferred-completion timer are stand-ins;
// they are the module's effect seams (ADR-0003). The database always starts empty: v1/v2 files
// are unsupported and are never built here. HistoryDatabaseArtifacts owns the two goldens.
//
// Failure modes this anchor exists to catch, each tied to the step that fails:
// 1. Start: the store is created before Start, or a Start that throws after creating it leaves
//    the store's writer alive. A Subscribe failure would take the same rollback, but
//    InMemoryBppEventBus.Subscribe cannot throw for a non-null handler, so it is not driven.
// 2. Run created: a field of the create request maps to the wrong column, or a second
//    RunInitializedObserved for the same run appends a second run_started.
// 3. PvP recorded: a matching PvP manifest is not attached to the run (a replay captured before
//    the run id was known keeps battles.run_id NULL), a mismatched or PvE manifest is written,
//    or run_events.seq and runs.last_seq drift apart.
// 4. Completion: a run completes while replay persistence is still pending, a stale deadline
//    completes a run that resumed or was replaced, or a replacement inherits the old deadline.
// 5. Interruption: an interrupted run that comes back is abandoned, or a different run starts
//    before the interrupted one is abandoned.
// 6. Reused server run id: a terminal row is reopened instead of a collision id being created.
// 7. Crash recovery (data loss): a restart loses run_id, last_seq, or the checkpoint day/hour of
//    the unfinished run, or returns a run that was abandoned since.
// 8. Quiescence: Stop leaves the module subscribed, or a handler captured before Stop writes
//    after the store is disposed.
// The order "attach the battle, then append the event and checkpoint" is not provable from final
// rows: the attach commits synchronously while the event and checkpoint go through the queue.
using var logSource = new ManualLogSource("run-logging-pipeline");
var logs = new List<string>();
logSource.LogEvent += (_, args) => logs.Add(args.Data?.ToString() ?? string.Empty);
BppLog.Install(logSource);

var root = Path.Combine(
    Path.GetTempPath(),
    "bpp-run-logging-pipeline-" + Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(root);
var artifacts = new HistoryDatabaseArtifacts();
Exception? failure = null;
try
{
    StartFailureDisposesTheOwnedStore(Path.Combine(root, "start-failure"));
    RunScenario(new Harness(Path.Combine(root, "history")), artifacts, logs);
}
catch (Exception ex)
{
    failure = ex;
}
finally
{
    SqliteConnection.ClearAllPools();
    Directory.Delete(root, recursive: true);
}

// The rows diff comes first, so a regression reads as an artifact diff before the check message.
artifacts.CompareRows(scenarioCompleted: failure == null);
if (failure != null)
{
    Console.Error.WriteLine(failure);
    return 1;
}
if (artifacts.Failed)
    return 1;
Console.WriteLine("Run logging pipeline passed.");
return 0;

static void RunScenario(Harness h, HistoryDatabaseArtifacts artifacts, List<string> logs)
{
    // 1. start: the store is created by Start, once.
    var bus = new InMemoryBppEventBus();
    var module = h.CreateModule(bus);
    Check.That(h.StoresCreated == 0, "1: constructing the module must not create its store.");
    module.Start();
    module.Start();
    Check.That(h.StoresCreated == 1, "1: Start creates the store exactly once.");
    artifacts.RecordSchema(h.Database);
    h.Record(artifacts, 1, "start");

    // 2. run-created: a run initialization creates the run row and one run_started event.
    h.Clock.Advance(TimeSpan.FromMinutes(1));
    h.Probe.Basics = Basics(day: 1, hour: 0, victories: 0, losses: 0);
    h.Activate(bus, "run-alpha");
    h.Clock.Advance(TimeSpan.FromSeconds(5));
    h.Activate(bus, "run-alpha");
    h.Record(artifacts, 2, "run-created");
    Check.That(
        h.Count("SELECT COUNT(*) FROM run_events WHERE run_id='run-alpha'") == 1,
        "2: a repeated initialization of the same run appends no second run_started."
    );

    // 3. pvp-recorded: matching PvP battles attach and append; mismatched and PvE ones do not.
    h.Clock.Advance(TimeSpan.FromMinutes(3));
    h.Probe.Basics = Basics(day: 2, hour: 1, victories: 1, losses: 0);
    h.Probe.Stats = Stats(gold: 14);
    h.RecordBattle(bus, "battle-alpha-1", h.Context.CurrentServerRunId, day: 2, hour: 1);
    h.Clock.Advance(TimeSpan.FromMinutes(3));
    // A replay captured before the run id reached the run context carries no run id.
    h.RecordBattle(bus, "battle-alpha-2", runId: null, day: 2, hour: 3);
    h.RecordBattle(bus, "battle-elsewhere", "run-elsewhere", day: 2, hour: 3);
    bus.Publish(
        new PvpBattleRecorded
        {
            Manifest = new PvpBattleManifest
            {
                BattleId = "battle-pve",
                RunId = "run-alpha",
                CombatKind = "Combat",
            },
        }
    );
    h.Record(artifacts, 3, "pvp-recorded");
    Check.That(
        h.Text("SELECT run_id FROM battles WHERE battle_id='battle-alpha-2'") == "run-alpha"
            && h.Text("SELECT run_id FROM battles WHERE battle_id='battle-elsewhere'") == null,
        "3: the module attaches a run-less PvP battle to the active run and ignores a mismatch."
    );
    Check.That(
        h.Count("SELECT last_seq FROM runs WHERE run_id='run-alpha'") == 3
            && h.Count("SELECT MAX(seq) FROM run_events WHERE run_id='run-alpha'") == 3,
        "3: run_events.seq counts from 1 and runs.last_seq follows it."
    );

    // 4. completion: pending replay persistence defers completion until drained or the deadline.
    h.Clock.Advance(TimeSpan.FromMinutes(2));
    h.Probe.Basics = Basics(day: 3, hour: 2, victories: 2, losses: 1);
    h.Probe.Rank = new RankSnapshot { Rank = "Gold 1", Rating = 1436 };
    h.PendingReplayPersistence = true;
    h.EndRun(bus, RunExitKind.Completed);
    h.RecordBattle(bus, "battle-alpha-3", "run-alpha", day: 3, hour: 2);
    h.Clock.Advance(TimeSpan.FromSeconds(1));
    bus.Publish(new CombatReplayPersistenceDrained());
    Check.That(
        h.Status("run-alpha") == "active",
        "4: completion waits while replay persistence is pending."
    );
    h.PendingReplayPersistence = false;
    bus.Publish(new CombatReplayPersistenceDrained());
    Check.That(h.Status("run-alpha") == "completed", "4: a drain completes the run at once.");

    h.Clock.Advance(TimeSpan.FromMinutes(1));
    h.Probe.Basics = Basics(day: 1, hour: 0, victories: 0, losses: 0);
    h.Activate(bus, "run-bravo");
    h.PendingReplayPersistence = true;
    h.EndRun(bus, RunExitKind.Completed);
    h.Context.IsInGameRun = true;
    h.Context.CurrentServerRunId = "run-bravo";
    h.Clock.Advance(TimeSpan.FromSeconds(2));
    bus.Publish(new RunInitializedObserved { RunId = "run-bravo" });
    Check.That(
        h.Status("run-bravo") == "active",
        "4: a deadline must not complete a run that resumed before it fired."
    );

    h.EndRun(bus, RunExitKind.Completed);
    h.Context.IsInGameRun = true;
    h.Context.CurrentServerRunId = "run-charlie";
    h.Clock.Advance(TimeSpan.FromSeconds(2));
    Check.That(
        h.Status("run-bravo") == "active",
        "4: a stale deadline must not complete a run whose replacement is entering."
    );
    bus.Publish(new RunInitializedObserved { RunId = "run-charlie" });
    h.EndRun(bus, RunExitKind.Completed);
    h.Scheduler.FireEarly();
    h.Clock.Advance(TimeSpan.FromSeconds(1));
    Check.That(
        h.Status("run-charlie") == "active",
        "4: a replacement gets its own full grace period, and an early timer re-arms."
    );
    h.Clock.Advance(TimeSpan.FromSeconds(1));
    Check.That(
        h.Status("run-charlie") == "completed"
            && logs.Any(log =>
                log.Contains("event=run_logging.run.completion_degraded", StringComparison.Ordinal)
                && log.Contains("replay_drain_timeout", StringComparison.Ordinal)
            ),
        "4: the deadline completes the run as a replay-drain timeout."
    );
    h.PendingReplayPersistence = false;
    h.Record(artifacts, 4, "completion");

    // 5. interrupt: the same run id resumes; a different one abandons the interrupted run first.
    h.Clock.Advance(TimeSpan.FromMinutes(1));
    h.Probe.Basics = Basics(day: 4, hour: 1, victories: 3, losses: 1);
    h.Activate(bus, "run-delta");
    h.EndRun(bus, RunExitKind.Interrupted);
    h.Clock.Advance(TimeSpan.FromMinutes(1));
    h.Activate(bus, "run-delta");
    Check.That(h.Status("run-delta") == "active", "5: the same run id resumes.");
    h.EndRun(bus, RunExitKind.Interrupted);
    h.Clock.Advance(TimeSpan.FromMinutes(1));
    h.Activate(bus, "run-echo");
    h.Clock.Advance(TimeSpan.FromMinutes(1));
    h.EndRun(bus, RunExitKind.Completed);
    h.Record(artifacts, 5, "interrupt");
    Check.That(
        h.Text("SELECT reason FROM runs WHERE run_id='run-delta'") == "run_interrupted"
            && h.Status("run-echo") == "completed",
        "5: a different run abandons the interrupted run before it starts."
    );

    // 6. collision: a reused terminal server run id gets an independent local identity.
    h.Clock.Advance(TimeSpan.FromMinutes(1));
    h.Probe.Basics = Basics(day: 1, hour: 0, victories: 0, losses: 0);
    h.Activate(bus, "run-alpha");
    var collisionId = h.Context.CurrentServerRunId!;
    h.Clock.Advance(TimeSpan.FromMinutes(2));
    h.Probe.Basics = Basics(day: 2, hour: 4, victories: 1, losses: 0);
    h.RecordBattle(bus, "battle-alpha-reused", h.Context.CurrentServerRunId, day: 2, hour: 4);
    h.Activate(bus, "run-alpha");
    h.Record(artifacts, 6, "collision");
    Check.That(
        collisionId != "run-alpha"
            && RunLogRunIdentity.MatchesServerRunId(collisionId, "run-alpha")
            && h.Status("run-alpha") == "completed"
            && logs.Any(log =>
                log.Contains(
                    "event=run_logging.run.id_collision_recovered",
                    StringComparison.Ordinal
                )
            ),
        "6: a reused terminal run id must not reopen the terminal row."
    );
    Check.That(
        h.Context.CurrentServerRunId == collisionId,
        "6: a repeated initialization keeps the recovered local identity."
    );

    // 7. restart: stop without completing, then a new process recovers the unfinished run.
    module.Stop();
    h.ForgetStore();
    var recovered = new RunLogStore(h.Paths).TryResumeActiveRun();
    Check.That(
        recovered != null
            && recovered.RunId == collisionId
            && recovered.LastSeq == 2
            && recovered.Day == 2
            && recovered.Hour == 4,
        "7: recovery must restore run_id, last_seq, and the checkpoint day and hour."
    );
    var restartedBus = new InMemoryBppEventBus();
    h.Context.IsInGameRun = false;
    h.Context.CurrentServerRunId = null;
    // Subscribed before the module, so InMemoryBppEventBus.Publish snapshots it first (step 8).
    var stopOnNextBattle = false;
    var restarted = h.CreateModule(restartedBus);
    restartedBus.Subscribe<PvpBattleRecorded>(_ =>
    {
        if (stopOnNextBattle)
            restarted.Stop();
    });
    restarted.Start();
    h.Clock.Advance(TimeSpan.FromMinutes(1));
    h.Activate(restartedBus, "run-alpha");
    h.Clock.Advance(TimeSpan.FromMinutes(1));
    h.Probe.Basics = Basics(day: 1, hour: 0, victories: 0, losses: 0);
    h.Activate(restartedBus, "run-foxtrot");
    h.Record(artifacts, 7, "restart");
    Check.That(
        new RunLogStore(h.Paths).TryResumeActiveRun()?.RunId == "run-foxtrot"
            && h.Text($"SELECT status FROM runs WHERE run_id='{collisionId}'") == "abandoned",
        "7: an abandoned run is never resumed again."
    );

    // 8. stop-barrier: Stop forces the deferred completion; a handler captured before Stop runs
    // after it and writes nothing; Stop is idempotent and releases every subscription.
    h.Clock.Advance(TimeSpan.FromMinutes(1));
    h.PendingReplayPersistence = true;
    h.EndRun(restartedBus, RunExitKind.Completed);
    stopOnNextBattle = true;
    h.RecordBattle(restartedBus, "battle-foxtrot-after-stop", "run-foxtrot", day: 1, hour: 0);
    h.ForgetStore();
    restarted.Stop();
    h.Activate(restartedBus, "run-golf");
    h.PendingReplayPersistence = false;
    var released = StartAndStop(h, restartedBus);
    h.ForgetStore();
    h.Record(artifacts, 8, "stop-barrier");
    Check.That(
        h.Status("run-foxtrot") == "completed"
            && logs.Any(log => log.Contains("shutdown_forced", StringComparison.Ordinal))
            && h.Count("SELECT COUNT(*) FROM run_events WHERE run_id='run-foxtrot'") == 1
            && h.Count("SELECT COUNT(*) FROM runs WHERE run_id='run-golf'") == 0,
        "8: Stop forces completion, and nothing captured before or published after it writes."
    );
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    Check.That(!released.IsAlive, "8: after Stop the event bus must not hold the module.");
    GC.KeepAlive(restartedBus);
}

static void StartFailureDisposesTheOwnedStore(string dataRoot)
{
    var h = new Harness(dataRoot);
    _ = new RunLogStore(h.Paths);
    h.Execute(
        "INSERT INTO runs (run_id, started_at_utc, last_seen_at_utc, status, hero, game_mode) "
            + "VALUES ('unreadable-run', 'not-a-time', 'not-a-time', 'active', 'Vanessa', 'Ranked');"
    );
    var bus = new InMemoryBppEventBus();
    var module = h.CreateModule(bus);
    var failed = false;
    try
    {
        module.Start();
    }
    catch (FormatException)
    {
        failed = true;
    }
    Check.That(failed && h.StoresCreated == 1, "1: an unreadable active run fails Start.");
    var disposed = false;
    try
    {
        h.LiveStore!.AppendEvent("unreadable-run", new RunLogEvent { Kind = "probe" });
    }
    catch (ObjectDisposedException)
    {
        disposed = true;
    }
    Check.That(disposed, "1: a failed Start disposes the store it created.");
    h.ForgetStore();
    h.Context.IsInGameRun = true;
    h.Activate(bus, "run-after-failure");
    Check.That(
        h.Count("SELECT COUNT(*) FROM runs") == 1,
        "1: a failed Start leaves no subscription behind."
    );
}

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference StartAndStop(Harness h, IBppEventBus bus)
{
    var module = h.CreateModule(bus);
    module.Start();
    module.Stop();
    module.Stop();
    return new WeakReference(module);
}

static RunBasicsSnapshot Basics(int day, int hour, int victories, int losses) =>
    new()
    {
        Day = day,
        Hour = hour,
        Victories = victories,
        Losses = losses,
        Hero = "Vanessa",
        GameMode = "Ranked",
    };

static PlayerStatsSnapshot Stats(int gold) =>
    new()
    {
        MaxHealth = 350,
        Prestige = 20,
        Level = 4,
        Income = 6,
        Gold = gold,
    };

/// <summary>Owns one data root and the stand-ins the module reads through its effect seams.</summary>
internal sealed class Harness
{
    private static readonly DateTimeOffset Epoch = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    internal Harness(string dataRoot)
    {
        Directory.CreateDirectory(dataRoot);
        Paths = new TestPaths(dataRoot);
        Database = PathConstants.RunLogDatabase(dataRoot);
        Clock = new ManualClock(Epoch);
        Scheduler = new ManualScheduler(Clock);
        Catalog = new PvpBattleSqliteStore(Database);
    }

    internal IPathProvider Paths { get; }
    internal string Database { get; }
    internal ManualClock Clock { get; }
    internal ManualScheduler Scheduler { get; }
    internal PvpBattleSqliteStore Catalog { get; }
    internal TestRunContext Context { get; } = new();
    internal TestSnapshotProbe Probe { get; } = new();
    internal bool PendingReplayPersistence { get; set; }
    internal int StoresCreated { get; private set; }
    internal QueuedRunLogStore? LiveStore { get; private set; }

    internal RunLoggingModule CreateModule(IBppEventBus bus) =>
        new(
            bus,
            Context,
            Probe,
            "Online",
            () =>
            {
                StoresCreated++;
                LiveStore = new QueuedRunLogStore(
                    new RunLogStore(Paths),
                    new RunLogStoreLoggerBridge()
                );
                return LiveStore;
            },
            Catalog,
            () => PendingReplayPersistence,
            () => Clock.Now,
            Database,
            Scheduler.Schedule,
            playerAccountIdResolver: () => "account-001",
            bundleScreenshotRequestedResolver: () => true,
            modVersionResolver: () => "5.0.0"
        );

    /// <summary>The store is gone after Stop; later dumps need no drain.</summary>
    internal void ForgetStore() => LiveStore = null;

    /// <summary>As RunLifecycleModule does: enter the run, then observe its server run id.</summary>
    internal void Activate(IBppEventBus bus, string runId)
    {
        if (!Context.IsInGameRun)
        {
            Context.IsInGameRun = true;
            Context.LastRunExitKind = RunExitKind.Completed;
            bus.Publish(
                new RunLifecycleChanged
                {
                    IsInGameRun = true,
                    LastRunExitKind = RunExitKind.Completed,
                    Reason = "Run started",
                }
            );
        }
        Context.CurrentServerRunId = runId;
        bus.Publish(new RunInitializedObserved { RunId = runId });
    }

    /// <summary>As RunLifecycleModule does on RunEnded or RunInterrupted.</summary>
    internal void EndRun(IBppEventBus bus, RunExitKind kind)
    {
        Context.CurrentServerRunId = null;
        Context.LastRunExitKind = kind;
        Context.IsInGameRun = false;
        bus.Publish(
            new RunLifecycleChanged
            {
                IsInGameRun = false,
                LastRunExitKind = kind,
                Reason =
                    kind == RunExitKind.Interrupted
                        ? RunLifecycleReasons.RunInterrupted
                        : RunLifecycleReasons.RunEnded,
            }
        );
    }

    /// <summary>As ReplayPersistenceOrchestrator does: save the battle, then announce it.</summary>
    internal void RecordBattle(IBppEventBus bus, string battleId, string? runId, int day, int hour)
    {
        var manifest = new PvpBattleManifest
        {
            BattleId = battleId,
            RunId = runId,
            RecordedAtUtc = Clock.Now,
            CombatKind = "PVPCombat",
            Day = day,
            Hour = hour,
            EncounterId = "encounter-" + battleId,
            Participants = new PvpBattleParticipants
            {
                PlayerName = "Player",
                PlayerAccountId = "account-001",
                PlayerHero = "Vanessa",
                OpponentName = "Rival " + battleId,
                OpponentHero = "Pygmalien",
                OpponentAccountId = "account-002",
            },
            Outcome = new PvpBattleOutcome { Result = "Win" },
        };
        Catalog.Save(manifest);
        bus.Publish(new PvpBattleRecorded { Manifest = manifest });
    }

    internal void Record(HistoryDatabaseArtifacts artifacts, int number, string name)
    {
        Drain();
        artifacts.RecordStep(number, name, Database);
    }

    internal string? Status(string runId)
    {
        Drain();
        return Text($"SELECT status FROM runs WHERE run_id='{runId}'");
    }

    internal long Count(string sql)
    {
        Drain();
        return HistoryDatabaseArtifacts.Count(Database, sql);
    }

    internal string? Text(string sql)
    {
        Drain();
        return HistoryDatabaseArtifacts.Text(Database, sql);
    }

    internal void Execute(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={Database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    // QueuedRunLogStore writes events and checkpoints on its worker. Its terminal writes drain the
    // queue first (DrainPendingWrites); one aimed at a run id with no row changes nothing else.
    private void Drain() =>
        LiveStore?.MarkRunAbandoned("drain-barrier-without-row", new RunLogAbandonment());
}

internal sealed class TestPaths(string root) : IPathProvider
{
    public string? DataRootDirectoryPath => root;
    public string? PluginsDirectoryPath => root;
}

internal sealed class TestRunContext : IRunContext
{
    public bool IsInGameRun { get; set; }
    public string? CurrentServerRunId { get; set; }
    public RunExitKind LastRunExitKind { get; set; }
    public RunVictoryOutcome LastVictoryOutcome { get; set; }
    public string LastMessageId { get; set; } = string.Empty;
}

internal sealed class TestSnapshotProbe : IRunSnapshotProbe
{
    internal RunBasicsSnapshot Basics { get; set; } = new();
    internal PlayerStatsSnapshot Stats { get; set; } =
        new()
        {
            MaxHealth = 300,
            Prestige = 20,
            Level = 3,
            Income = 5,
            Gold = 10,
        };
    internal RankSnapshot Rank { get; set; } = new() { Rank = "Gold 2", Rating = 1420 };

    public bool TryGetRunBasics(out RunBasicsSnapshot basics)
    {
        basics = Basics;
        return true;
    }

    public bool TryGetPlayerStats(out PlayerStatsSnapshot stats)
    {
        stats = Stats;
        return true;
    }

    public bool TryGetRankSnapshot(out RankSnapshot rank)
    {
        rank = Rank;
        return true;
    }

    public bool TryGetLeaderboardPosition(out int? position)
    {
        position = null;
        return false;
    }
}

internal sealed class ManualClock(DateTimeOffset start)
{
    internal DateTimeOffset Now { get; private set; } = start;

    internal event Action? Advanced;

    internal void Advance(TimeSpan elapsed)
    {
        Now += elapsed;
        Advanced?.Invoke();
    }
}

/// <summary>The deferred-completion timer: callbacks fire when the clock passes their due time.</summary>
internal sealed class ManualScheduler
{
    private readonly ManualClock _clock;
    private readonly List<Scheduled> _pending = [];

    internal ManualScheduler(ManualClock clock)
    {
        _clock = clock;
        _clock.Advanced += () => Fire(entry => entry.Due <= _clock.Now);
    }

    internal IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var entry = new Scheduled(_clock.Now + delay, callback);
        _pending.Add(entry);
        return entry;
    }

    /// <summary>A one-shot timer may fire before the wall-clock deadline.</summary>
    internal void FireEarly() => Fire(_ => true);

    private void Fire(Func<Scheduled, bool> due)
    {
        foreach (var entry in _pending.ToArray())
        {
            if (entry.Disposed || !due(entry))
                continue;
            entry.Dispose();
            entry.Callback();
        }
        _pending.RemoveAll(entry => entry.Disposed);
    }

    private sealed class Scheduled(DateTimeOffset due, Action callback) : IDisposable
    {
        internal DateTimeOffset Due { get; } = due;
        internal Action Callback { get; } = callback;
        internal bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
