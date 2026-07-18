#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal enum CollectionPanelLogReasonCode
{
    OverlayHostUnavailable,
    NotMounted,
    UnknownPanel,
    CombatActive,
    ProbeReadFailed,
    PendingBindWaitFailed,
    CardMapTaskFailed,
    CardMapNull,
    StaticDataNotReady,
    TemplateLookupFailed,
    BindException,
    Untracked,
    ButtonMissing,
    MissingCollectionButton,
    GearFootprintUnavailable,
    CollectionFootprintUnavailable,
    AnchorCanvasUnavailable,
    PlacementBlocked,
    TargetLocalPositionUnavailable,
    AddressablesLoadException,
    AddressablesLoadFailed,
    InvalidSavedHero,
    UnsupportedHero,
    IdentityUnavailable,
    ResourceMissing,
    ResourceStreamUnavailable,
    SourceCatalogInvalid,
    LocaleChange,
    RuntimeDispose,
    StaticDataManagerChanged,
    TierTooltipMergeException,
    CachedLoadFailed,
    NativePreviewUnavailable,
    NativePreviewRuntimeFailed,
}

internal enum CollectionTierField
{
    Active,
    Passive,
    Cooldown,
}

internal enum CollectionPortraitReasonCode
{
    CollectionManagerUnavailable,
    DefaultSkinUnavailable,
    PortraitUnavailable,
    LoadException,
    ArtKeyUnavailable,
    AssetLoaderUnavailable,
    EncounterAssetUnavailable,
}

internal enum CollectionTypographyReasonCode
{
    IconResolveException,
    ConfigurationMethodUnavailable,
    ConfigurationInvocationException,
}

internal enum CollectionPanelSelectionProbe
{
    RunState,
    Hero,
    Day,
    Encounter,
}

internal enum CollectionPanelLoadPhase
{
    OpenPrologue,
    PanelLoad,
}

internal enum CollectionPanelLoadOutcome
{
    Completed,
    Loaded,
    Unavailable,
}

internal enum CollectionPanelLoadSegment
{
    CatalogAcquire,
    Catalog,
    Filter,
    Refresh,
}

internal enum CollectionCardBindStage
{
    TemplateLookup,
    Bind,
}

internal enum CollectionCardDisplayStage
{
    Show,
}

internal enum CollectionGridPerformancePhase
{
    FirstWindowBind,
}

internal enum CollectionCardArtStatus
{
    ArtUnavailable,
}

internal enum CollectionCacheKind
{
    Art,
    Material,
}

internal enum CollectionCacheCleanupStage
{
    Release,
    EvictRelease,
    Destroy,
    EvictDestroy,
}

internal enum CollectionHoverOperation
{
    OnHover,
    OnHoverOut,
}

[BppLogEventSource]
internal static class CollectionPanelLogEvents
{
    internal static readonly BppLogFieldDefinition MountFailedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition MountFailed = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.mount.failed",
        [MountFailedReasonCode]
    );

    internal static readonly BppLogFieldDefinition OpenFailedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition OpenFailed = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.open.failed",
        [OpenFailedReasonCode]
    );

    internal static readonly BppLogFieldDefinition OpenSkippedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition OpenSkipped = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.open.skipped",
        [OpenSkippedReasonCode]
    );

    internal static readonly BppLogFieldDefinition SelectionResolvedSource = Public(
        0,
        "source",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition SelectionResolvedHero = Public(
        1,
        "hero",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition SelectionResolvedDay = Public(
        2,
        "day",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition SelectionResolvedEncounterId = Public(
        3,
        "encounter_id",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition SelectionResolved = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.selection.resolved",
        [
            SelectionResolvedSource,
            SelectionResolvedHero,
            SelectionResolvedDay,
            SelectionResolvedEncounterId,
        ]
    );

    internal static readonly BppLogFieldDefinition SelectionDegradedProbe = Public(
        0,
        "probe",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition SelectionDegradedReasonCode = Public(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition SelectionDegraded = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.selection.degraded",
        [SelectionDegradedProbe, SelectionDegradedReasonCode],
        new BppLogStormPolicy([SelectionDegradedProbe, SelectionDegradedReasonCode])
    );
    internal static readonly BppLogFieldDefinition SelectionRecoveredProbe = Public(
        0,
        "probe",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition SelectionRecovered = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.selection.recovered",
        [SelectionRecoveredProbe]
    );

    internal static readonly BppLogFieldDefinition LoadPhase = Public(
        0,
        "phase",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition LoadOutcome = Public(
        1,
        "outcome",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition LoadReasonCode = Public(
        2,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition LoadDurationMs = Public(
        3,
        "duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition LoadCatalogAcquireDurationMs = Public(
        4,
        "catalog_acquire_duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition LoadCatalogDurationMs = Public(
        5,
        "catalog_duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition LoadFilterDurationMs = Public(
        6,
        "filter_duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition LoadRefreshDurationMs = Public(
        7,
        "refresh_duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition LoadCatalogCacheHit = Public(
        8,
        "catalog_cache_hit",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition LoadSourceTemplateCount = Public(
        9,
        "source_template_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition LoadAcceptedCount = Public(
        10,
        "accepted_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition LoadRejectedCount = Public(
        11,
        "rejected_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition LoadCatalogCardCount = Public(
        12,
        "catalog_card_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition LoadVisibleCardCount = Public(
        13,
        "visible_card_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition LoadCompleted = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.load.completed",
        [
            LoadPhase,
            LoadOutcome,
            LoadReasonCode,
            LoadDurationMs,
            LoadCatalogAcquireDurationMs,
            LoadCatalogDurationMs,
            LoadFilterDurationMs,
            LoadRefreshDurationMs,
            LoadCatalogCacheHit,
            LoadSourceTemplateCount,
            LoadAcceptedCount,
            LoadRejectedCount,
            LoadCatalogCardCount,
            LoadVisibleCardCount,
        ]
    );

    internal static readonly BppLogFieldDefinition CleanupDegradedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CleanupDegraded = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.cleanup.degraded",
        [CleanupDegradedReasonCode],
        new BppLogStormPolicy([CleanupDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition CardBindDegradedStage = Public(
        0,
        "stage",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CardBindDegradedTemplateId = Public(
        1,
        "template_id",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition CardBindDegradedReasonCode = Public(
        2,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CardBindDegraded = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.card.bind_degraded",
        [CardBindDegradedStage, CardBindDegradedTemplateId, CardBindDegradedReasonCode],
        new BppLogStormPolicy([CardBindDegradedStage, CardBindDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition CardReturnSkippedKind = Public(
        0,
        "kind",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CardReturnSkippedReasonCode = Public(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CardReturnSkipped = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.card.return_skipped",
        [CardReturnSkippedKind, CardReturnSkippedReasonCode]
    );

    internal static readonly BppLogFieldDefinition DockButtonSetupFailedPlacement = Public(
        0,
        "placement",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition DockButtonSetupFailedReasonCode = Public(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition DockButtonSetupFailed = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.dock_button.setup_failed",
        [DockButtonSetupFailedPlacement, DockButtonSetupFailedReasonCode]
    );

    internal static readonly BppLogFieldDefinition DockLayoutDegradedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition DockLayoutDegradedBlocker = Untrusted(
        1,
        "blocker",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition DockLayoutDegraded = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.dock_layout.degraded",
        [DockLayoutDegradedReasonCode, DockLayoutDegradedBlocker],
        new BppLogStormPolicy([DockLayoutDegradedReasonCode])
    );
    internal static readonly BppLogFieldDefinition DockLayoutRecoveredReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition DockLayoutRecoveredBlocker = Untrusted(
        1,
        "blocker",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition DockLayoutRecovered = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.dock_layout.recovered",
        [DockLayoutRecoveredReasonCode, DockLayoutRecoveredBlocker]
    );

    internal static readonly BppLogFieldDefinition CardDisplayFailedStage = Public(
        0,
        "stage",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CardDisplayFailedTemplateId = Public(
        1,
        "template_id",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition CardDisplayFailed = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.card.display_failed",
        [CardDisplayFailedStage, CardDisplayFailedTemplateId]
    );

    internal static readonly BppLogFieldDefinition GridPerformancePhase = Public(
        0,
        "phase",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition GridPerformanceFirstIndex = Public(
        1,
        "first_index",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition GridPerformanceLastIndex = Public(
        2,
        "last_index",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition GridPerformanceWindowCount = Public(
        3,
        "window_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition GridPerformanceVisibleCount = Public(
        4,
        "visible_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition GridPerformanceShelfCount = Public(
        5,
        "shelf_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition GridPerformanceAttemptCount = Public(
        6,
        "attempt_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition GridPerformanceBoundCount = Public(
        7,
        "bound_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition GridPerformanceFailedBindCount = Public(
        8,
        "failed_bind_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition GridPerformanceBindDurationMs = Public(
        9,
        "bind_duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition GridPerformanceElapsedMs = Public(
        10,
        "elapsed_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition GridPerformanceFaultedCount = Public(
        11,
        "faulted_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition GridPerformanceCanceledCount = Public(
        12,
        "canceled_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition GridPerformanceObserved = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.grid.performance_observed",
        [
            GridPerformancePhase,
            GridPerformanceFirstIndex,
            GridPerformanceLastIndex,
            GridPerformanceWindowCount,
            GridPerformanceVisibleCount,
            GridPerformanceShelfCount,
            GridPerformanceAttemptCount,
            GridPerformanceBoundCount,
            GridPerformanceFailedBindCount,
            GridPerformanceBindDurationMs,
            GridPerformanceElapsedMs,
            GridPerformanceFaultedCount,
            GridPerformanceCanceledCount,
        ]
    );

    internal static readonly BppLogFieldDefinition CardArtDegradedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CardArtDegradedStatus = Public(
        1,
        "status",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CardArtDegradedArtKey = Untrusted(
        2,
        "art_key",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition CardArtDegraded = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.card_art.degraded",
        [CardArtDegradedReasonCode, CardArtDegradedStatus, CardArtDegradedArtKey],
        new BppLogStormPolicy([CardArtDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition TierTooltipDegradedTierField = Public(
        0,
        "tier_field",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition TierTooltipDegradedReasonCode = Public(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition TierTooltipDegraded = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.tier_tooltip.degraded",
        [TierTooltipDegradedTierField, TierTooltipDegradedReasonCode],
        new BppLogStormPolicy([TierTooltipDegradedTierField, TierTooltipDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition HeroPortraitDegradedHero = Public(
        0,
        "hero",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition HeroPortraitDegradedReasonCode = Public(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition HeroPortraitDegraded = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.hero_portrait.degraded",
        [HeroPortraitDegradedHero, HeroPortraitDegradedReasonCode],
        new BppLogStormPolicy([HeroPortraitDegradedReasonCode])
    );
    internal static readonly BppLogFieldDefinition HeroPortraitFallbackHero = Public(
        0,
        "hero",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition HeroPortraitFallbackReasonCode = Public(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition HeroPortraitFallbackObserved = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.hero_portrait.fallback_observed",
        [HeroPortraitFallbackHero, HeroPortraitFallbackReasonCode]
    );

    internal static readonly BppLogFieldDefinition EncounterPortraitDegradedTemplateId = Public(
        0,
        "template_id",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition EncounterPortraitDegradedReasonCode = Public(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition EncounterPortraitDegradedArtKey = Untrusted(
        2,
        "art_key",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition EncounterPortraitDegraded = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.encounter_portrait.degraded",
        [
            EncounterPortraitDegradedTemplateId,
            EncounterPortraitDegradedReasonCode,
            EncounterPortraitDegradedArtKey,
        ],
        new BppLogStormPolicy([EncounterPortraitDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition KeywordIconDegradedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition KeywordIconDegradedIconName = Untrusted(
        1,
        "icon_name",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition KeywordIconDegraded = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.keyword_icon.degraded",
        [KeywordIconDegradedReasonCode, KeywordIconDegradedIconName],
        new BppLogStormPolicy([KeywordIconDegradedReasonCode])
    );
    internal static readonly BppLogFieldDefinition TagTypographyDegradedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition TagTypographyDegraded = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.tag_typography.degraded",
        [TagTypographyDegradedReasonCode],
        new BppLogStormPolicy([TagTypographyDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition CacheCleanupFailedCache = Public(
        0,
        "cache",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CacheCleanupFailedStage = Public(
        1,
        "stage",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CacheCleanupFailedArtKey = Untrusted(
        2,
        "art_key",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition CacheCleanupFailed = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.cache.cleanup_failed",
        [CacheCleanupFailedCache, CacheCleanupFailedStage, CacheCleanupFailedArtKey]
    );

    internal static readonly BppLogFieldDefinition HoverInvokeFailedOperation = Public(
        0,
        "operation",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition HoverInvokeFailed = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.hover.invoke_failed",
        [HoverInvokeFailedOperation]
    );

    internal static readonly BppLogFieldDefinition HeroPreferenceDegradedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition HeroPreferenceDegradedHero = Public(
        1,
        "hero",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition HeroPreferenceDegraded = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.hero_preference.degraded",
        [HeroPreferenceDegradedReasonCode, HeroPreferenceDegradedHero],
        new BppLogStormPolicy([HeroPreferenceDegradedReasonCode])
    );
    internal static readonly BppLogFieldDefinition HeroPreferenceScopeDegradedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition HeroPreferenceScopeDegraded = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.hero_preference.scope_degraded",
        [HeroPreferenceScopeDegradedReasonCode],
        new BppLogStormPolicy([HeroPreferenceScopeDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition SourceCatalogLoadedEntryCount = Public(
        0,
        "entry_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition SourceCatalogLoadedSourceTemplateCount = Public(
        1,
        "source_template_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition SourceCatalogLoaded = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.source_catalog.loaded",
        [SourceCatalogLoadedEntryCount, SourceCatalogLoadedSourceTemplateCount]
    );
    internal static readonly BppLogFieldDefinition SourceCatalogLoadFailedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition SourceCatalogLoadFailedResourceSuffix = Public(
        1,
        "resource_suffix",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition SourceCatalogLoadFailed = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.source_catalog.load_failed",
        [SourceCatalogLoadFailedReasonCode, SourceCatalogLoadFailedResourceSuffix]
    );

    internal static readonly BppLogFieldDefinition CatalogBuildDeferredReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CatalogBuildDeferred = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.catalog.build_deferred",
        [CatalogBuildDeferredReasonCode]
    );
    internal static readonly BppLogFieldDefinition CatalogDegradedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CatalogDegraded = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.catalog.degraded",
        [CatalogDegradedReasonCode],
        new BppLogStormPolicy([CatalogDegradedReasonCode])
    );
    internal static readonly BppLogFieldDefinition CatalogReadyAcceptedCount = Public(
        0,
        "accepted_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition CatalogReadyRejectedCount = Public(
        1,
        "rejected_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition CatalogReadySourceTemplateCount = Public(
        2,
        "source_template_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition CatalogReady = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.catalog.ready",
        [CatalogReadyAcceptedCount, CatalogReadyRejectedCount, CatalogReadySourceTemplateCount]
    );
    internal static readonly BppLogFieldDefinition CatalogRecoveredAcceptedCount = Public(
        0,
        "accepted_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition CatalogRecoveredRejectedCount = Public(
        1,
        "rejected_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition CatalogRecoveredSourceTemplateCount = Public(
        2,
        "source_template_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition CatalogRecovered = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.catalog.recovered",
        [
            CatalogRecoveredAcceptedCount,
            CatalogRecoveredRejectedCount,
            CatalogRecoveredSourceTemplateCount,
        ]
    );
    internal static readonly BppLogFieldDefinition CatalogInvalidatedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CatalogInvalidated = new(
        BppLogFeatureScope.CollectionPanel,
        "collection_panel.catalog.invalidated",
        [CatalogInvalidatedReasonCode]
    );

    private static BppLogFieldDefinition Public(
        int order,
        string name,
        BppLogCardinality cardinality
    ) => new(order, name, BppLogFieldPrivacy.Public, BppLogCorrelationPolicy.None, cardinality);

    private static BppLogFieldDefinition Untrusted(
        int order,
        string name,
        BppLogCardinality cardinality
    ) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.UntrustedText,
            BppLogCorrelationPolicy.None,
            cardinality
        );
}
