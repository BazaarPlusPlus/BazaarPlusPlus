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
        var failedDeleteCount = 0;
        var orphanDeleteCount = DeleteFiles(
            storedBattleIds.Where(battleId => !databaseBattleIds.Contains(battleId)).ToArray(),
            cancellationToken,
            ref workUnits,
            ref failedDeleteCount
        ).Count;

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
        var ids = battleIds.ToArray();
        var succeeded = new bool[ids.Length];
        // Parallel.For joins every started deletion before returning or throwing cancellation.
        // Run therefore retains its maintenance lease until all file operations have stopped.
        // Workers touch files and their own result slot only; catalog updates stay on the caller.
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
            workUnits++;
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
    int FailedDeleteCount,
    int WorkUnits
);

public enum ReplayMaintenanceReasonCode
{
    Completed,
    DeleteFailed,
    ScanFailed,
}
