#nullable enable

namespace BazaarPlusPlus.Game.Settings;

internal enum SettingsLogReasonCode
{
    InvalidBindingPath,
    NoResolvedControls,
    ResourceMissing,
    ResourceStreamUnavailable,
    ResourceDecodeFailed,
    SupportSectionUnavailable,
    ScrollSpyEntryUnavailable,
    InstallException,
    GeometryUnavailable,
    SupportBufferUnavailable,
    RetryExhausted,
    PatchException,
}

internal enum SettingsDockSpriteResourceId
{
    CollectionPanelIcon,
    ReplayExportIcon,
    ReplayRecordingIcon,
    ReplayViewIcon,
    ReplayRetryIcon,
}

internal enum SettingsNativeSectionStage
{
    BuildSection,
    ResolveSupportSection,
    ResolveScrollSpyEntry,
    Install,
}

internal enum SettingsNativeLayoutOperation
{
    FooterGeometry,
    FooterSiblings,
    SupportBottomBuffer,
    SplitContainer,
}

internal enum SettingsNativeLayoutOutcome
{
    Applied,
    Skipped,
}

internal enum SettingsNativeButtonId
{
    MainMenu,
    HeroSelect,
    FightMenu,
}

internal enum SettingsKeybindStage
{
    TemplateDiscovery,
    Refresh,
}

internal enum SettingsPatchOperation
{
    DockAwake,
    DockOpen,
    NativeSectionInstall,
    LanguageRefresh,
}

internal enum SettingsRowLayoutMode
{
    Automatic,
    Manual,
}

internal enum SettingsRowId
{
    Unknown,
    HoldEnchantPreview,
    HoldUpgradePreview,
    HistoryPanel,
    CollectionPanel,
    LiveBuildPanel,
}
