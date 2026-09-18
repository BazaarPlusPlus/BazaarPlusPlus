#nullable enable
using BazaarPlusPlus.Game.PvpBattles.Persistence;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class ReplayPayloadMaintenanceService
{
    private readonly IReplayPayloadMaintenanceCatalog _catalog;
    private readonly IReplayPayloadFiles _files;
    private readonly ReplayPayloadOperationGate _operationGate;
    private readonly Func<IReadOnlyCollection<string>> _protectedBattleIds;

    internal ReplayPayloadMaintenanceService(
        IReplayPayloadMaintenanceCatalog catalog,
        IReplayPayloadFiles files,
        ReplayPayloadOperationGate operationGate,
        Func<IReadOnlyCollection<string>> protectedBattleIds
    )
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _operationGate = operationGate ?? throw new ArgumentNullException(nameof(operationGate));
        _protectedBattleIds =
            protectedBattleIds ?? throw new ArgumentNullException(nameof(protectedBattleIds));
    }

    internal ReplayPayloadMaintenanceResult Run(
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        using var maintenanceLease = _operationGate.AcquireMaintenance(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var inventory = _catalog.ListReplayMaintenanceInventory();
        var storedBattleIds = _files.ListBattleIds();
        var workUnits = inventory.Count + storedBattleIds.Count;
        var databaseBattleIds = new HashSet<string>(
            inventory.Select(record => record.BattleId),
            StringComparer.Ordinal
        );
        var storedBattleIdSet = new HashSet<string>(storedBattleIds, StringComparer.Ordinal);
        var orphanDeleteCount = 0;
        var failedDeleteCount = 0;

        foreach (var battleId in storedBattleIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (databaseBattleIds.Contains(battleId))
                continue;
            workUnits++;
            if (TryDelete(battleId))
                orphanDeleteCount++;
            else
                failedDeleteCount++;
        }

        var deletedPayloadCount = 0;
        var pending = inventory
            .Where(record => record.PayloadState == ReplayPayloadState.DeletePending)
            .Select(record => record.BattleId)
            .ToList();
        var completedPending = DeleteFiles(
            pending,
            cancellationToken,
            ref workUnits,
            ref failedDeleteCount
        );
        if (completedPending.Count > 0)
        {
            _catalog.CompleteReplayPayloadDeletion(completedPending, now);
            deletedPayloadCount += completedPending.Count;
        }

        var missing = inventory
            .Where(record =>
                record.PayloadState == ReplayPayloadState.Ready
                && !storedBattleIdSet.Contains(record.BattleId)
            )
            .Select(record => record.BattleId)
            .ToList();
        if (missing.Count > 0)
            _catalog.MarkReplayPayloadMissing(missing, now);

        var protectedIds = new HashSet<string>(
            _protectedBattleIds() ?? Array.Empty<string>(),
            StringComparer.Ordinal
        );
        var retention = ReplayPayloadRetentionPolicy.SelectEvictionCandidates(
            inventory
                .Where(record =>
                    record.PayloadState != ReplayPayloadState.Ready
                    || storedBattleIdSet.Contains(record.BattleId)
                )
                .ToList(),
            protectedIds,
            now
        );
        var proposed = retention.BattleIds.Where(storedBattleIdSet.Contains).ToList();
        var scheduled = _catalog.ScheduleReplayPayloadDeletion(proposed, now);
        var completedScheduled = DeleteFiles(
            scheduled,
            cancellationToken,
            ref workUnits,
            ref failedDeleteCount
        );
        if (completedScheduled.Count > 0)
        {
            _catalog.CompleteReplayPayloadDeletion(completedScheduled, now);
            deletedPayloadCount += completedScheduled.Count;
        }

        return new ReplayPayloadMaintenanceResult(
            retention.EvaluatedCount,
            scheduled.Count,
            deletedPayloadCount,
            missing.Count,
            orphanDeleteCount,
            failedDeleteCount,
            workUnits
        );
    }

    private List<string> DeleteFiles(
        IReadOnlyCollection<string> battleIds,
        CancellationToken cancellationToken,
        ref int workUnits,
        ref int failedDeleteCount
    )
    {
        var deleted = new List<string>();
        foreach (var battleId in battleIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            workUnits++;
            if (TryDelete(battleId))
                deleted.Add(battleId);
            else
                failedDeleteCount++;
        }
        return deleted;
    }

    private bool TryDelete(string battleId)
    {
        try
        {
            _files.Delete(battleId);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
