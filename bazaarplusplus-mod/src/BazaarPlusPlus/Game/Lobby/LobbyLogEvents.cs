#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.Lobby;

internal enum LobbyLogReasonCode
{
    HttpFailureStatus,
    ManifestVersionMissing,
    RequestTimedOut,
    RequestException,
    LabelRefreshException,
}

[BppLogEventSource]
internal static class LobbyLogEvents
{
    internal static readonly BppLogFieldDefinition VersionCheckDegradedReasonCode = PublicLow(
        0,
        "reason_code"
    );
    internal static readonly BppLogFieldDefinition VersionCheckDegradedHttpStatus = PublicLow(
        1,
        "http_status"
    );
    internal static readonly BppLogFieldDefinition VersionCheckDegradedTimeoutMs = PublicLow(
        2,
        "timeout_ms"
    );
    internal static readonly BppLogEventDefinition VersionCheckDegraded = new(
        BppLogFeatureScope.Lobby,
        "lobby.version_check.degraded",
        [
            VersionCheckDegradedReasonCode,
            VersionCheckDegradedHttpStatus,
            VersionCheckDegradedTimeoutMs,
        ],
        new BppLogStormPolicy([VersionCheckDegradedReasonCode])
    );
    internal static readonly BppLogFieldDefinition VersionCheckCompletedCurrentVersion = PublicHigh(
        0,
        "current_version"
    );
    internal static readonly BppLogFieldDefinition VersionCheckCompletedLatestVersion = Untrusted(
        1,
        "latest_version"
    );
    internal static readonly BppLogFieldDefinition VersionCheckCompletedUpdateAvailable = PublicLow(
        2,
        "update_available"
    );
    internal static readonly BppLogEventDefinition VersionCheckCompleted = new(
        BppLogFeatureScope.Lobby,
        "lobby.version_check.completed",
        [
            VersionCheckCompletedCurrentVersion,
            VersionCheckCompletedLatestVersion,
            VersionCheckCompletedUpdateAvailable,
        ]
    );

    internal static readonly BppLogFieldDefinition VersionLabelDegradedReasonCode = PublicLow(
        0,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition VersionLabelDegraded = new(
        BppLogFeatureScope.Lobby,
        "lobby.version_label.degraded",
        [VersionLabelDegradedReasonCode],
        new BppLogStormPolicy([VersionLabelDegradedReasonCode])
    );

    private static BppLogFieldDefinition PublicLow(int order, string name) =>
        new(order, name, BppLogCorrelationPolicy.None, BppLogCardinality.Low);

    private static BppLogFieldDefinition PublicHigh(int order, string name) =>
        new(order, name, BppLogCorrelationPolicy.None, BppLogCardinality.High);

    private static BppLogFieldDefinition Untrusted(int order, string name) =>
        new(order, name, BppLogCorrelationPolicy.None, BppLogCardinality.High);
}
