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
    PreferenceParseException,
    AccountScopeException,
    OperationException,
    OwnerUnavailable,
    ReflectionUnavailable,
}

internal enum RandomPoolKind
{
    Hero,
    Collectible,
}

internal enum HeroPoolOperation
{
    Attach,
    ProjectInitialVisual,
    ProjectVisualUpdate,
    RouteCardClick,
    RouteRandomSelection,
    ResolveOwner,
    ResolveNativeFields,
}

internal enum CollectiblePoolOperation
{
    BeginFetch,
    ProjectFetch,
    EndFetch,
    RegisterCard,
    ProjectVisual,
    RouteClick,
    PreserveEquipped,
    RestoreVisuals,
    ApplyRandomizedLoadout,
}

internal enum CollectiblePoolKind
{
    Unknown,
    All,
    HeroSkins,
    Toys,
    Boards,
    Carpets,
    CardBacks,
    Album,
    Stash,
    Bank,
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

    internal static readonly BppLogFieldDefinition RandomPoolPreferencesDegradedPoolKind =
        PublicLow(0, "pool_kind");
    internal static readonly BppLogFieldDefinition RandomPoolPreferencesDegradedReasonCode =
        PublicLow(1, "reason_code");
    internal static readonly BppLogEventDefinition RandomPoolPreferencesDegraded = new(
        BppLogFeatureScope.Lobby,
        "lobby.random_pool_preferences.degraded",
        [RandomPoolPreferencesDegradedPoolKind, RandomPoolPreferencesDegradedReasonCode],
        new BppLogStormPolicy([
            RandomPoolPreferencesDegradedPoolKind,
            RandomPoolPreferencesDegradedReasonCode,
        ])
    );

    internal static readonly BppLogFieldDefinition HeroPoolDegradedOperation = PublicLow(
        0,
        "operation"
    );
    internal static readonly BppLogFieldDefinition HeroPoolDegradedReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition HeroPoolDegraded = new(
        BppLogFeatureScope.Lobby,
        "lobby.hero_pool.degraded",
        [HeroPoolDegradedOperation, HeroPoolDegradedReasonCode],
        new BppLogStormPolicy([HeroPoolDegradedOperation, HeroPoolDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition CollectiblePoolDegradedOperation = PublicLow(
        0,
        "operation"
    );
    internal static readonly BppLogFieldDefinition CollectiblePoolDegradedCollectionKind =
        PublicLow(1, "collection_kind");
    internal static readonly BppLogFieldDefinition CollectiblePoolDegradedReasonCode = PublicLow(
        2,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition CollectiblePoolDegraded = new(
        BppLogFeatureScope.Lobby,
        "lobby.collectible_pool.degraded",
        [
            CollectiblePoolDegradedOperation,
            CollectiblePoolDegradedCollectionKind,
            CollectiblePoolDegradedReasonCode,
        ],
        new BppLogStormPolicy([CollectiblePoolDegradedOperation, CollectiblePoolDegradedReasonCode])
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

    private static BppLogFieldDefinition Untrusted(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.UntrustedText,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.High
        );
}
