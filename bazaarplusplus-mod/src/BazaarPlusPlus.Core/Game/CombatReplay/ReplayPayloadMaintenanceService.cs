#nullable enable
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CombatReplay;

/// <summary>The maintenance lease keeps the sweep from deleting a Replay Payload whose battle row is still being saved.</summary>
public sealed class ReplayPayloadMaintenanceService
{
    private const int MaximumConcurrentFileDeletes = 4;
    private readonly Action<string> _deleteFile;
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
        : this(catalog, files, operationGate, protectedBattleIds, deleteFile: null) { }

    internal ReplayPayloadMaintenanceService(
        IReplayPayloadMaintenanceCatalog catalog,
        CombatReplayPayloadStore files,
        ReplayPayloadOperationGate operationGate,
        Func<IReadOnlyCollection<string>> protectedBattleIds,
        Action<string>? deleteFile
    )
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _deleteFile = deleteFile ?? _files.Delete;
        _operationGate = operationGate ?? throw new ArgumentNullException(nameof(operationGate));
        _protectedBattleIds =
            protectedBattleIds ?? throw new ArgumentNullException(nameof(protectedBattleIds));
    }

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
        var databaseBattleIds = new HashSet<string>(
            inventory.Select(record => record.BattleId),
            StringComparer.Ordinal
        );
        var storedBattleIdSet = new HashSet<string>(storedBattleIds, StringComparer.Ordinal);
        var failedDeleteCount = 0;
        var orphanDeleteCount = DeleteFiles(
            storedBattleIds.Where(battleId => !databaseBattleIds.Contains(battleId)).ToArray(),
            cancellationToken,
            ref failedDeleteCount
        ).Count;

        var deletedPayloadCount = 0;
        var pending = inventory
            .Where(record => record.PayloadState == ReplayPayloadState.DeletePending)
            .Select(record => record.BattleId)
            .ToList();
        var completedPending = DeleteFiles(pending, cancellationToken, ref failedDeleteCount);
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
        var completedScheduled = DeleteFiles(scheduled, cancellationToken, ref failedDeleteCount);
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
            failedDeleteCount
        );
    }

    private List<string> DeleteFiles(
        IReadOnlyCollection<string> battleIds,
        CancellationToken cancellationToken,
        ref int failedDeleteCount
    )
    {
        var ids = battleIds.ToArray();
        var succeeded = new bool[ids.Length];
        // Parallel.For joins every started deletion even on cancellation, so the maintenance lease covers them all.
        // Catalog writes stay on the caller.
        Parallel.For(
            0,
            ids.Length,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaximumConcurrentFileDeletes,
                CancellationToken = cancellationToken,
            },
            index =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                succeeded[index] = TryDelete(ids[index]);
            }
        );

        var deleted = new List<string>(ids.Length);
        for (var index = 0; index < ids.Length; index++)
        {
            if (succeeded[index])
                deleted.Add(ids[index]);
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
            _deleteFile(battleId);
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
    int FailedDeleteCount
);

public enum ReplayMaintenanceReasonCode
{
    Completed,
    DeleteFailed,
    ScanFailed,
}
