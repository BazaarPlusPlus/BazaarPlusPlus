#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.HistoryPanel;

[BppLogEventSource]
internal static class HistoryPanelLogEvents
{
    internal static readonly BppLogFieldDefinition MountDependency = PublicLow(0, "dependency");
    internal static readonly BppLogFieldDefinition MountReasonCode = PublicLow(1, "reason_code");
    internal static readonly BppLogEventDefinition MountFailed = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.mount.failed",
        [MountDependency, MountReasonCode]
    );

    internal static readonly BppLogFieldDefinition DataDataset = PublicLow(0, "dataset");
    internal static readonly BppLogFieldDefinition DataRunId = PublicHighShort(1, "run_id");
    internal static readonly BppLogEventDefinition DataLoadFailed = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.data.load_failed",
        [DataDataset, DataRunId]
    );

    internal static readonly BppLogFieldDefinition ReplayRequestId = PublicHighShort(
        0,
        "request_id"
    );
    internal static readonly BppLogFieldDefinition ReplayBattleId = PublicHighShort(1, "battle_id");
    internal static readonly BppLogFieldDefinition ReplayRecordVideo = PublicLow(2, "record_video");
    internal static readonly BppLogFieldDefinition ReplayCanRecord = PublicLow(3, "can_record");
    internal static readonly BppLogFieldDefinition ReplayPreflightReasonCode = PublicLow(
        4,
        "reason_code"
    );
    internal static readonly BppLogFieldDefinition ReplayFailureReasonCode = PublicLow(
        3,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition ReplayPreflightCompleted = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.replay.preflight_completed",
        [
            ReplayRequestId,
            ReplayBattleId,
            ReplayRecordVideo,
            ReplayCanRecord,
            ReplayPreflightReasonCode,
        ]
    );
    internal static readonly BppLogEventDefinition ReplayFailed = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.replay.failed",
        [ReplayRequestId, ReplayBattleId, ReplayRecordVideo, ReplayFailureReasonCode]
    );
    internal static readonly BppLogEventDefinition ReplayAccepted = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.replay.accepted",
        [ReplayRequestId, ReplayBattleId, ReplayRecordVideo]
    );

    internal static readonly BppLogFieldDefinition DeleteRequestId = PublicHighShort(
        0,
        "request_id"
    );
    internal static readonly BppLogFieldDefinition DeleteRunId = PublicHighShort(1, "run_id");
    internal static readonly BppLogFieldDefinition DeleteBattleCount = PublicHigh(
        2,
        "battle_count"
    );
    internal static readonly BppLogFieldDefinition DeleteCleanupFailedCount = PublicHigh(
        3,
        "cleanup_failed_count"
    );
    internal static readonly BppLogFieldDefinition DeleteReasonCode = PublicLow(4, "reason_code");
    internal static readonly BppLogEventDefinition RunDeleteFailed = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.run_delete.failed",
        DeleteFields()
    );
    internal static readonly BppLogEventDefinition RunDeleteDegraded = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.run_delete.degraded",
        DeleteFields()
    );
    internal static readonly BppLogEventDefinition RunDeleteSucceeded = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.run_delete.succeeded",
        DeleteFields()
    );

    internal static readonly BppLogFieldDefinition HealthRequestId = PublicHighShort(
        0,
        "request_id"
    );
    internal static readonly BppLogFieldDefinition HealthDurationMs = PublicHigh(1, "duration_ms");
    internal static readonly BppLogFieldDefinition HealthReasonCode = PublicLow(2, "reason_code");
    internal static readonly BppLogEventDefinition ServerHealthFailed = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.server_health.failed",
        HealthFields()
    );
    internal static readonly BppLogEventDefinition ServerHealthSucceeded = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.server_health.succeeded",
        HealthFields()
    );

    internal static readonly BppLogFieldDefinition SyncRequestId = PublicHighShort(0, "request_id");
    internal static readonly BppLogFieldDefinition SyncImportedCount = PublicHigh(
        1,
        "imported_count"
    );
    internal static readonly BppLogFieldDefinition SyncReasonCode = PublicLow(2, "reason_code");
    internal static readonly BppLogEventDefinition GhostSyncFailed = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.ghost_sync.failed",
        SyncFields()
    );
    internal static readonly BppLogEventDefinition GhostSyncSucceeded = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.ghost_sync.succeeded",
        SyncFields()
    );

    internal static readonly BppLogFieldDefinition GhostIdentityReasonCode = PublicLow(
        0,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition GhostIdentityReadFailed = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.ghost_identity.read_failed",
        [GhostIdentityReasonCode]
    );

    internal static readonly BppLogFieldDefinition PreviewTemplateId = PublicHigh(0, "template_id");
    internal static readonly BppLogFieldDefinition PreviewSocketReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogFieldDefinition PreviewStaticDataReasonCode = PublicLow(
        0,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition PreviewSocketEffectDegraded = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.preview.socket_effect_degraded",
        [PreviewTemplateId, PreviewSocketReasonCode],
        new BppLogStormPolicy([PreviewSocketReasonCode])
    );
    internal static readonly BppLogEventDefinition PreviewStaticDataDegraded = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.preview.static_data_degraded",
        [PreviewStaticDataReasonCode],
        new BppLogStormPolicy([PreviewStaticDataReasonCode])
    );
    internal static readonly BppLogFieldDefinition PreviewPayloadBattleId = PublicHighShort(
        0,
        "battle_id"
    );
    internal static readonly BppLogFieldDefinition PreviewPayloadReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition PreviewPayloadDegraded = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.preview.payload_degraded",
        [PreviewPayloadBattleId, PreviewPayloadReasonCode],
        new BppLogStormPolicy([PreviewPayloadReasonCode])
    );

    internal static readonly BppLogFieldDefinition RowBattleId = PublicHighShort(0, "battle_id");
    internal static readonly BppLogFieldDefinition RowReasonCode = PublicLow(1, "reason_code");
    internal static readonly BppLogEventDefinition RowSkipped = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.row.skipped",
        [RowBattleId, RowReasonCode],
        new BppLogStormPolicy([RowReasonCode])
    );

    internal static readonly BppLogFieldDefinition OpenReasonCode = PublicLow(0, "reason_code");
    internal static readonly BppLogEventDefinition OpenFailed = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.open.failed",
        [OpenReasonCode]
    );
    internal static readonly BppLogEventDefinition OpenSkipped = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.open.skipped",
        [OpenReasonCode]
    );

    internal static readonly BppLogFieldDefinition CardPreviewDegradedOperation = PublicLow(
        0,
        "operation"
    );
    internal static readonly BppLogFieldDefinition CardPreviewDegradedReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogFieldDefinition CardPreviewDegradedTemplateId = PublicHigh(
        2,
        "template_id"
    );
    internal static readonly BppLogEventDefinition CardPreviewDegraded = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.card_preview.degraded",
        [
            CardPreviewDegradedOperation,
            CardPreviewDegradedReasonCode,
            CardPreviewDegradedTemplateId,
        ],
        new BppLogStormPolicy([CardPreviewDegradedOperation, CardPreviewDegradedReasonCode])
    );
    internal static readonly BppLogFieldDefinition ItemBoardPreviewDegradedOperation = PublicLow(
        0,
        "operation"
    );
    internal static readonly BppLogFieldDefinition ItemBoardPreviewDegradedReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogFieldDefinition ItemBoardPreviewDegradedTemplateId = PublicHigh(
        2,
        "template_id"
    );
    internal static readonly BppLogEventDefinition ItemBoardPreviewDegraded = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.item_board_preview.degraded",
        [
            ItemBoardPreviewDegradedOperation,
            ItemBoardPreviewDegradedReasonCode,
            ItemBoardPreviewDegradedTemplateId,
        ],
        new BppLogStormPolicy([
            ItemBoardPreviewDegradedOperation,
            ItemBoardPreviewDegradedReasonCode,
        ])
    );

    private static BppLogFieldDefinition PublicLow(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.Public,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.Low
        );

    private static BppLogFieldDefinition PublicHigh(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.Public,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.High
        );

    private static BppLogFieldDefinition PublicHighShort(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.Public,
            BppLogCorrelationPolicy.Short,
            BppLogCardinality.High
        );

    private static BppLogFieldDefinition[] DeleteFields() =>
        [
            DeleteRequestId,
            DeleteRunId,
            DeleteBattleCount,
            DeleteCleanupFailedCount,
            DeleteReasonCode,
        ];

    private static BppLogFieldDefinition[] HealthFields() =>
        [HealthRequestId, HealthDurationMs, HealthReasonCode];

    private static BppLogFieldDefinition[] SyncFields() =>
        [SyncRequestId, SyncImportedCount, SyncReasonCode];
}
