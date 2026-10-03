#nullable enable

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
