#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CombatReplay;

internal readonly record struct ReplayPayloadMaintenanceResult(
    int EvaluatedPayloadCount,
    int ScheduledDeleteCount,
    int DeletedPayloadCount,
    int MissingPayloadCount,
    int OrphanDeleteCount,
    int FailedDeleteCount,
    int WorkUnits
);

internal sealed class ReplayPersistenceCompletionGate
{
    private int _completed;

    internal bool TryComplete() => Interlocked.Exchange(ref _completed, 1) == 0;
}

internal static class ReplayPersistenceLogWriter
{
    internal static void EmitMaintenanceTerminal(ReplayPayloadMaintenanceResult result)
    {
        var fields = BuildMaintenanceFields(
            result.FailedDeleteCount == 0
                ? ReplayMaintenanceReasonCode.Completed
                : ReplayMaintenanceReasonCode.DeleteFailed,
            result
        );
        if (result.FailedDeleteCount == 0)
        {
            BppLog.DebugEvent(CombatReplayLogEvents.MaintenanceCompleted, () => fields);
            return;
        }

        BppLog.WarnEvent(CombatReplayLogEvents.MaintenanceDegraded, fields);
    }

    internal static void EmitMaintenanceFailed(Exception exception)
    {
        var fields = BuildMaintenanceFields(ReplayMaintenanceReasonCode.ScanFailed, default);
        BppLog.WarnEvent(CombatReplayLogEvents.MaintenanceDegraded, exception, fields);
    }

    private static BppLogFieldValue[] BuildMaintenanceFields(
        ReplayMaintenanceReasonCode reasonCode,
        ReplayPayloadMaintenanceResult result
    ) =>
        [
            CombatReplayLogEvents.MaintenanceReasonCode.Bind(reasonCode),
            CombatReplayLogEvents.MaintenanceEvaluatedCount.Bind(result.EvaluatedPayloadCount),
            CombatReplayLogEvents.MaintenanceScheduledCount.Bind(result.ScheduledDeleteCount),
            CombatReplayLogEvents.MaintenanceDeletedCount.Bind(result.DeletedPayloadCount),
            CombatReplayLogEvents.MaintenanceMissingCount.Bind(result.MissingPayloadCount),
            CombatReplayLogEvents.MaintenanceOrphanCount.Bind(result.OrphanDeleteCount),
            CombatReplayLogEvents.MaintenanceFailedCount.Bind(result.FailedDeleteCount),
        ];
}
