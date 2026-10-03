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
            BppLog.DebugEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CombatReplay,
                    "combat_replay.maintenance.completed"
                ),
                () => fields
            );
            return;
        }

        BppLog.WarnEvent(
            new BppLogEvent(
                BppLogFeatureScope.CombatReplay,
                "combat_replay.maintenance.degraded",
                storm: ["reason_code"]
            ),
            fields
        );
    }

    internal static void EmitMaintenanceFailed(Exception exception)
    {
        var fields = BuildMaintenanceFields(ReplayMaintenanceReasonCode.ScanFailed, default);
        BppLog.WarnEvent(
            new BppLogEvent(
                BppLogFeatureScope.CombatReplay,
                "combat_replay.maintenance.degraded",
                storm: ["reason_code"]
            ),
            exception,
            fields
        );
    }

    private static BppLogField[] BuildMaintenanceFields(
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
}
