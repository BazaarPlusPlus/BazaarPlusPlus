#nullable enable
using System.Runtime.CompilerServices;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.RunLog.Replication;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class RunLoggingPipelineTests : IDisposable
{
#if BPP_NET10_BINDING
    private const string SharedTarget = SharedTargetPin.Net10;
#else
    private const string SharedTarget = SharedTargetPin.Desktop;
#endif

    private readonly List<string> _logs = new();
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "bpp-run-logging-pipeline-" + Guid.NewGuid().ToString("N")
    );

    public RunLoggingPipelineTests()
    {
        BppLog.Install(new ListLogSink(_logs));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void SharedProjectsLoadTheBindingOutput() => RequireSharedTarget();

    [Fact]
    public void AFailedStartDisposesTheOwnedStore() =>
        StartFailureDisposesTheOwnedStore(Path.Combine(_root, "start-failure"));

    [Fact]
    public void RunLoggingReproducesTheHistoryGoldens()
    {
        // A wrong binding must never rewrite the goldens, so the pin guards this test too.
        RequireSharedTarget();
        // xUnit shows no console output, so the schema and rows diffs go into the failure message.
        var errors = new StringWriter();
        var stderr = Console.Error;
        Console.SetError(errors);
        var artifacts = new HistoryDatabaseArtifacts();
        Exception? failure = null;
        try
        {
            try
            {
                RunScenario(new Harness(Path.Combine(_root, "history")), artifacts, _logs);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                SqliteConnection.ClearAllPools();
            }

            artifacts.CompareRows(scenarioCompleted: failure == null);
        }
        finally
        {
            Console.SetError(stderr);
        }
        if (failure != null)
            Xunit.Assert.Fail($"{errors}{failure}");
        Xunit.Assert.False(artifacts.Failed, errors.ToString());
    }

    [Fact]
    public void RankedRunsWaitForTheRankRefresh() =>
        RankedRunWaitsForTheRankRefresh(new Harness(Path.Combine(_root, "rank-refresh")), _logs);

    [Fact]
    public void QuickNextRunActivationCompletesDeferredRankedRun()
    {
        var h = new Harness(Path.Combine(_root, "next-run-handoff"));
        var bus = new InMemoryBppEventBus();
        var completed = new List<(string RunId, string? Status)>();
        bus.Subscribe<RunLogRunCompleted>(done =>
            completed.Add((done.RunId, h.Status(done.RunId)))
        );
        var module = h.CreateModule(bus, observeRankRefresh: true);
        module.Start();

        h.Probe.Basics = Basics(day: 1, hour: 0, victories: 0, losses: 0);
        h.Activate(bus, "run-a");
        h.Probe.Basics = Basics(day: 10, hour: 6, victories: 10, losses: 1);
        h.EndRun(bus, RunExitKind.Completed);
        Check.That(
            h.Status("run-a") == "active" && completed.Count == 0,
            "Run A waits for rank refresh before completing."
        );

        // Player quickly starts Run B inside the 15s rank-refresh window.
        h.Clock.Advance(TimeSpan.FromSeconds(3));
        bus.Publish(
            new RunMessageStreamFrameObserved
            {
                CommandNumber = 10,
                Command = "SendInitializeRun",
                CommandArgsJson = "{\"playmode\":\"Ranked\"}",
                StartsRun = true,
            }
        );
        bus.Publish(
            new RunMessageStreamFrameObserved { Message = [7, 8, 9], RespondsToCommand = 10 }
        );
        h.Probe.Basics = Basics(day: 1, hour: 0, victories: 0, losses: 0);
        h.Activate(bus, "run-b");

        Check.That(
            h.Status("run-a") == "completed",
            "Starting Run B during deferred completion must complete Run A instead of abandoning it."
        );
        Check.That(
            completed.Count == 1
                && completed[0].RunId == "run-a"
                && completed[0].Status == "completed",
            "RunLogRunCompleted fires exactly once for Run A."
        );
        Check.That(
            _logs.Any(line =>
                line.Contains("run_logging.run.completion_degraded", StringComparison.Ordinal)
                && line.Contains("next_run_activated", StringComparison.Ordinal)
            ),
            "Forced next-run handoff logs next_run_activated instead of shutdown_forced."
        );
        Check.That(
            h.Status("run-b") == "active"
                && h.Text(
                    "SELECT group_concat(kind, ',') FROM (SELECT kind FROM run_events WHERE run_id='run-b' ORDER BY seq)"
                ) == "run_started,stream_command,stream_message"
                && h.Count(
                    "SELECT json_extract(payload_json, '$.responds_to_seq') FROM run_events WHERE run_id='run-b' AND seq=3"
                ) == 2,
            "Run B activates cleanly and receives its own buffered initialize frames."
        );

        module.Stop();
    }

    [Fact]
    public void SameRunReactivationCancelsDeferredCompletion()
    {
        var h = new Harness(Path.Combine(_root, "same-run-reactivation"));
        var bus = new InMemoryBppEventBus();
        var completed = new List<string>();
        bus.Subscribe<RunLogRunCompleted>(done => completed.Add(done.RunId));
        var module = h.CreateModule(bus, observeRankRefresh: true);
        module.Start();

        h.Probe.Basics = Basics(day: 1, hour: 0, victories: 0, losses: 0);
        h.Activate(bus, "run-same");
        h.EndRun(bus, RunExitKind.Completed);
        h.Clock.Advance(TimeSpan.FromSeconds(2));
        h.Activate(bus, "run-same");
        h.Clock.Advance(TimeSpan.FromSeconds(20));

        Check.That(
            h.Status("run-same") == "active" && completed.Count == 0,
            "Reactivating the same run cancels its deferred completion."
        );
        module.Stop();
    }

    [Fact]
    public void PveCombatSummaryInterleavesWithMessageStreamAndSurvivesStreamCleanup()
    {
        var h = new Harness(Path.Combine(_root, "pve-summary"));
        var bus = new InMemoryBppEventBus();
        var module = h.CreateModule(bus);
        module.Start();

        h.Probe.Basics = Basics(day: 1, hour: 1, victories: 0, losses: 0);
        h.Activate(bus, "run-pve");
        bus.Publish(new RunMessageStreamFrameObserved { Message = [1, 1] });
        bus.Publish(
            new RunMessageStreamFrameObserved
            {
                PveCombat = new PveCombatSummary
                {
                    EncounterId = "monster-pyro",
                    Day = 1,
                    Hour = 3,
                    Winner = "Player",
                    Loser = "Opponent",
                    FrameCount = 140,
                    OpponentHealthThresholdsForGold = [0.5f, 0.0f],
                    OpponentHealthThresholdsForXp = [0.0f],
                    GoldThresholdsHit = 2,
                    XpThresholdsHit = 1,
                    CardStats = new Dictionary<string, Dictionary<string, int>>
                    {
                        ["itm_1"] = new() { ["DamageDealt"] = 250 },
                    },
                },
            }
        );
        bus.Publish(new RunMessageStreamFrameObserved { Message = [2, 2] });
        Check.That(
            module.FlushPendingWrites(TimeSpan.FromSeconds(2)),
            "FlushPendingWrites drains queued events."
        );

        Check.That(
            h.Text(
                "SELECT group_concat(kind, ',') FROM (SELECT kind FROM run_events WHERE run_id='run-pve' ORDER BY seq)"
            ) == "run_started,stream_message,pve_combat_recorded,stream_message",
            "pve_combat_recorded sits on the shared seq axis between opening and closing GameSim."
        );
        Check.That(
            h.Text(
                "SELECT json_extract(payload_json, '$.winner') FROM run_events WHERE run_id='run-pve' AND kind='pve_combat_recorded'"
            ) == "Player"
                && h.Count(
                    "SELECT json_extract(payload_json, '$.duration_ms') FROM run_events WHERE run_id='run-pve' AND kind='pve_combat_recorded'"
                ) == 7000
                && h.Count(
                    "SELECT json_extract(payload_json, '$.gold_thresholds_hit') FROM run_events WHERE run_id='run-pve' AND kind='pve_combat_recorded'"
                ) == 2
                && h.Count(
                    "SELECT json_extract(payload_json, '$.card_stats.itm_1.DamageDealt') FROM run_events WHERE run_id='run-pve' AND kind='pve_combat_recorded'"
                ) == 250,
            "pve_combat_recorded persists winner, duration, threshold hits, and card_stats."
        );
        module.Stop();
    }

    [Fact]
    public void TryFlushPendingWritesTimeoutDoesNotThrowWhenWorkerCompletesLater()
    {
        var entered = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var failures = new List<RunLogStoreDiagnostic>();
        using var store = new QueuedRunLogStore(
            new BlockingRunLogStore(entered, release),
            new CallbackLogger(failures.Add)
        );
        store.AppendEvent("run-slow", new RunLogEvent { Kind = "test", Seq = 1 });
        Check.That(entered.Wait(TimeSpan.FromSeconds(2)), "Worker started the blocked write.");
        Check.That(
            !store.TryFlushPendingWrites(TimeSpan.FromMilliseconds(30)),
            "Flush returns false on timeout without throwing."
        );
        release.Set();
        Check.That(
            store.TryFlushPendingWrites(TimeSpan.FromSeconds(2)),
            "Subsequent flush succeeds after blocked write finishes."
        );
        Check.That(
            failures.Count == 0,
            "Late completion of a timed-out flush barrier must not throw ObjectDisposedException."
        );
    }

    private static void RequireSharedTarget()
    {
        if (SharedTargetPin.Mismatch(SharedTarget) is { } mismatch)
            Xunit.Assert.Fail(mismatch);
    }

    private static void RunScenario(
        Harness h,
        HistoryDatabaseArtifacts artifacts,
        List<string> logs
    )
    {
        var bus = new InMemoryBppEventBus();
        var completed = new List<(string RunId, string? Status)>();
        bus.Subscribe<RunLogRunCompleted>(done =>
            completed.Add((done.RunId, h.Status(done.RunId)))
        );
        var module = h.CreateModule(bus);
        Check.That(h.StoresCreated == 0, "1: constructing the module must not create its store.");
        module.Start();
        module.Start();
        Check.That(h.StoresCreated == 1, "1: Start creates the store exactly once.");
        artifacts.RecordSchema(h.Database);
        h.Record(artifacts, 1, "start");

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        h.Probe.Basics = Basics(day: 1, hour: 0, victories: 0, losses: 0);
        h.PlayerAccountId = null;
        bus.Publish(new RunMessageStreamFrameObserved { Message = [9, 9, 9] });
        bus.Publish(
            new RunMessageStreamFrameObserved
            {
                CommandNumber = 1,
                Command = "SendInitializeRun",
                CommandArgsJson = "{\"playmode\":\"Ranked\"}",
                StartsRun = true,
            }
        );
        bus.Publish(
            new RunMessageStreamFrameObserved { Message = [1, 2, 3], RespondsToCommand = 1 }
        );
        h.Activate(bus, "run-alpha");
        Check.That(
            h.Text("SELECT player_account_id FROM runs WHERE run_id='run-alpha'") == null,
            "2: a run created before the account is known stores no account."
        );
        h.PlayerAccountId = "account-001";
        h.Clock.Advance(TimeSpan.FromSeconds(5));
        h.Activate(bus, "run-alpha");
        h.Record(artifacts, 2, "run-created");
        Check.That(
            h.Count(
                "SELECT COUNT(*) FROM run_events WHERE run_id='run-alpha' AND kind='run_started'"
            ) == 1,
            "2: a repeated initialization of the same run appends no second run_started."
        );
        Check.That(
            h.Text(
                "SELECT group_concat(kind, ',') FROM (SELECT kind FROM run_events WHERE run_id='run-alpha' ORDER BY seq)"
            ) == "run_started,stream_command,stream_message"
                && h.Count(
                    "SELECT json_extract(payload_json, '$.responds_to_seq') FROM run_events WHERE run_id='run-alpha' AND seq=3"
                ) == 2,
            "2: the held command and its answer follow the start event; the earlier frame is dropped."
        );
        Check.That(
            h.Text("SELECT player_account_id FROM runs WHERE run_id='run-alpha'") == "account-001",
            "2: an account resolved after the run row exists is written to it."
        );

        h.PlayerAccountId = "account-other";
        h.Clock.Advance(TimeSpan.FromMinutes(3));
        h.Probe.Basics = Basics(day: 2, hour: 1, victories: 1, losses: 0);
        h.Probe.Stats = Stats(gold: 14);
        bus.Publish(
            new RunMessageStreamFrameObserved
            {
                CommandNumber = 2,
                Command = "SendSelectItem",
                CommandArgsJson = "{\"instanceId\":\"itm_a\"}",
            }
        );
        bus.Publish(new RunMessageStreamFrameObserved { Message = [4, 5], RespondsToCommand = 2 });
        h.RecordBattle(bus, "battle-alpha-1", h.Context.CurrentServerRunId, day: 2, hour: 1);
        h.Clock.Advance(TimeSpan.FromMinutes(3));
        // A replay captured before the run id reached the run context carries no run id.
        h.RecordBattle(bus, "battle-alpha-2", runId: null, day: 2, hour: 3);
        h.RecordBattle(bus, "battle-elsewhere", "run-elsewhere", day: 2, hour: 3);
        bus.Publish(
            new PvpBattleRecorded
            {
                BattleId = "battle-pve",
                RunId = "run-alpha",
                CombatKind = "Combat",
            }
        );
        h.Record(artifacts, 3, "pvp-recorded");
        Check.That(
            h.Text("SELECT run_id FROM battles WHERE battle_id='battle-alpha-2'") == "run-alpha"
                && h.Text("SELECT run_id FROM battles WHERE battle_id='battle-elsewhere'") == null,
            "3: the module attaches a run-less PvP battle to the active run and ignores a mismatch."
        );
        Check.That(
            h.Count("SELECT last_seq FROM runs WHERE run_id='run-alpha'") == 7
                && h.Count("SELECT MAX(seq) FROM run_events WHERE run_id='run-alpha'") == 7
                && h.Count(
                    "SELECT json_extract(payload_json, '$.responds_to_seq') FROM run_events WHERE run_id='run-alpha' AND seq=5"
                ) == 4,
            "3: run_events.seq counts from 1 and runs.last_seq follows it."
        );
        Check.That(
            h.Text("SELECT player_account_id FROM runs WHERE run_id='run-alpha'") == "account-001",
            "3: the first account written to a run is never replaced."
        );
        h.PlayerAccountId = "account-001";

        h.Clock.Advance(TimeSpan.FromMinutes(2));
        h.Probe.Basics = Basics(day: 3, hour: 2, victories: 2, losses: 1);
        h.Probe.Rank = new RankSnapshot { Rank = "Gold 1", Rating = 1436 };
        h.PendingReplayPersistence = true;
        h.EndRun(bus, RunExitKind.Completed);
        h.RecordBattle(bus, "battle-alpha-3", "run-alpha", day: 3, hour: 2);
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        bus.Publish(new CombatReplayPersistenceDrained());
        Check.That(
            h.Status("run-alpha") == "active" && completed.Count == 0,
            "4: completion waits while replay persistence is pending, and reports nothing yet."
        );
        h.PendingReplayPersistence = false;
        bus.Publish(new CombatReplayPersistenceDrained());
        Check.That(h.Status("run-alpha") == "completed", "4: a drain completes the run at once.");
        Check.That(
            completed.SequenceEqual([("run-alpha", (string?)"completed")]),
            "4: the drained completion is reported once, after its row reads completed."
        );

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        h.Probe.Basics = Basics(day: 1, hour: 0, victories: 0, losses: 0);
        h.Activate(bus, "run-bravo");
        h.PendingReplayPersistence = true;
        h.EndRun(bus, RunExitKind.Completed);
        h.Context.IsInGameRun = true;
        h.Context.CurrentServerRunId = "run-bravo";
        h.Clock.Advance(TimeSpan.FromSeconds(2));
        bus.Publish(new RunInitializedObserved { RunId = "run-bravo", BuildId = Harness.BuildId });
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
        bus.Publish(
            new RunInitializedObserved { RunId = "run-charlie", BuildId = Harness.BuildId }
        );
        h.EndRun(bus, RunExitKind.Completed);
        h.Scheduler.FireEarly();
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        Check.That(
            h.Status("run-charlie") == "active" && completed.Count == 1,
            "4: a replacement gets its own full grace period, and an early timer re-arms."
        );
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        Check.That(
            h.Status("run-charlie") == "completed"
                && logs.Any(log =>
                    log.Contains(
                        "event=run_logging.run.completion_degraded",
                        StringComparison.Ordinal
                    ) && log.Contains("replay_drain_timeout", StringComparison.Ordinal)
                ),
            "4: the deadline completes the run as a replay-drain timeout."
        );
        Check.That(
            completed.SequenceEqual([
                ("run-alpha", (string?)"completed"),
                ("run-charlie", (string?)"completed"),
            ]),
            "4: the deadline completion is reported once; resumed and replaced runs are not."
        );
        h.PendingReplayPersistence = false;
        h.Record(artifacts, 4, "completion");

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
        // Subscribe before the module so Publish can stop it before invoking its snapshotted handler.
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

    private static void RankedRunWaitsForTheRankRefresh(Harness h, List<string> logs)
    {
        var bus = new InMemoryBppEventBus();
        var completed = new List<string>();
        bus.Subscribe<RunLogRunCompleted>(done => completed.Add(done.RunId));
        var module = h.CreateModule(bus, observeRankRefresh: true);
        module.Start();
        Check.That(h.RankListeners.Count == 1, "9: Start subscribes to rank refreshes once.");

        // The game still caches the starting rating when the Run exits.
        h.Probe.Rank = new RankSnapshot { Rank = "Legendary", Rating = 1052 };
        h.Probe.Basics = Basics(day: 1, hour: 0, victories: 0, losses: 0);
        h.Activate(bus, "run-ranked");
        h.Clock.Advance(TimeSpan.FromMinutes(20));
        h.Probe.Basics = Basics(day: 12, hour: 5, victories: 8, losses: 10);
        h.EndRun(bus, RunExitKind.Completed);
        Check.That(
            h.Status("run-ranked") == "active" && completed.Count == 0,
            "9: a Ranked Run waits for the end-of-run rank refresh."
        );
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        h.RefreshRank(new RankSnapshot { Rank = "Legendary", Rating = 1046 });
        Check.That(
            h.Status("run-ranked") == "completed"
                && h.Count("SELECT final_player_rating FROM runs WHERE run_id='run-ranked'") == 1046
                && h.Count("SELECT final_player_rating_delta FROM runs WHERE run_id='run-ranked'")
                    == -6
                && completed.SequenceEqual(["run-ranked"]),
            "9: the refreshed rating is the Run's final rating, and the Run is reported once."
        );
        h.RefreshRank(new RankSnapshot { Rank = "Legendary", Rating = 1100 });
        Check.That(
            h.Count("SELECT final_player_rating FROM runs WHERE run_id='run-ranked'") == 1046,
            "9: a refresh after the completion is written changes nothing."
        );

        h.Probe.Rank = new RankSnapshot { Rank = "Legendary", Rating = 1046 };
        h.Probe.Basics = Basics(day: 1, hour: 0, victories: 0, losses: 0);
        h.Activate(bus, "run-unrefreshed");
        h.EndRun(bus, RunExitKind.Completed);
        h.Clock.Advance(TimeSpan.FromSeconds(14));
        Check.That(
            h.Status("run-unrefreshed") == "active",
            "9: the rank refresh has a grace period."
        );
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        Check.That(
            h.Status("run-unrefreshed") == "completed"
                && h.Count("SELECT final_player_rating FROM runs WHERE run_id='run-unrefreshed'")
                    == 1046
                && logs.Any(log => log.Contains("rank_refresh_timeout", StringComparison.Ordinal)),
            "9: the deadline completes the Run as a rank-refresh timeout."
        );

        h.Probe.Basics = Basics(day: 1, hour: 0, victories: 0, losses: 0);
        h.Probe.Basics.GameMode = "Unranked";
        h.Activate(bus, "run-unranked");
        h.EndRun(bus, RunExitKind.Completed);
        Check.That(
            h.Status("run-unranked") == "completed",
            "9: an unranked Run completes at once."
        );

        module.Stop();
        h.ForgetStore();
        Check.That(h.RankListeners.Count == 0, "9: Stop releases the rank subscription.");
    }

    private static void StartFailureDisposesTheOwnedStore(string dataRoot)
    {
        var h = new Harness(dataRoot);
        _ = new RunLogStore(h.Paths);
        h.Execute(
            "INSERT INTO runs (run_id, started_at_ms, last_seen_at_ms, status, hero, game_mode) "
                + "VALUES ('unreadable-run', 9223372036854775807, 9223372036854775807, 'active', 'Vanessa', 'Ranked');"
        );
        var bus = new InMemoryBppEventBus();
        var module = h.CreateModule(bus);
        var failed = false;
        try
        {
            module.Start();
        }
        catch (ArgumentOutOfRangeException)
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
    private static WeakReference StartAndStop(Harness h, IBppEventBus bus)
    {
        var module = h.CreateModule(bus);
        module.Start();
        module.Stop();
        module.Stop();
        return new WeakReference(module);
    }

    private static RunBasicsSnapshot Basics(int day, int hour, int victories, int losses) =>
        new()
        {
            Day = day,
            Hour = hour,
            Victories = victories,
            Losses = losses,
            Hero = "Vanessa",
            GameMode = "Ranked",
        };

    private static PlayerStatsSnapshot Stats(int gold) =>
        new()
        {
            MaxHealth = 350,
            Prestige = 20,
            Level = 4,
            Income = 6,
            Gold = gold,
        };
}

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
        // The game's data manifest as the game writes it: ETags keep their HTTP quotes.
        GameDataCache = Path.Combine(dataRoot, "game-data-cache");
        Directory.CreateDirectory(GameDataCache);
        File.WriteAllText(
            Path.Combine(GameDataCache, GameDataManifest.FileName),
            """{"Entries":{"maintenance":{"ETag":"\"ffffffffffffffffffffffffffffffff\""},"GameData":{"ETag":"\"0123456789abcdef0123456789abcdef\""}}}"""
        );
    }

    internal const string BuildId = "server-build-13176";
    internal string GameDataCache { get; }

    internal IPathProvider Paths { get; }
    internal string Database { get; }
    internal ManualClock Clock { get; }
    internal ManualScheduler Scheduler { get; }
    internal PvpBattleSqliteStore Catalog { get; }
    internal TestRunContext Context { get; } = new();
    internal TestSnapshotProbe Probe { get; } = new();
    internal bool PendingReplayPersistence { get; set; }
    internal string? PlayerAccountId { get; set; } = "account-001";
    internal int StoresCreated { get; private set; }
    internal QueuedRunLogStore? LiveStore { get; private set; }

    internal List<Action<RankSnapshot>> RankListeners { get; } = [];

    internal RunLoggingModule CreateModule(IBppEventBus bus, bool observeRankRefresh = false) =>
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
            Catalog.AttachToRun,
            () => PendingReplayPersistence,
            () => Clock.Now,
            Database,
            Scheduler.Schedule,
            playerAccountIdResolver: () => PlayerAccountId,
            bundleScreenshotRequestedResolver: () => true,
            modVersionResolver: () => "5.0.0",
            subscribeRankRefreshed: observeRankRefresh ? SubscribeRankRefreshed : null,
            gameDataEtagResolver: () => GameDataManifest.ReadGameDataEtag(GameDataCache)
        );

    internal void RefreshRank(RankSnapshot rank)
    {
        Probe.Rank = rank;
        foreach (var listener in RankListeners.ToArray())
            listener(rank);
    }

    private IDisposable SubscribeRankRefreshed(Action<RankSnapshot> listener)
    {
        RankListeners.Add(listener);
        return new Unsubscribe(() => RankListeners.Remove(listener));
    }

    private sealed class Unsubscribe(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    internal void ForgetStore() => LiveStore = null;

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
        bus.Publish(new RunInitializedObserved { RunId = runId, BuildId = BuildId });
    }

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

    internal void RecordBattle(IBppEventBus bus, string battleId, string? runId, int day, int hour)
    {
        var record = new PvpBattleRecord
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
        Catalog.Save(record);
        bus.Publish(
            new PvpBattleRecorded
            {
                BattleId = record.BattleId,
                RunId = record.RunId,
                CombatKind = record.CombatKind,
                Day = record.Day,
                Hour = record.Hour,
                EncounterId = record.EncounterId,
                OpponentName = record.Participants.OpponentName,
            }
        );
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

internal sealed class ListLogSink(List<string> lines) : IBppLogSink
{
    public void Write(BppLogSeverity severity, string message) => lines.Add(message);
}

internal sealed class CallbackLogger(Action<RunLogStoreDiagnostic> onDiagnostic)
    : IRunLogStoreLogger
{
    public void Emit(RunLogStoreDiagnostic diagnostic) => onDiagnostic(diagnostic);
}

internal sealed class BlockingRunLogStore(
    ManualResetEventSlim entered,
    ManualResetEventSlim release
) : IRunLogStore
{
    public RunLogSessionState? TryResumeActiveRun() => null;

    public RunLogSessionState CreateRun(RunLogCreateRequest request) =>
        new() { RunId = request.RunId, StartedAtUtc = request.StartedAtUtc };

    public void SetPlayerAccountIdOnce(string runId, string? playerAccountId) { }

    public void AppendEvent(string runId, RunLogEvent entry)
    {
        entered.Set();
        release.Wait(TimeSpan.FromSeconds(5));
    }

    public void SaveCheckpoint(string runId, RunLogCheckpoint checkpoint) { }

    public void CompleteRun(string runId, RunLogCompletion completion) { }

    public void MarkRunAbandoned(string runId, RunLogAbandonment abandonment) { }
}

internal sealed class TestPaths(string root) : IPathProvider
{
    public string? DataRootDirectoryPath => root;
}

internal sealed class TestRunContext : IRunContext
{
    public bool IsInGameRun { get; set; }
    public string? CurrentServerRunId { get; set; }
    public RunExitKind LastRunExitKind { get; set; }
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

    // A one-shot timer can fire before the wall-clock deadline.
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
