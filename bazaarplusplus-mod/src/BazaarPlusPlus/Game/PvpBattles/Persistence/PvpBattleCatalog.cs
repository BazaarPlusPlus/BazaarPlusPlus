#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles.Persistence;

internal sealed class PvpBattleCatalog : IPvpBattleCatalog
{
    private readonly PvpBattleSqliteStore _store;

    public PvpBattleCatalog(
        string databasePath,
        Action<ReplayPayloadMaintenanceStorageEvent>? maintenanceDiagnostics = null
    )
    {
        _store = new PvpBattleSqliteStore(databasePath, maintenanceDiagnostics);
    }

    public void Save(PvpBattleManifest manifest)
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));

        _store.Save(manifest);
    }

    public void AttachToRun(string battleId, string runId)
    {
        _store.AttachToRun(battleId, runId);
    }

    public PvpBattleManifest? TryLoad(string battleId)
    {
        return _store.TryLoad(battleId);
    }

    public IReadOnlyList<ReplayPayloadMaintenanceRecord> ListReplayMaintenanceInventory()
    {
        return _store.ListReplayMaintenanceInventory();
    }

    public IReadOnlyList<string> ScheduleReplayPayloadDeletion(
        IReadOnlyCollection<string> battleIds,
        DateTimeOffset now
    )
    {
        return _store.ScheduleReplayPayloadDeletion(battleIds, now);
    }

    public void CompleteReplayPayloadDeletion(
        IReadOnlyCollection<string> battleIds,
        DateTimeOffset now
    )
    {
        _store.CompleteReplayPayloadDeletion(battleIds, now);
    }

    public void MarkReplayPayloadMissing(IReadOnlyCollection<string> battleIds, DateTimeOffset now)
    {
        _store.MarkReplayPayloadMissing(battleIds, now);
    }

    public IReadOnlyList<PvpBattleManifest> ListRecentBattles(int limit)
    {
        return _store.ListRecentBattles(limit);
    }

    public IReadOnlyList<PvpBattleManifest> ListByRunId(string runId)
    {
        return _store.ListByRunId(runId);
    }
}
