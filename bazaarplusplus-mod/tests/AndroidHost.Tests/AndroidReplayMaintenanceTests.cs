using BazaarPlusPlus.Android;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Storage.Paths;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// Core's Replay Payload sweep as <see cref="AndroidComposition"/> runs it: over the battle store
/// opened with the host's DELETE journal mode and the host's combat replay directory.
/// </summary>
public sealed class AndroidReplayMaintenanceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "bpp-android-maintenance-" + Guid.NewGuid().ToString("N")
    );

    public AndroidReplayMaintenanceTests() => Directory.CreateDirectory(_dataRoot);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dataRoot, recursive: true);
    }

    [Fact]
    public void The_sweep_deletes_orphans_marks_lost_files_missing_and_keeps_saved_replays()
    {
        var database = PathConstants.RunLogDatabase(_dataRoot);
        var store = new PvpBattleSqliteStore(database, AndroidComposition.DatabaseOptions);
        var files = new CombatReplayPayloadStore(PathConstants.CombatReplays(_dataRoot));
        store.Save(Battle("kept"));
        files.Save(ReplayMessages.Payload("kept"));
        store.Save(Battle("lost"));
        files.Save(ReplayMessages.Payload("orphan"));
        var gate = new ReplayPayloadOperationGate();

        new ReplayPayloadMaintenanceService(store, files, gate, static () => []).RunAndReport(
            Now,
            CancellationToken.None
        );

        Assert.Equal(new[] { "kept" }, files.ListBattleIds());
        var states = store
            .ListReplayMaintenanceInventory()
            .ToDictionary(record => record.BattleId, record => record.PayloadState);
        Assert.Equal(ReplayPayloadState.Ready, states["kept"]);
        Assert.Equal(ReplayPayloadState.Missing, states["lost"]);
    }

    [Fact]
    public async Task The_sweep_waits_for_a_battle_being_saved_under_the_persistence_lease()
    {
        var database = PathConstants.RunLogDatabase(_dataRoot);
        var store = new PvpBattleSqliteStore(database, AndroidComposition.DatabaseOptions);
        var files = new CombatReplayPayloadStore(PathConstants.CombatReplays(_dataRoot));
        var gate = new ReplayPayloadOperationGate();
        var maintenance = new ReplayPayloadMaintenanceService(store, files, gate, static () => []);
        Task sweep;
        using (gate.AcquirePersistence(CancellationToken.None))
        {
            // The capture's order: the file first, then its row.
            files.Save(ReplayMessages.Payload("saving"));
            sweep = Task.Run(() => maintenance.RunAndReport(Now, CancellationToken.None));
            Assert.NotSame(sweep, await Task.WhenAny(sweep, Task.Delay(200)));
            store.Save(Battle("saving"));
        }
        await sweep.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { "saving" }, files.ListBattleIds());
    }

    private static PvpBattleRecord Battle(string battleId) =>
        new()
        {
            BattleId = battleId,
            RecordedAtUtc = Now.AddDays(-1),
            CombatKind = "PVPCombat",
            Participants = new PvpBattleParticipants(),
            Outcome = new PvpBattleOutcome(),
            Snapshots = new PvpBattleSnapshotDocuments(),
        };
}
