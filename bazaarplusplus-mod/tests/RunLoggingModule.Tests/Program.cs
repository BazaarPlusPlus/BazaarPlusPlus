#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Storage.RunLog;
using BepInEx.Logging;

using var logSource = new ManualLogSource("run-logging-tests");
var capturedLogs = new List<LogEventArgs>();
logSource.LogEvent += (_, args) => capturedLogs.Add(args);
BppLog.Install(logSource);

TestStartStopAreIdempotentAndStopUnsubscribes();
TestStoreCreationIsDeferredUntilStart();
TestStartFailureRollsBackOwnedStoreAndSubscriptions();
TestRestoredRunResumesOnceAndDifferentRunAbandonsBeforeStart();
TestPvpManifestAttachesBeforeEventAndCheckpoint(capturedLogs);
TestReusedTerminalRunIdGetsIndependentLocalIdentity(capturedLogs);
TestCapturedHandlerCannotCrossStopBoundary();
TestInterruptedRunResumesOrAbandonsByStableRunId();
TestDeferredCompletionWaitsForDrainAndStopFailurePreservesActiveRun(capturedLogs);
TestDeadlineCannotCompleteAResumedOrReplacementRun();

Console.WriteLine("RunLogging module checks passed.");

static void TestStartStopAreIdempotentAndStopUnsubscribes()
{
    var fixture = new Fixture();
    fixture.Module.Start();
    fixture.Module.Start();

    Assert(
        fixture.Bus.Count<RunLifecycleChanged>() == 1
            && fixture.Bus.Count<PvpBattleRecorded>() == 1
            && fixture.Bus.Count<RunInitializedObserved>() == 1
            && fixture.Bus.Count<CombatReplayPersistenceDrained>() == 1,
        "Starting twice must retain exactly one subscription for each input event."
    );

    fixture.Activate("run-one");
    fixture.Activate("run-one");

    Assert(
        fixture.Store.Events.Select(entry => entry.Kind).SequenceEqual(["run_started"]),
        "Starting twice must still append exactly one run_started event."
    );
    Assert(fixture.Store.Checkpoints.Count == 1, "The started event must checkpoint once.");

    fixture.Module.Stop();
    fixture.Module.Stop();
    Assert(fixture.Bus.TotalSubscriptionCount == 0, "Stop must remove all four subscriptions.");
    fixture.Activate("run-after-stop");

    Assert(fixture.Store.Events.Count == 1, "Stop must remove every event subscription.");
    Assert(fixture.Store.DisposeCount == 1, "Stop must dispose the owned store exactly once.");
}

static void TestStoreCreationIsDeferredUntilStart()
{
    var calls = new List<string>();
    var store = new FakeRunLogStore(calls, restored: null);
    var creationCount = 0;
    var module = new RunLoggingModule(
        new RecordingEventBus(),
        new FakeRunContext(),
        new FakeRunSnapshotProbe(),
        "Online",
        () =>
        {
            creationCount++;
            return store;
        },
        new FakePvpBattleCatalog(calls),
        static () => false
    );

    Assert(
        creationCount == 0,
        "Constructing the composition-owned feature must not start its store worker."
    );
    module.Start();
    Assert(creationCount == 1, "Start must create the owned store exactly once.");
    module.Start();
    Assert(creationCount == 1, "An idempotent Start must not recreate the store.");
    module.Stop();
}

static void TestStartFailureRollsBackOwnedStoreAndSubscriptions()
{
    var fixture = new Fixture();
    fixture.Bus.ThrowOnSubscriptionNumber = 3;

    AssertThrows<IOException>(fixture.Module.Start);

    Assert(
        fixture.Bus.TotalSubscriptionCount == 0,
        "A partial Start failure must roll back every subscription already installed."
    );
    Assert(
        fixture.Store.DisposeCount == 1,
        "A partial Start failure must dispose the store owned by the module."
    );
}

static void TestRestoredRunResumesOnceAndDifferentRunAbandonsBeforeStart()
{
    var restored = new RunLogSessionState
    {
        RunId = "restored-run",
        SchemaVersion = 1,
        StartedAtUtc = Fixture.InitialNow,
        LastSeenAtUtc = Fixture.InitialNow,
        LastSeq = 4,
        Day = 7,
        Hour = 2,
    };
    var fixture = new Fixture(restored);
    fixture.Module.Start();
    fixture.Store.Calls.Clear();

    fixture.Activate("restored-run");
    fixture.Activate("restored-run");

    Assert(
        fixture.Store.Events.Select(entry => entry.Kind).SequenceEqual(["run_resumed"]),
        "A restored active run must append run_resumed exactly once."
    );
    Assert(fixture.Store.Events[0].Seq == 5, "Resume must continue the stored sequence.");
    Assert(fixture.Store.Checkpoints.Count == 1, "Resume must checkpoint immediately.");

    fixture.Store.Calls.Clear();
    fixture.Activate("replacement-run");

    Assert(
        fixture.Store.Calls.SequenceEqual([
            "abandon:session_mismatch",
            "create:replacement-run",
            "append:run_started",
            "checkpoint",
        ]),
        "A different server run id must abandon the restored session before creating and checkpointing the replacement."
    );
    Assert(
        fixture.Store.Events[^1].RunId == "replacement-run",
        "The replacement started event must use the new stable run id."
    );
    fixture.Module.Stop();
}

static void TestPvpManifestAttachesBeforeEventAndCheckpoint(List<LogEventArgs> capturedLogs)
{
    var fixture = new Fixture();
    fixture.Module.Start();
    fixture.Activate("run-pvp");
    fixture.Store.Calls.Clear();

    var manifest = new PvpBattleManifest
    {
        BattleId = "battle-123",
        RunId = "run-pvp",
        CombatKind = "PVPCombat",
        Day = 4,
        Hour = 6,
        EncounterId = "encounter-template-id",
        Participants = new PvpBattleParticipants { OpponentName = "Rival" },
    };
    fixture.Bus.Publish(new PvpBattleRecorded { Manifest = manifest });

    Assert(
        fixture.Store.Calls.SequenceEqual([
            "attach:battle-123:run-pvp",
            "append:pvp_combat_recorded",
            "checkpoint",
        ]),
        "A matching PvP battle must attach to the run before append and checkpoint."
    );
    var recorded = fixture.Store.Events[^1];
    Assert(
        recorded.RunId == "run-pvp"
            && recorded.BattleId == "battle-123"
            && recorded.EncounterId == "encounter-template-id"
            && recorded.OpponentName == "Rival",
        "The persisted event must retain the stable run/battle/encounter ids and opponent metadata."
    );

    fixture.Store.Calls.Clear();
    fixture.Bus.Publish(
        new PvpBattleRecorded
        {
            Manifest = new PvpBattleManifest
            {
                BattleId = "wrong-run-battle",
                RunId = "another-run",
                CombatKind = "PVPCombat",
            },
        }
    );
    fixture.Bus.Publish(
        new PvpBattleRecorded
        {
            Manifest = new PvpBattleManifest
            {
                BattleId = "non-pvp-battle",
                RunId = "run-pvp",
                CombatKind = "PVECombat",
            },
        }
    );
    Assert(fixture.Store.Calls.Count == 0, "Mismatched and non-PvP manifests must be ignored.");
    Assert(
        capturedLogs.Any(log =>
            log.Data?.ToString()?.Contains("event=run_logging.battle.capture_failed") == true
            && log.Data?.ToString()?.Contains("reason_code=in_run_mismatch") == true
        ),
        "An in-run mismatch must emit the established typed reason code."
    );
    Assert(
        capturedLogs.All(log =>
            log.Data?.ToString()?.Contains("another-run", StringComparison.Ordinal) != true
            && log.Data?.ToString()?.Contains("wrong-run-battle", StringComparison.Ordinal) != true
        ),
        "Mismatch diagnostics must retain correlation redaction rather than raw stable ids."
    );

    capturedLogs.Clear();
    fixture.Store.ThrowOnAppendEvent = true;
    fixture.Bus.Publish(
        new PvpBattleRecorded
        {
            Manifest = new PvpBattleManifest
            {
                BattleId = "write-failure-battle",
                RunId = "run-pvp",
                CombatKind = "PVPCombat",
            },
        }
    );
    Assert(
        capturedLogs.Any(log =>
            log.Data?.ToString()?.Contains("event=run_logging.battle.capture_failed") == true
            && log.Data?.ToString()?.Contains("reason_code=battle_capture_exception") == true
        ),
        "A storage failure must be contained and mapped to a typed feature diagnostic."
    );
    Assert(
        capturedLogs.All(log =>
            log.Data?.ToString()?.Contains("event=plugin.event_handler.degraded") != true
        ),
        "RunLogging handlers must not leak storage exceptions to the event bus."
    );
    fixture.Module.Stop();
}

static void TestReusedTerminalRunIdGetsIndependentLocalIdentity(List<LogEventArgs> capturedLogs)
{
    capturedLogs.Clear();
    const string serverRunId = "reused-terminal-run";
    var fixture = new Fixture();
    fixture.Store.CollisionServerRunId = serverRunId;
    fixture.Module.Start();

    fixture.Activate(serverRunId);

    var localRunId = fixture.Store.ActiveState?.RunId;
    Assert(
        localRunId != null
            && localRunId != serverRunId
            && RunLogRunIdentity.MatchesServerRunId(localRunId, serverRunId),
        "A reused terminal server run id must receive a distinct local identity."
    );
    Assert(
        fixture.Context.CurrentServerRunId == localRunId,
        "The effective local identity must flow to replay and screenshot capture."
    );
    Assert(
        fixture.Store.Events.Single().RunId == localRunId,
        "The recovered run_started event must belong to the distinct local run."
    );
    Assert(
        capturedLogs.Any(log =>
            log.Data?.ToString()?.Contains("event=run_logging.run.id_collision_recovered") == true
        ),
        "Recovering a reused server run id should emit a typed diagnostic."
    );

    fixture.Store.Calls.Clear();
    fixture.Activate(serverRunId);
    Assert(
        fixture.Store.Abandonments.Count == 0
            && fixture.Store.Events.Count == 1
            && fixture.Store.Calls.Count == 0,
        "Repeated activation of the reused server id must keep the recovered session."
    );

    var manifest = new PvpBattleManifest
    {
        BattleId = "collision-battle",
        RunId = serverRunId,
        CombatKind = "PVPCombat",
        Participants = new PvpBattleParticipants(),
    };
    fixture.Bus.Publish(new PvpBattleRecorded { Manifest = manifest });
    Assert(
        manifest.RunId == localRunId
            && fixture.Store.Calls[0] == $"attach:collision-battle:{localRunId}",
        "A replay carrying the original server id must attach to the recovered local run."
    );

    fixture.Module.Stop();
}

static void TestCapturedHandlerCannotCrossStopBoundary()
{
    var fixture = new Fixture();
    fixture.Module.Start();
    fixture.Activate("stop-boundary-run");
    fixture.Store.Calls.Clear();
    var captured = fixture.Bus.Capture<PvpBattleRecorded>();

    fixture.Module.Stop();
    captured(
        new PvpBattleRecorded
        {
            Manifest = new PvpBattleManifest
            {
                BattleId = "captured-before-stop",
                RunId = "stop-boundary-run",
                CombatKind = "PVPCombat",
            },
        }
    );

    Assert(
        fixture.Store.Calls.SequenceEqual(["dispose"]),
        "A handler snapshot captured before Stop must not perform work after store disposal."
    );
}

static void TestInterruptedRunResumesOrAbandonsByStableRunId()
{
    var fixture = new Fixture();
    fixture.Module.Start();
    fixture.Activate("interrupted-run");
    fixture.Store.Calls.Clear();

    fixture.Interrupt();
    Assert(
        fixture.Store.Abandonments.Count == 0 && fixture.Store.Completions.Count == 0,
        "An interrupted transition must remain non-terminal."
    );

    fixture.Activate("interrupted-run");
    Assert(
        fixture.Store.Abandonments.Count == 0,
        "The same stable run id must resume without abandonment."
    );

    fixture.Interrupt();
    fixture.Store.Calls.Clear();
    fixture.Activate("next-run");
    Assert(
        fixture.Store.Calls[0] == "abandon:run_interrupted",
        "A different activation after interruption must abandon the old run first."
    );
    fixture.Module.Stop();
}

static void TestDeferredCompletionWaitsForDrainAndStopFailurePreservesActiveRun(
    List<LogEventArgs> capturedLogs
)
{
    var fixture = new Fixture();
    fixture.Module.Start();
    fixture.Activate("completed-run");
    fixture.PendingReplayPersistence = true;
    fixture.CompleteRunTransition();

    Assert(
        fixture.Store.Completions.Count == 0,
        "A normal run completion must wait while replay persistence is pending."
    );
    fixture.Store.Calls.Clear();
    fixture.Bus.Publish(
        new PvpBattleRecorded
        {
            Manifest = new PvpBattleManifest
            {
                BattleId = "deferred-battle",
                RunId = "completed-run",
                CombatKind = "PVPCombat",
            },
        }
    );
    Assert(
        fixture.Store.Calls.SequenceEqual([
            "attach:deferred-battle:completed-run",
            "append:pvp_combat_recorded",
            "checkpoint",
        ]),
        "A matching deferred-run manifest must attach, append, and checkpoint before completion."
    );
    fixture.Now = fixture.Now.AddSeconds(1);
    fixture.Bus.Publish(new CombatReplayPersistenceDrained());
    Assert(
        fixture.Store.Completions.Count == 0,
        "The two-second grace deadline must not complete early."
    );
    fixture.PendingReplayPersistence = false;
    fixture.Bus.Publish(new CombatReplayPersistenceDrained());
    Assert(
        fixture.Store.Completions.Count == 1 && fixture.Store.Completions[0].Status == "completed",
        "A replay drain must complete immediately without waiting for the grace deadline."
    );
    fixture.Module.Stop();

    var timedOut = new Fixture();
    timedOut.Module.Start();
    timedOut.Activate("deadline-run");
    timedOut.PendingReplayPersistence = true;
    timedOut.CompleteRunTransition();
    capturedLogs.Clear();
    timedOut.AdvanceBy(TimeSpan.FromSeconds(2));
    Assert(
        timedOut.Store.Completions.Count == 1,
        "The two-second grace deadline must actively complete a run even without a drain event."
    );
    Assert(
        capturedLogs.Any(log =>
            log.Data?.ToString()?.Contains("event=run_logging.run.completion_degraded") == true
            && log.Data?.ToString()?.Contains("reason_code=replay_drain_timeout") == true
            && log.Data?.ToString()?.Contains("grace_ms=2000") == true
        ),
        "An active deadline timeout must retain the established typed degradation diagnostic."
    );
    timedOut.Module.Stop();

    var earlyTimer = new Fixture();
    earlyTimer.Module.Start();
    earlyTimer.Activate("early-timer-run");
    earlyTimer.PendingReplayPersistence = true;
    earlyTimer.CompleteRunTransition();
    earlyTimer.FireScheduledCallback();
    Assert(
        earlyTimer.Store.Completions.Count == 0,
        "An early one-shot callback must not complete before the wall-clock deadline."
    );
    earlyTimer.AdvanceBy(TimeSpan.FromSeconds(2));
    Assert(
        earlyTimer.Store.Completions.Count == 1,
        "An early one-shot callback must re-arm for the remaining grace period."
    );
    earlyTimer.Module.Stop();

    var forced = new Fixture();
    forced.Module.Start();
    forced.Activate("stop-success-run");
    forced.PendingReplayPersistence = true;
    forced.CompleteRunTransition();
    forced.Store.Calls.Clear();
    forced.Module.Stop();
    Assert(
        forced.Store.Calls.SequenceEqual(["complete:run_state_exit", "dispose"]),
        "Stop must force successful deferred completion before disposing the store."
    );

    var failing = new Fixture();
    failing.Module.Start();
    failing.Activate("stop-failure-run");
    failing.PendingReplayPersistence = true;
    failing.CompleteRunTransition();
    failing.Store.ThrowOnCompleteRun = true;
    capturedLogs.Clear();

    failing.Module.Stop();

    Assert(
        failing.Store.ActiveState?.RunId == "stop-failure-run",
        "A forced teardown completion failure must not clear the active run."
    );
    Assert(failing.Store.DisposeCount == 1, "Stop must still dispose the store after failure.");
    Assert(
        capturedLogs.Any(log =>
            log.Data?.ToString()?.Contains("event=run_logging.run.completion_failed") == true
            && log.Data?.ToString()?.Contains("reason_code=teardown_finalization_exception") == true
        ),
        "Forced completion failure must emit a typed diagnostic."
    );
}

static void TestDeadlineCannotCompleteAResumedOrReplacementRun()
{
    var resumed = new Fixture();
    resumed.Module.Start();
    resumed.Activate("resumed-before-deadline");
    resumed.PendingReplayPersistence = true;
    resumed.CompleteRunTransition();
    resumed.Context.IsInGameRun = true;
    resumed.Context.CurrentServerRunId = "resumed-before-deadline";

    resumed.AdvanceBy(TimeSpan.FromSeconds(2));
    Assert(
        resumed.Store.Completions.Count == 0,
        "A deadline callback must not complete a run after shared context already says it resumed."
    );
    resumed.Bus.Publish(new RunInitializedObserved { RunId = "resumed-before-deadline" });
    Assert(
        resumed.Store.Completions.Count == 0 && resumed.Store.Abandonments.Count == 0,
        "Delivery of the captured same-run activation must keep the resumed session active."
    );
    resumed.Module.Stop();

    var replacement = new Fixture();
    replacement.Module.Start();
    replacement.Activate("old-deferred-run");
    replacement.PendingReplayPersistence = true;
    replacement.CompleteRunTransition();
    replacement.Context.IsInGameRun = true;
    replacement.Context.CurrentServerRunId = "replacement-run";

    replacement.AdvanceBy(TimeSpan.FromSeconds(2));
    Assert(
        replacement.Store.Completions.Count == 0,
        "A stale deadline must not complete the old session while a replacement run is entering."
    );
    replacement.Bus.Publish(new RunInitializedObserved { RunId = "replacement-run" });
    Assert(
        replacement.Store.Abandonments.Count == 1
            && replacement.Store.Abandonments[0].Reason == "session_mismatch",
        "Replacement activation must preserve mismatch abandonment instead of terminal completion."
    );

    replacement.PendingReplayPersistence = true;
    replacement.CompleteRunTransition();
    replacement.AdvanceBy(TimeSpan.FromSeconds(1));
    Assert(
        replacement.Store.Completions.Count == 0,
        "A replacement run must receive its own full grace period rather than inherit a stale deadline."
    );
    replacement.AdvanceBy(TimeSpan.FromSeconds(1));
    Assert(
        replacement.Store.Completions.Count == 1,
        "The replacement run's own two-second deadline must still complete normally."
    );
    replacement.Module.Stop();
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

file sealed class Fixture
{
    internal static readonly DateTimeOffset InitialNow = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly List<string> _calls = [];
    private readonly ManualScheduler _scheduler = new();

    internal Fixture(RunLogSessionState? restored = null)
    {
        Bus = new RecordingEventBus();
        Context = new FakeRunContext();
        Snapshot = new FakeRunSnapshotProbe();
        Store = new FakeRunLogStore(_calls, restored);
        Catalog = new FakePvpBattleCatalog(_calls);
        Now = InitialNow.UtcDateTime;
        Module = new RunLoggingModule(
            Bus,
            Context,
            Snapshot,
            "Online",
            Store,
            Catalog,
            () => PendingReplayPersistence,
            () => Now,
            scheduleDeferredCompletion: _scheduler.Schedule
        );
    }

    internal RecordingEventBus Bus { get; }
    internal FakeRunContext Context { get; }
    internal FakeRunSnapshotProbe Snapshot { get; }
    internal FakeRunLogStore Store { get; }
    internal FakePvpBattleCatalog Catalog { get; }
    internal RunLoggingModule Module { get; }
    internal bool PendingReplayPersistence { get; set; }
    internal DateTime Now { get; set; }

    internal void AdvanceBy(TimeSpan elapsed)
    {
        Now = Now.Add(elapsed);
        _scheduler.AdvanceBy(elapsed);
    }

    internal void FireScheduledCallback() => _scheduler.FireNext();

    internal void Activate(string runId)
    {
        Context.CurrentServerRunId = runId;
        Context.IsInGameRun = true;
        Bus.Publish(new RunInitializedObserved { RunId = runId });
    }

    internal void Interrupt()
    {
        Context.IsInGameRun = false;
        Context.LastRunExitKind = RunExitKind.Interrupted;
        Bus.Publish(
            new RunLifecycleChanged
            {
                IsInGameRun = false,
                LastRunExitKind = RunExitKind.Interrupted,
                Reason = RunLifecycleReasons.RunInterrupted,
            }
        );
    }

    internal void CompleteRunTransition()
    {
        Context.IsInGameRun = false;
        Context.LastRunExitKind = RunExitKind.Completed;
        Bus.Publish(
            new RunLifecycleChanged
            {
                IsInGameRun = false,
                LastRunExitKind = RunExitKind.Completed,
                Reason = RunLifecycleReasons.RunEnded,
            }
        );
    }
}

file sealed class FakeRunContext : IRunContext
{
    public bool IsInGameRun { get; set; }
    public string? CurrentServerRunId { get; set; }
    public RunExitKind LastRunExitKind { get; set; }
    public RunVictoryOutcome LastVictoryOutcome { get; set; }
    public string LastMessageId { get; set; } = string.Empty;
}

file sealed class FakeRunSnapshotProbe : IRunSnapshotProbe
{
    internal RunBasicsSnapshot Basics { get; } =
        new()
        {
            Day = 3,
            Hour = 5,
            Victories = 6,
            Losses = 1,
            Hero = "Vanessa",
            GameMode = "Ranked",
        };

    internal PlayerStatsSnapshot Stats { get; } =
        new()
        {
            MaxHealth = 100,
            Prestige = 8,
            Level = 7,
            Income = 6,
            Gold = 20,
        };

    internal RankSnapshot Rank { get; } = new() { Rank = "Gold", Rating = 1800 };

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

file sealed class FakeRunLogStore : IRunLogStore, IDisposable
{
    private readonly List<string> _calls;

    internal FakeRunLogStore(List<string> calls, RunLogSessionState? restored)
    {
        _calls = calls;
        ActiveState = restored;
    }

    internal List<string> Calls => _calls;
    internal List<RunLogEvent> Events { get; } = [];
    internal List<RunLogCheckpoint> Checkpoints { get; } = [];
    internal List<RunLogCompletion> Completions { get; } = [];
    internal List<RunLogAbandonment> Abandonments { get; } = [];
    internal RunLogSessionState? ActiveState { get; private set; }
    internal bool ThrowOnCompleteRun { get; set; }
    internal bool ThrowOnAppendEvent { get; set; }
    internal string? CollisionServerRunId { get; set; }
    internal int DisposeCount { get; private set; }

    public RunLogSessionState? TryResumeActiveRun() => ActiveState;

    public RunLogSessionState CreateRun(RunLogCreateRequest request)
    {
        _calls.Add($"create:{request.RunId}");
        var effectiveRunId =
            request.RunId == CollisionServerRunId
                ? $"{request.RunId}:bpp:0123456789abcdef0123456789abcdef"
                : request.RunId;
        ActiveState = new RunLogSessionState
        {
            RunId = effectiveRunId,
            SchemaVersion = request.SchemaVersion,
            StartedAtUtc = request.StartedAtUtc,
            LastSeenAtUtc = request.StartedAtUtc,
            Day = request.Day,
            Hour = request.Hour,
        };
        return ActiveState;
    }

    public void SetPlayerAccountIdOnce(string runId, string? playerAccountId) { }

    public void AppendEvent(string runId, RunLogEvent entry)
    {
        if (ThrowOnAppendEvent)
            throw new IOException("append failed");
        _calls.Add($"append:{entry.Kind}");
        Events.Add(entry);
    }

    public void SaveCheckpoint(string runId, RunLogCheckpoint checkpoint)
    {
        _calls.Add("checkpoint");
        Checkpoints.Add(checkpoint);
    }

    public void CompleteRun(string runId, RunLogCompletion completion)
    {
        if (ThrowOnCompleteRun)
            throw new IOException("complete failed");
        _calls.Add($"complete:{completion.Reason}");
        Completions.Add(completion);
        ActiveState = null;
    }

    public void MarkRunAbandoned(string runId, RunLogAbandonment abandonment)
    {
        _calls.Add($"abandon:{abandonment.Reason}");
        Abandonments.Add(abandonment);
        ActiveState = null;
    }

    public void Dispose()
    {
        DisposeCount++;
        _calls.Add("dispose");
    }
}

file sealed class RecordingEventBus : IBppEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];
    private int _subscriptionCount;

    internal int? ThrowOnSubscriptionNumber { get; set; }

    internal int TotalSubscriptionCount => _handlers.Values.Sum(handlers => handlers.Count);

    internal int Count<TEvent>()
        where TEvent : class =>
        _handlers.TryGetValue(typeof(TEvent), out var handlers) ? handlers.Count : 0;

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : class
    {
        _subscriptionCount++;
        if (_subscriptionCount == ThrowOnSubscriptionNumber)
            throw new IOException("subscription failed");
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
        {
            handlers = [];
            _handlers.Add(typeof(TEvent), handlers);
        }
        handlers.Add(handler);
        return new Subscription(() => handlers.Remove(handler));
    }

    public void Publish<TEvent>(TEvent eventData)
        where TEvent : class
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
            return;
        foreach (var handler in handlers.ToArray())
            ((Action<TEvent>)handler)(eventData);
    }

    internal Action<TEvent> Capture<TEvent>()
        where TEvent : class
    {
        var snapshot = _handlers.TryGetValue(typeof(TEvent), out var handlers)
            ? handlers.ToArray()
            : [];
        return eventData =>
        {
            foreach (var handler in snapshot)
                ((Action<TEvent>)handler)(eventData);
        };
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            dispose();
        }
    }
}

file sealed class ManualScheduler
{
    private readonly List<ScheduledCallback> _callbacks = [];

    internal IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var scheduled = new ScheduledCallback(delay, callback);
        _callbacks.Add(scheduled);
        return scheduled;
    }

    internal void AdvanceBy(TimeSpan elapsed)
    {
        foreach (var callback in _callbacks.ToArray())
            callback.AdvanceBy(elapsed);
        _callbacks.RemoveAll(callback => callback.IsDisposed);
    }

    internal void FireNext()
    {
        var callback = _callbacks.FirstOrDefault(entry => !entry.IsDisposed);
        if (callback == null)
            throw new InvalidOperationException("No scheduled callback is available.");
        callback.Fire();
        _callbacks.RemoveAll(entry => entry.IsDisposed);
    }

    private sealed class ScheduledCallback(TimeSpan remaining, Action callback) : IDisposable
    {
        private TimeSpan _remaining = remaining;

        internal bool IsDisposed { get; private set; }

        internal void AdvanceBy(TimeSpan elapsed)
        {
            if (IsDisposed)
                return;
            _remaining -= elapsed;
            if (_remaining > TimeSpan.Zero)
                return;
            IsDisposed = true;
            callback();
        }

        internal void Fire()
        {
            if (IsDisposed)
                return;
            IsDisposed = true;
            callback();
        }

        public void Dispose() => IsDisposed = true;
    }
}

file sealed class FakePvpBattleCatalog(List<string> calls) : IPvpBattleCatalog
{
    public void Save(PvpBattleManifest manifest) { }

    public void AttachToRun(string battleId, string runId) =>
        calls.Add($"attach:{battleId}:{runId}");

    public PvpBattleManifest? TryLoad(string battleId) => null;

    public IReadOnlyList<ReplayPayloadMaintenanceRecord> ListReplayMaintenanceInventory() => [];

    public IReadOnlyList<string> ScheduleReplayPayloadDeletion(
        IReadOnlyCollection<string> battleIds,
        DateTimeOffset now
    ) => [];

    public void CompleteReplayPayloadDeletion(
        IReadOnlyCollection<string> battleIds,
        DateTimeOffset now
    ) { }

    public void MarkReplayPayloadMissing(
        IReadOnlyCollection<string> battleIds,
        DateTimeOffset now
    ) { }

    public IReadOnlyList<PvpBattleManifest> ListRecentBattles(int limit) => [];

    public IReadOnlyList<PvpBattleManifest> ListByRunId(string runId) => [];
}
