#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles.Persistence;

internal enum ReplayPayloadState
{
    Ready,
    DeletePending,
    Evicted,
    Missing,
}

internal readonly record struct ReplayPayloadMaintenanceRecord(
    string BattleId,
    DateTimeOffset RecordedAtUtc,
    bool HasLocalPayload,
    ReplayPayloadState PayloadState
);

internal enum ReplayPayloadMaintenanceStorageEvent
{
    ConnectionOpened,
    CommandExecuted,
}

internal interface IReplayPayloadMaintenanceCatalog
{
    IReadOnlyList<ReplayPayloadMaintenanceRecord> ListReplayMaintenanceInventory();

    IReadOnlyList<string> ScheduleReplayPayloadDeletion(
        IReadOnlyCollection<string> battleIds,
        DateTimeOffset now
    );

    void CompleteReplayPayloadDeletion(IReadOnlyCollection<string> battleIds, DateTimeOffset now);

    void MarkReplayPayloadMissing(IReadOnlyCollection<string> battleIds, DateTimeOffset now);
}
