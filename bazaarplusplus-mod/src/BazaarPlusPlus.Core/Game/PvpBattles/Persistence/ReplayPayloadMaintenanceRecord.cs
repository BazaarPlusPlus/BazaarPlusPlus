#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles.Persistence;

public enum ReplayPayloadState
{
    Ready,
    DeletePending,
    Evicted,
    Missing,
}

public readonly record struct ReplayPayloadMaintenanceRecord(
    string BattleId,
    DateTimeOffset RecordedAtUtc,
    ReplayPayloadState PayloadState
);

public enum ReplayPayloadMaintenanceStorageEvent
{
    ConnectionOpened,
    CommandExecuted,
}

public interface IReplayPayloadMaintenanceCatalog
{
    IReadOnlyList<ReplayPayloadMaintenanceRecord> ListReplayMaintenanceInventory();

    IReadOnlyList<string> ScheduleReplayPayloadDeletion(
        IReadOnlyCollection<string> battleIds,
        DateTimeOffset now
    );

    void CompleteReplayPayloadDeletion(IReadOnlyCollection<string> battleIds, DateTimeOffset now);

    void MarkReplayPayloadMissing(IReadOnlyCollection<string> battleIds, DateTimeOffset now);
}
