#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus;

internal enum PluginInitializationPhase
{
    PluginVersion,
    Composition,
    StaticUtilities,
    HarmonyPatches,
    ReplayRuntime,
    Features,
    OnlineServices,
    Mountables,
}

internal enum PluginLogReasonCode
{
    VersionUnreadable,
    DetectionSignalsDisagree,
    InitializationException,
    TeardownStepFailed,
    InvalidBaseUrl,
    PatchClassException,
    PatchClassesFailed,
    HandlerException,
    FeatureException,
}

internal enum PluginOnlineEndpoint
{
    ModApi,
}

internal enum PluginEventId
{
    Unknown,
    ChineseLocaleModeChanged,
    CombatFrameAdvanced,
    CombatReplayPersistenceDrained,
    CombatReplayPlaybackEnded,
    CombatReplayPlaybackStarting,
    CombatSimObserved,
    NetMessageObserved,
    PvpBattleRecorded,
    RunInitializedObserved,
    RunLifecycleChanged,
}

internal enum PluginHandlerId
{
    Unknown,
    BackgroundUploadPump,
    CollectionPanelMount,
    CombatReplayModule,
    CombatReplayVideoRecorder,
    CombatStatusBarModule,
    EndOfRunCaptureDriver,
    HistoryPanelMount,
    RunBundleUploadFeed,
    RunLifecycleModule,
    RunLoggingModule,
}

internal enum PluginFeatureId
{
    Unknown,
    RunLifecycle,
    CombatReplay,
    CombatStatusBar,
    VoiceSubtitlesInterop,
    VoiceSubtitles,
}

[BppLogEventSource]
internal static class PluginLogEvents
{
    internal static readonly BppLogFieldDefinition InitializationSucceededPluginVersion = Public(
        0,
        "plugin_version",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition InitializationSucceededGameBuild = Untrusted(
        1,
        "game_build"
    );
    internal static readonly BppLogFieldDefinition InitializationSucceededBuildChannel = Public(
        2,
        "build_channel",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition InitializationSucceeded = new(
        BppLogFeatureScope.Plugin,
        "plugin.initialization.succeeded",
        [
            InitializationSucceededPluginVersion,
            InitializationSucceededGameBuild,
            InitializationSucceededBuildChannel,
        ]
    );

    internal static readonly BppLogFieldDefinition GameBuildDegradedGameBuild = Untrusted(
        0,
        "game_build"
    );
    internal static readonly BppLogFieldDefinition GameBuildDegradedBuildChannel = Public(
        1,
        "build_channel",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition GameBuildDegradedReasonCode = Public(
        2,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition GameBuildDegraded = new(
        BppLogFeatureScope.Plugin,
        "plugin.game_build.degraded",
        [GameBuildDegradedGameBuild, GameBuildDegradedBuildChannel, GameBuildDegradedReasonCode],
        new BppLogStormPolicy([GameBuildDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition InitializationFailedPhase = Public(
        0,
        "phase",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition InitializationFailedReasonCode = Public(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition InitializationFailed = new(
        BppLogFeatureScope.Plugin,
        "plugin.initialization.failed",
        [InitializationFailedPhase, InitializationFailedReasonCode]
    );

    internal static readonly BppLogFieldDefinition ShutdownDegradedFailedStepCount = Public(
        0,
        "failed_step_count",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition ShutdownDegradedFirstFailedStep = Public(
        1,
        "first_failed_step",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition ShutdownDegradedReasonCode = Public(
        2,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition ShutdownDegraded = new(
        BppLogFeatureScope.Plugin,
        "plugin.shutdown.degraded",
        [
            ShutdownDegradedFailedStepCount,
            ShutdownDegradedFirstFailedStep,
            ShutdownDegradedReasonCode,
        ],
        new BppLogStormPolicy([ShutdownDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition OnlineServicesDegradedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition OnlineServicesDegradedEndpoint = Public(
        1,
        "endpoint",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition OnlineServicesDegraded = new(
        BppLogFeatureScope.Plugin,
        "plugin.online_services.degraded",
        [OnlineServicesDegradedReasonCode, OnlineServicesDegradedEndpoint],
        new BppLogStormPolicy([OnlineServicesDegradedEndpoint, OnlineServicesDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition PatchApplyFailedPatchType = Untrusted(
        0,
        "patch_type"
    );
    internal static readonly BppLogFieldDefinition PatchApplyFailedReasonCode = Public(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition PatchApplyFailed = new(
        BppLogFeatureScope.Plugin,
        "plugin.patch.apply_failed",
        [PatchApplyFailedPatchType, PatchApplyFailedReasonCode]
    );
    internal static readonly BppLogFieldDefinition PatchesDegradedFailedPatchCount = Public(
        0,
        "failed_patch_count",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition PatchesDegradedReasonCode = Public(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition PatchesDegraded = new(
        BppLogFeatureScope.Plugin,
        "plugin.patches.degraded",
        [PatchesDegradedFailedPatchCount, PatchesDegradedReasonCode],
        new BppLogStormPolicy([PatchesDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition EventHandlerDegradedEventId = Public(
        0,
        "event_id",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition EventHandlerDegradedHandlerId = Public(
        1,
        "handler_id",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition EventHandlerDegradedReasonCode = Public(
        2,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition EventHandlerDegraded = new(
        BppLogFeatureScope.Plugin,
        "plugin.event_handler.degraded",
        [
            EventHandlerDegradedEventId,
            EventHandlerDegradedHandlerId,
            EventHandlerDegradedReasonCode,
        ],
        new BppLogStormPolicy([EventHandlerDegradedEventId, EventHandlerDegradedHandlerId])
    );

    internal static readonly BppLogFieldDefinition FeatureDegradedFeature = Public(
        0,
        "feature",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition FeatureDegradedReasonCode = Public(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition FeatureStartDegraded = new(
        BppLogFeatureScope.Plugin,
        "plugin.feature_start.degraded",
        [FeatureDegradedFeature, FeatureDegradedReasonCode],
        new BppLogStormPolicy([FeatureDegradedFeature, FeatureDegradedReasonCode])
    );
    internal static readonly BppLogEventDefinition FeatureStopDegraded = new(
        BppLogFeatureScope.Plugin,
        "plugin.feature_stop.degraded",
        [FeatureDegradedFeature, FeatureDegradedReasonCode],
        new BppLogStormPolicy([FeatureDegradedFeature, FeatureDegradedReasonCode])
    );

    private static BppLogFieldDefinition Public(
        int order,
        string name,
        BppLogCardinality cardinality
    ) => new(order, name, BppLogFieldPrivacy.Public, BppLogCorrelationPolicy.None, cardinality);

    private static BppLogFieldDefinition Untrusted(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.UntrustedText,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.High
        );
}
