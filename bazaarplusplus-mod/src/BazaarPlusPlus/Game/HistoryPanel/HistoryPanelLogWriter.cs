#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelLogWriter
{
    internal static void EmitReplayPreflight(HistoryPanelReplayPreflightResult result) =>
        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.HistoryPanel,
                "history_panel.replay.preflight_completed"
            ),
            () =>
                [
                    ("request_id", result.RequestId, BppLogCorrelationPolicy.Short),
                    ("battle_id", result.BattleId, BppLogCorrelationPolicy.Short),
                    ("record_video", result.RecordVideo),
                    ("can_record", result.CanRecord),
                    ("reason_code", result.ReasonCode),
                ]
        );

    internal static void EmitReplayAccepted(HistoryPanelReplayAcceptedResult result) =>
        BppLog.DebugEvent(
            new BppLogEvent(BppLogFeatureScope.HistoryPanel, "history_panel.replay.accepted"),
            () =>
                [
                    ("request_id", result.RequestId, BppLogCorrelationPolicy.Short),
                    ("battle_id", result.BattleId, BppLogCorrelationPolicy.Short),
                    ("record_video", result.RecordVideo),
                ]
        );

    internal static void EmitReplayFailed(HistoryPanelReplayFailedResult result)
    {
        var fields = new BppLogField[]
        {
            ("request_id", result.RequestId, BppLogCorrelationPolicy.Short),
            ("battle_id", result.BattleId, BppLogCorrelationPolicy.Short),
            ("record_video", result.RecordVideo),
            ("reason_code", result.ReasonCode),
        };
        if (result.Exception == null)
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.HistoryPanel, "history_panel.replay.failed"),
                fields
            );
        else
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.HistoryPanel, "history_panel.replay.failed"),
                result.Exception,
                fields
            );
    }

    internal static void EmitRunDeleteTerminal(HistoryPanelRunDeleteTerminalResult result)
    {
        var fields = new BppLogField[]
        {
            ("request_id", result.RequestId, BppLogCorrelationPolicy.Short),
            ("run_id", result.RunId, BppLogCorrelationPolicy.Short),
            ("battle_count", result.BattleCount),
            ("cleanup_failed_count", result.CleanupFailedCount),
            ("reason_code", result.ReasonCode),
        };
        switch (result.Status)
        {
            case HistoryPanelRunDeleteTerminalStatus.Failed:
                if (result.Exception == null)
                    BppLog.ErrorEvent(
                        new BppLogEvent(
                            BppLogFeatureScope.HistoryPanel,
                            "history_panel.run_delete.failed"
                        ),
                        fields
                    );
                else
                    BppLog.ErrorEvent(
                        new BppLogEvent(
                            BppLogFeatureScope.HistoryPanel,
                            "history_panel.run_delete.failed"
                        ),
                        result.Exception,
                        fields
                    );
                return;
            case HistoryPanelRunDeleteTerminalStatus.Degraded:
                if (result.Exception == null)
                    BppLog.WarnEvent(
                        new BppLogEvent(
                            BppLogFeatureScope.HistoryPanel,
                            "history_panel.run_delete.degraded"
                        ),
                        fields
                    );
                else
                    BppLog.WarnEvent(
                        new BppLogEvent(
                            BppLogFeatureScope.HistoryPanel,
                            "history_panel.run_delete.degraded"
                        ),
                        result.Exception,
                        fields
                    );
                return;
            case HistoryPanelRunDeleteTerminalStatus.Succeeded:
                BppLog.InfoEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.HistoryPanel,
                        "history_panel.run_delete.succeeded"
                    ),
                    fields
                );
                return;
        }
    }

    internal static void EmitServerHealthTerminal(HistoryPanelServerHealthTerminalResult result)
    {
        var fields = new BppLogField[]
        {
            ("request_id", result.RequestId, BppLogCorrelationPolicy.Short),
            ("duration_ms", result.DurationMilliseconds),
            ("reason_code", result.ReasonCode),
        };
        if (result.Status == HistoryPanelServerHealthTerminalStatus.Succeeded)
        {
            BppLog.InfoEvent(
                new BppLogEvent(
                    BppLogFeatureScope.HistoryPanel,
                    "history_panel.server_health.succeeded"
                ),
                fields
            );
            return;
        }

        if (result.Exception == null)
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.HistoryPanel,
                    "history_panel.server_health.failed"
                ),
                fields
            );
        else
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.HistoryPanel,
                    "history_panel.server_health.failed"
                ),
                result.Exception,
                fields
            );
    }

    internal static void EmitGhostSyncTerminal(HistoryPanelGhostSyncTerminalResult result)
    {
        var fields = new BppLogField[]
        {
            ("request_id", result.RequestId, BppLogCorrelationPolicy.Short),
            ("imported_count", result.ImportedCount),
            ("reason_code", result.ReasonCode),
        };
        if (result.Status == HistoryPanelGhostSyncTerminalStatus.Succeeded)
        {
            BppLog.InfoEvent(
                new BppLogEvent(
                    BppLogFeatureScope.HistoryPanel,
                    "history_panel.ghost_sync.succeeded"
                ),
                fields
            );
            return;
        }

        if (result.Exception == null)
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.HistoryPanel, "history_panel.ghost_sync.failed"),
                fields
            );
        else
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.HistoryPanel, "history_panel.ghost_sync.failed"),
                result.Exception,
                fields
            );
    }
}
