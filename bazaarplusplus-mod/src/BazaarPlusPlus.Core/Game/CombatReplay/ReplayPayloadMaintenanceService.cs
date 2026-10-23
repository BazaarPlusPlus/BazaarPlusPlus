#nullable enable
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CombatReplay;

/// <summary>
/// One sweep of a host's Replay Payload files against the history database: deletes files no battle
/// owns, finishes pending deletions, marks ready battles whose file is gone as missing, and evicts
/// what <see cref="ReplayPayloadRetentionPolicy"/> does not keep. It holds the maintenance lease of
/// <see cref="ReplayPayloadOperationGate"/> throughout, so a battle being saved is never swept.
/// </summary>
public sealed class ReplayPayloadMaintenanceService
{
    private readonly IReplayPayloadMaintenanceCatalog _catalog;
    private readonly CombatReplayPayloadStore _files;
    private readonly ReplayPayloadOperationGate _operationGate;
    private readonly Func<IReadOnlyCollection<string>> _protectedBattleIds;

    public ReplayPayloadMaintenanceService(
        IReplayPayloadMaintenanceCatalog catalog,
        CombatReplayPayloadStore files,
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

    /// <summary>
    /// Runs one sweep and logs its terminal: <c>combat_replay.maintenance.completed</c>, or
    /// <c>combat_replay.maintenance.degraded</c> when a delete or the scan failed. Never throws;
    /// a cancellation requested through <paramref name="cancellationToken"/> ends it silently.
    /// </summary>
    public void RunAndReport(DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var result = Run(now, cancellationToken);
            if (result.FailedDeleteCount == 0)
            {
                BppLog.InfoEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.CombatReplay,
                        "combat_replay.maintenance.completed"
                    ),
                    MaintenanceFields(ReplayMaintenanceReasonCode.Completed, result)
                );
                return;
            }

            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CombatReplay,
                    "combat_replay.maintenance.degraded",
                    storm: ["reason_code"]
                ),
                MaintenanceFields(ReplayMaintenanceReasonCode.DeleteFailed, result)
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CombatReplay,
                    "combat_replay.maintenance.degraded",
                    storm: ["reason_code"]
                ),
                ex,
                MaintenanceFields(ReplayMaintenanceReasonCode.ScanFailed, default)
            );
        }
    }

    public ReplayPayloadMaintenanceResult Run(
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        using var maintenanceLease = _operationGate.AcquireMaintenance(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var inventory = _catalog.ListReplayMaintenanceInventory();
        var storedBattleIds = _files.ListBattleIds().ToList();
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

    private static BppLogField[] MaintenanceFields(
        ReplayMaintenanceReasonCode reasonCode,
        ReplayPayloadMaintenanceResult result
    ) =>
        [
            ("reason_code", reasonCode),
            ("evaluated_count", result.EvaluatedPayloadCount),
            ("scheduled_count", result.ScheduledDeleteCount),
            ("deleted_count", result.DeletedPayloadCount),
            ("missing_count", result.MissingPayloadCount),
            ("orphan_count", result.OrphanDeleteCount),
            ("failed_count", result.FailedDeleteCount),
        ];

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

public readonly record struct ReplayPayloadMaintenanceResult(
    int EvaluatedPayloadCount,
    int ScheduledDeleteCount,
    int DeletedPayloadCount,
    int MissingPayloadCount,
    int OrphanDeleteCount,
    int FailedDeleteCount,
    int WorkUnits
);

public enum ReplayMaintenanceReasonCode
{
    Completed,
    DeleteFailed,
    ScanFailed,
}
