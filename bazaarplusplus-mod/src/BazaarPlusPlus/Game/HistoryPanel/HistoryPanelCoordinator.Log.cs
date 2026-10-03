#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.HistoryPanel;

// Terminal events of the panel's user-initiated requests. Every request flow writes exactly one
// terminal event on each path that returns or throws, and none when a stale panel session
// abandons the request. The request id only correlates a request's lines.
internal sealed partial class HistoryPanelCoordinator
{
    private static string NewLogRequestId() => Guid.NewGuid().ToString("N");

    private static long LogTimestampMilliseconds() =>
        (long)(Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency);

    private static void LogReplayPreflight(
        string requestId,
        string battleId,
        bool recordVideo,
        bool canRecord,
        HistoryPanelReplayReasonCode reasonCode
    ) =>
        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.HistoryPanel,
                "history_panel.replay.preflight_completed"
            ),
            () =>
                [
                    ("request_id", requestId, BppLogCorrelationPolicy.Short),
                    ("battle_id", battleId, BppLogCorrelationPolicy.Short),
                    ("record_video", recordVideo),
                    ("can_record", canRecord),
                    ("reason_code", reasonCode),
                ]
        );

    private static void LogReplayAccepted(string requestId, string battleId, bool recordVideo) =>
        BppLog.DebugEvent(
            new BppLogEvent(BppLogFeatureScope.HistoryPanel, "history_panel.replay.accepted"),
            () =>
                [
                    ("request_id", requestId, BppLogCorrelationPolicy.Short),
                    ("battle_id", battleId, BppLogCorrelationPolicy.Short),
                    ("record_video", recordVideo),
                ]
        );

    private static void LogReplayFailed(
        string requestId,
        string battleId,
        bool recordVideo,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception
    )
    {
        var logEvent = new BppLogEvent(
            BppLogFeatureScope.HistoryPanel,
            "history_panel.replay.failed"
        );
        var fields = new BppLogField[]
        {
            ("request_id", requestId, BppLogCorrelationPolicy.Short),
            ("battle_id", battleId, BppLogCorrelationPolicy.Short),
            ("record_video", recordVideo),
            ("reason_code", reasonCode),
        };
        if (exception == null)
            BppLog.ErrorEvent(logEvent, fields);
        else
            BppLog.ErrorEvent(logEvent, exception, fields);
    }

    private static void LogRunDeleteFailed(
        string requestId,
        string runId,
        int battleCount,
        Exception exception
    ) =>
        BppLog.ErrorEvent(
            new BppLogEvent(BppLogFeatureScope.HistoryPanel, "history_panel.run_delete.failed"),
            exception,
            RunDeleteFields(
                requestId,
                runId,
                battleCount,
                cleanupFailedCount: 0,
                HistoryPanelRunDeleteReasonCode.PrimaryDeleteFailed
            )
        );

    /// <summary>Writes <c>degraded</c> when any replay payload cleanup failed, else <c>succeeded</c>.</summary>
    private static void LogRunDeleteCompleted(
        string requestId,
        string runId,
        int battleCount,
        int cleanupFailedCount,
        Exception? cleanupException
    )
    {
        if (cleanupFailedCount <= 0)
        {
            BppLog.InfoEvent(
                new BppLogEvent(
                    BppLogFeatureScope.HistoryPanel,
                    "history_panel.run_delete.succeeded"
                ),
                RunDeleteFields(
                    requestId,
                    runId,
                    battleCount,
                    cleanupFailedCount: 0,
                    HistoryPanelRunDeleteReasonCode.Completed
                )
            );
            return;
        }

        var degraded = new BppLogEvent(
            BppLogFeatureScope.HistoryPanel,
            "history_panel.run_delete.degraded"
        );
        var fields = RunDeleteFields(
            requestId,
            runId,
            battleCount,
            cleanupFailedCount,
            HistoryPanelRunDeleteReasonCode.ReplayPayloadCleanupFailed
        );
        if (cleanupException == null)
            BppLog.WarnEvent(degraded, fields);
        else
            BppLog.WarnEvent(degraded, cleanupException, fields);
    }

    private static BppLogField[] RunDeleteFields(
        string requestId,
        string runId,
        int battleCount,
        int cleanupFailedCount,
        HistoryPanelRunDeleteReasonCode reasonCode
    ) =>
        [
            ("request_id", requestId, BppLogCorrelationPolicy.Short),
            ("run_id", runId, BppLogCorrelationPolicy.Short),
            ("battle_count", Math.Max(0, battleCount)),
            ("cleanup_failed_count", Math.Max(0, cleanupFailedCount)),
            ("reason_code", reasonCode),
        ];

    private static void LogServerHealth(
        string requestId,
        long startedAtMilliseconds,
        bool succeeded,
        HistoryPanelServerHealthReasonCode reasonCode,
        Exception? exception
    )
    {
        var fields = new BppLogField[]
        {
            ("request_id", requestId, BppLogCorrelationPolicy.Short),
            ("duration_ms", Math.Max(0, LogTimestampMilliseconds() - startedAtMilliseconds)),
            ("reason_code", reasonCode),
        };
        if (succeeded)
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

        var failed = new BppLogEvent(
            BppLogFeatureScope.HistoryPanel,
            "history_panel.server_health.failed"
        );
        if (exception == null)
            BppLog.ErrorEvent(failed, fields);
        else
            BppLog.ErrorEvent(failed, exception, fields);
    }

    private static void LogGhostSync(
        string requestId,
        bool succeeded,
        int importedCount,
        HistoryPanelGhostSyncReasonCode reasonCode,
        Exception? exception
    )
    {
        var fields = new BppLogField[]
        {
            ("request_id", requestId, BppLogCorrelationPolicy.Short),
            ("imported_count", Math.Max(0, importedCount)),
            ("reason_code", reasonCode),
        };
        if (succeeded)
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

        var failed = new BppLogEvent(
            BppLogFeatureScope.HistoryPanel,
            "history_panel.ghost_sync.failed"
        );
        if (exception == null)
            BppLog.ErrorEvent(failed, fields);
        else
            BppLog.ErrorEvent(failed, exception, fields);
    }
}
