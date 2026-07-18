#nullable enable
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelLogWriter
{
    internal static void EmitReplayPreflight(HistoryPanelReplayPreflightResult result) =>
        BppLog.DebugEvent(
            HistoryPanelLogEvents.ReplayPreflightCompleted,
            () =>
                [
                    HistoryPanelLogEvents.ReplayRequestId.Bind(result.RequestId),
                    HistoryPanelLogEvents.ReplayBattleId.Bind(result.BattleId),
                    HistoryPanelLogEvents.ReplayRecordVideo.Bind(result.RecordVideo),
                    HistoryPanelLogEvents.ReplayCanRecord.Bind(result.CanRecord),
                    HistoryPanelLogEvents.ReplayPreflightReasonCode.Bind(result.ReasonCode),
                ]
        );

    internal static void EmitReplayAccepted(HistoryPanelReplayAcceptedResult result) =>
        BppLog.DebugEvent(
            HistoryPanelLogEvents.ReplayAccepted,
            () =>
                [
                    HistoryPanelLogEvents.ReplayRequestId.Bind(result.RequestId),
                    HistoryPanelLogEvents.ReplayBattleId.Bind(result.BattleId),
                    HistoryPanelLogEvents.ReplayRecordVideo.Bind(result.RecordVideo),
                ]
        );

    internal static void EmitReplayFailed(HistoryPanelReplayFailedResult result)
    {
        var fields = new[]
        {
            HistoryPanelLogEvents.ReplayRequestId.Bind(result.RequestId),
            HistoryPanelLogEvents.ReplayBattleId.Bind(result.BattleId),
            HistoryPanelLogEvents.ReplayRecordVideo.Bind(result.RecordVideo),
            HistoryPanelLogEvents.ReplayFailureReasonCode.Bind(result.ReasonCode),
        };
        if (result.Exception == null)
            BppLog.ErrorEvent(HistoryPanelLogEvents.ReplayFailed, fields);
        else
            BppLog.ErrorEvent(HistoryPanelLogEvents.ReplayFailed, result.Exception, fields);
    }

    internal static void EmitRunDeleteTerminal(HistoryPanelRunDeleteTerminalResult result)
    {
        var fields = new[]
        {
            HistoryPanelLogEvents.DeleteRequestId.Bind(result.RequestId),
            HistoryPanelLogEvents.DeleteRunId.Bind(result.RunId),
            HistoryPanelLogEvents.DeleteBattleCount.Bind(result.BattleCount),
            HistoryPanelLogEvents.DeleteCleanupFailedCount.Bind(result.CleanupFailedCount),
            HistoryPanelLogEvents.DeleteReasonCode.Bind(result.ReasonCode),
        };
        switch (result.Status)
        {
            case HistoryPanelRunDeleteTerminalStatus.Failed:
                if (result.Exception == null)
                    BppLog.ErrorEvent(HistoryPanelLogEvents.RunDeleteFailed, fields);
                else
                    BppLog.ErrorEvent(
                        HistoryPanelLogEvents.RunDeleteFailed,
                        result.Exception,
                        fields
                    );
                return;
            case HistoryPanelRunDeleteTerminalStatus.Degraded:
                if (result.Exception == null)
                    BppLog.WarnEvent(HistoryPanelLogEvents.RunDeleteDegraded, fields);
                else
                    BppLog.WarnEvent(
                        HistoryPanelLogEvents.RunDeleteDegraded,
                        result.Exception,
                        fields
                    );
                return;
            case HistoryPanelRunDeleteTerminalStatus.Succeeded:
                BppLog.InfoEvent(HistoryPanelLogEvents.RunDeleteSucceeded, fields);
                return;
        }
    }

    internal static void EmitServerHealthTerminal(HistoryPanelServerHealthTerminalResult result)
    {
        var fields = new[]
        {
            HistoryPanelLogEvents.HealthRequestId.Bind(result.RequestId),
            HistoryPanelLogEvents.HealthDurationMs.Bind(result.DurationMilliseconds),
            HistoryPanelLogEvents.HealthReasonCode.Bind(result.ReasonCode),
        };
        if (result.Status == HistoryPanelServerHealthTerminalStatus.Succeeded)
        {
            BppLog.InfoEvent(HistoryPanelLogEvents.ServerHealthSucceeded, fields);
            return;
        }

        if (result.Exception == null)
            BppLog.ErrorEvent(HistoryPanelLogEvents.ServerHealthFailed, fields);
        else
            BppLog.ErrorEvent(HistoryPanelLogEvents.ServerHealthFailed, result.Exception, fields);
    }

    internal static void EmitGhostSyncTerminal(HistoryPanelGhostSyncTerminalResult result)
    {
        var fields = new[]
        {
            HistoryPanelLogEvents.SyncRequestId.Bind(result.RequestId),
            HistoryPanelLogEvents.SyncImportedCount.Bind(result.ImportedCount),
            HistoryPanelLogEvents.SyncReasonCode.Bind(result.ReasonCode),
        };
        if (result.Status == HistoryPanelGhostSyncTerminalStatus.Succeeded)
        {
            BppLog.InfoEvent(HistoryPanelLogEvents.GhostSyncSucceeded, fields);
            return;
        }

        if (result.Exception == null)
            BppLog.ErrorEvent(HistoryPanelLogEvents.GhostSyncFailed, fields);
        else
            BppLog.ErrorEvent(HistoryPanelLogEvents.GhostSyncFailed, result.Exception, fields);
    }
}
