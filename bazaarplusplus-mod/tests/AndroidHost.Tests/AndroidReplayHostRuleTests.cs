using System.Diagnostics;
using BazaarPlusPlus.Android;
using BazaarPlusPlus.Android.CombatReplay;
using BazaarPlusPlus.Android.CombatReplay.Bootstrap;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Storage.Paths;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The managed rules the Android replay and History stand on: the capture never blocks the main
/// thread on the payload gate a replay holds, the record chain's intake keeps replay messages
/// out of the battle capture, Ghost sync's template lookup never builds its index, and the
/// replay waits for the game's presentation tasks as the desktop waits for its own.
/// </summary>
public sealed class AndroidReplayHostRuleTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "bpp-android-replay-" + Guid.NewGuid().ToString("N")
    );

    public AndroidReplayHostRuleTests() => Directory.CreateDirectory(_dataRoot);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dataRoot, recursive: true);
    }

    [Fact]
    public void A_capture_under_a_held_playback_lease_returns_and_saves_the_battle_without_its_payload()
    {
        var (store, files) = Stores();
        var gate = new ReplayPayloadOperationGate();
        using var playback = gate.TryAcquirePlayback();
        Assert.NotNull(playback);

        var clock = Stopwatch.StartNew();
        var outcome = AndroidPvpBattleCapture.Persist(
            gate,
            store,
            files,
            Battle("during-replay"),
            ReplayMessages.Payload("during-replay"),
            TimeSpan.FromMilliseconds(50)
        );

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"blocked {clock.Elapsed}");
        Assert.False(outcome.LeaseHeld);
        Assert.False(outcome.ReplaySaved);
        Assert.Empty(files.ListBattleIds());
        Assert.Equal(
            ReplayPayloadState.Missing,
            store
                .ListReplayMaintenanceInventory()
                .Single(record => record.BattleId == "during-replay")
                .PayloadState
        );
    }

    [Fact]
    public void A_capture_with_the_gate_free_saves_the_payload_before_its_battle()
    {
        var (store, files) = Stores();
        var gate = new ReplayPayloadOperationGate();

        var outcome = AndroidPvpBattleCapture.Persist(
            gate,
            store,
            files,
            Battle("live"),
            ReplayMessages.Payload("live"),
            AndroidPvpBattleCapture.LeaseWait
        );

        Assert.True(outcome.LeaseHeld);
        Assert.True(outcome.ReplaySaved);
        Assert.Equal(new[] { "live" }, files.ListBattleIds());
        Assert.NotNull(gate.TryAcquirePlayback());
    }

    [Fact]
    public void During_a_replay_the_intake_keeps_messages_out_of_the_battle_capture_only()
    {
        var live = AndroidReplayGuard.Decide(replayActive: false);
        var replay = AndroidReplayGuard.Decide(replayActive: true);

        Assert.True(live.CaptureBattle);
        Assert.False(replay.CaptureBattle);
        // Run lifecycle is never dropped: the run context must not stick in a run.
        Assert.True(live.PublishRunInitialized);
        Assert.True(replay.PublishRunInitialized);
    }

    [Fact]
    public void The_guard_reads_no_replay_without_a_runtime_and_a_failing_source_as_none()
    {
        var previous = AndroidReplayGuard.IsActiveSource;
        try
        {
            AndroidReplayGuard.IsActiveSource = null;
            Assert.False(AndroidReplayGuard.IsActive);
            AndroidReplayGuard.IsActiveSource = static () => throw new InvalidOperationException();
            Assert.False(AndroidReplayGuard.IsActive);
            AndroidReplayGuard.IsActiveSource = static () => true;
            Assert.True(AndroidReplayGuard.IsActive);
        }
        finally
        {
            AndroidReplayGuard.IsActiveSource = previous;
        }
    }

    [Fact]
    public void The_template_index_reads_the_ids_of_the_game_data_cards_table()
    {
        Directory.CreateDirectory(_dataRoot);
        var path = Path.Combine(_dataRoot, "GameData.db");
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE cards (Id TEXT NOT NULL PRIMARY KEY, Data BLOB NOT NULL) STRICT, WITHOUT ROWID;"
                + $"INSERT INTO cards VALUES ('{first}', x'7b7d'), ('{second}', x'7b7d'), ('not-a-guid', x'7b7d');";
            command.ExecuteNonQuery();
        }

        var ids = AndroidCardTemplateIndex.ReadTemplateIds(path);

        Assert.Equal(new[] { first, second }.Order(), ids.Order());
    }

    [Fact]
    public async Task The_template_index_counts_every_template_until_frozen_then_only_known_ones()
    {
        var index = new AndroidCardTemplateIndex();
        var known = Guid.NewGuid();
        var unknown = Guid.NewGuid();

        Assert.False(index.IsBuilt);
        Assert.True(index.Contains(unknown));

        index.Freeze([known], IntPtr.Zero);

        Assert.True(index.IsBuilt);
        Assert.Equal(1, index.Count);
        Assert.True(index.Contains(known));
        Assert.False(index.Contains(unknown));
        Assert.True(await Task.Run(() => index.Contains(known)));
    }

    [Fact]
    public void The_presentation_tracker_records_only_while_a_replay_injects()
    {
        var tracker = Tracker();
        tracker.Track(new FakeTask());
        using (tracker.BeginTracking())
        {
            Assert.Equal(ReplayPresentationState.Settled, tracker.Observe());
            var item = new FakeTask();
            tracker.Track(item);
            Assert.Equal(ReplayPresentationState.Pending, tracker.Observe());
            item.Completed = true;
            Assert.Equal(ReplayPresentationState.Settled, tracker.Observe());
            tracker.Track(new FakeTask { Completed = true, Faulted = true });
            Assert.Equal(ReplayPresentationState.Faulted, tracker.Observe());
        }
        Assert.False(tracker.IsTracking);
        Assert.Throws<InvalidOperationException>(() => tracker.Observe());
    }

    [Fact]
    public async Task The_presentation_wait_ends_after_the_tasks_settle_and_a_render_boundary()
    {
        var pump = new AndroidMainThread();
        pump.Capture();
        var tracker = Tracker();
        using var scope = tracker.BeginTracking();
        var item = new FakeTask();
        tracker.Track(item);

        Task wait;
        using (pump.Enter())
            wait = tracker.WaitAsync(pump, TimeSpan.FromSeconds(30));
        for (var frame = 0; frame < 3; frame++)
            pump.Tick();
        Assert.False(wait.IsCompleted);

        item.Completed = true;
        for (var frame = 0; frame < 8 && !wait.IsCompleted; frame++)
            pump.Tick();

        await wait.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_faulted_presentation_task_fails_the_wait()
    {
        var pump = new AndroidMainThread();
        pump.Capture();
        var tracker = Tracker();
        using var scope = tracker.BeginTracking();
        tracker.Track(new FakeTask { Completed = true, Faulted = true });

        Task wait;
        using (pump.Enter())
            wait = tracker.WaitAsync(pump, TimeSpan.FromSeconds(30));
        for (var frame = 0; frame < 4 && !wait.IsCompleted; frame++)
            pump.Tick();

        await Assert.ThrowsAsync<InvalidOperationException>(() => wait);
    }

    private static AndroidReplayPresentationTasks<FakeTask> Tracker() =>
        new(task => task.Completed, task => task.Faulted);

    private (PvpBattleSqliteStore Store, CombatReplayPayloadStore Files) Stores() =>
        (
            new PvpBattleSqliteStore(
                PathConstants.RunLogDatabase(_dataRoot),
                AndroidComposition.DatabaseOptions
            ),
            new CombatReplayPayloadStore(PathConstants.CombatReplays(_dataRoot))
        );

    private static PvpBattleRecord Battle(string battleId) =>
        new()
        {
            BattleId = battleId,
            RecordedAtUtc = Now,
            CombatKind = "PVPCombat",
            Participants = new PvpBattleParticipants(),
            Outcome = new PvpBattleOutcome(),
            Snapshots = new PvpBattleSnapshotDocuments(),
        };

    private sealed class FakeTask
    {
        public bool Completed { get; set; }

        public bool Faulted { get; set; }
    }
}
