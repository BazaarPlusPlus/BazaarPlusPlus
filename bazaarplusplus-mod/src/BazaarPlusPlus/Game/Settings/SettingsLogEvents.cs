#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

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
    PositionReadFailed,
    SupportBufferUnavailable,
    RetryExhausted,
    PatchException,
    FontAssetUnavailable,
    FontLoadException,
    GlyphWarmupException,
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
    RowInstall,
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
    ItemEnchantPreview,
    EventPreview,
    QuestPreview,
    CombatStatusBar,
    VoiceSubtitles,
}

[BppLogEventSource]
internal static class SettingsLogEvents
{
    internal static readonly BppLogFieldDefinition HotkeyDegradedActionId = PublicLow(
        0,
        "action_id"
    );
    internal static readonly BppLogFieldDefinition HotkeyDegradedBindingPath = HashedUntrusted(
        1,
        "binding_path"
    );
    internal static readonly BppLogFieldDefinition HotkeyDegradedReasonCode = PublicLow(
        2,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition HotkeyDegraded = new(
        BppLogFeatureScope.Settings,
        "settings.hotkey.degraded",
        [HotkeyDegradedActionId, HotkeyDegradedBindingPath, HotkeyDegradedReasonCode],
        new BppLogStormPolicy([HotkeyDegradedReasonCode])
    );
    internal static readonly BppLogFieldDefinition HotkeyModifierBindingPath = HashedUntrusted(
        0,
        "binding_path"
    );
    internal static readonly BppLogFieldDefinition HotkeyModifierLegacyPressed = PublicLow(
        1,
        "legacy_pressed"
    );
    internal static readonly BppLogFieldDefinition HotkeyModifierActionPressed = PublicLow(
        2,
        "action_pressed"
    );
    internal static readonly BppLogEventDefinition HotkeyModifierDisagreementObserved = new(
        BppLogFeatureScope.Settings,
        "settings.hotkey.modifier_disagreement_observed",
        [HotkeyModifierBindingPath, HotkeyModifierLegacyPressed, HotkeyModifierActionPressed]
    );

    internal static readonly BppLogFieldDefinition DockSpriteDegradedReasonCode = PublicLow(
        0,
        "reason_code"
    );
    internal static readonly BppLogFieldDefinition DockSpriteDegradedResourceId = PublicLow(
        1,
        "resource_id"
    );
    internal static readonly BppLogEventDefinition DockSpriteDegraded = new(
        BppLogFeatureScope.Settings,
        "settings.dock_sprite.degraded",
        [DockSpriteDegradedReasonCode, DockSpriteDegradedResourceId],
        new BppLogStormPolicy([DockSpriteDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition NativeSectionDegradedStage = PublicLow(
        0,
        "stage"
    );
    internal static readonly BppLogFieldDefinition NativeSectionDegradedReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition NativeSectionDegraded = new(
        BppLogFeatureScope.Settings,
        "settings.native_section.degraded",
        [NativeSectionDegradedStage, NativeSectionDegradedReasonCode],
        new BppLogStormPolicy([NativeSectionDegradedStage, NativeSectionDegradedReasonCode])
    );
    internal static readonly BppLogFieldDefinition NativeSectionRecoveredStage = PublicLow(
        0,
        "stage"
    );
    internal static readonly BppLogEventDefinition NativeSectionRecovered = new(
        BppLogFeatureScope.Settings,
        "settings.native_section.recovered",
        [NativeSectionRecoveredStage]
    );

    internal static readonly BppLogFieldDefinition NativeSectionLayoutObservedOperation = PublicLow(
        0,
        "operation"
    );
    internal static readonly BppLogFieldDefinition NativeSectionLayoutObservedOutcome = PublicLow(
        1,
        "outcome"
    );
    internal static readonly BppLogFieldDefinition NativeSectionLayoutObservedAffectedCount =
        PublicHigh(2, "affected_count");
    internal static readonly BppLogFieldDefinition NativeSectionLayoutObservedGrowthUnits =
        PublicHigh(3, "growth_units");
    internal static readonly BppLogEventDefinition NativeSectionLayoutObserved = new(
        BppLogFeatureScope.Settings,
        "settings.native_section.layout_observed",
        [
            NativeSectionLayoutObservedOperation,
            NativeSectionLayoutObservedOutcome,
            NativeSectionLayoutObservedAffectedCount,
            NativeSectionLayoutObservedGrowthUnits,
        ]
    );
    internal static readonly BppLogFieldDefinition NativeSectionLayoutDegradedOperation = PublicLow(
        0,
        "operation"
    );
    internal static readonly BppLogFieldDefinition NativeSectionLayoutDegradedReasonCode =
        PublicLow(1, "reason_code");
    internal static readonly BppLogEventDefinition NativeSectionLayoutDegraded = new(
        BppLogFeatureScope.Settings,
        "settings.native_section.layout_degraded",
        [NativeSectionLayoutDegradedOperation, NativeSectionLayoutDegradedReasonCode],
        new BppLogStormPolicy([
            NativeSectionLayoutDegradedOperation,
            NativeSectionLayoutDegradedReasonCode,
        ])
    );
    internal static readonly BppLogFieldDefinition NativeSectionLayoutRecoveredOperation =
        PublicLow(0, "operation");
    internal static readonly BppLogEventDefinition NativeSectionLayoutRecovered = new(
        BppLogFeatureScope.Settings,
        "settings.native_section.layout_recovered",
        [NativeSectionLayoutRecoveredOperation]
    );

    internal static readonly BppLogFieldDefinition NativeButtonClonedButtonId = PublicLow(
        0,
        "button_id"
    );
    internal static readonly BppLogEventDefinition NativeButtonCloned = new(
        BppLogFeatureScope.Settings,
        "settings.native_button.cloned",
        [NativeButtonClonedButtonId]
    );

    internal static readonly BppLogFieldDefinition KeybindRowsDegradedStage = PublicLow(0, "stage");
    internal static readonly BppLogFieldDefinition KeybindRowsDegradedReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition KeybindRowsDegraded = new(
        BppLogFeatureScope.Settings,
        "settings.keybind_rows.degraded",
        [KeybindRowsDegradedStage, KeybindRowsDegradedReasonCode],
        new BppLogStormPolicy([KeybindRowsDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition PatchDegradedOperation = PublicLow(
        0,
        "operation"
    );
    internal static readonly BppLogFieldDefinition PatchDegradedReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition PatchDegraded = new(
        BppLogFeatureScope.Settings,
        "settings.patch.degraded",
        [PatchDegradedOperation, PatchDegradedReasonCode],
        new BppLogStormPolicy([PatchDegradedOperation, PatchDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition RowLayoutAppliedLayoutMode = PublicLow(
        0,
        "layout_mode"
    );
    internal static readonly BppLogFieldDefinition RowLayoutAppliedRowId = PublicLow(1, "row_id");
    internal static readonly BppLogFieldDefinition RowLayoutAppliedAdditionalIndex = PublicHigh(
        2,
        "additional_index"
    );
    internal static readonly BppLogFieldDefinition RowLayoutAppliedStepPx = PublicHigh(
        3,
        "step_px"
    );
    internal static readonly BppLogFieldDefinition RowLayoutAppliedPositionXPx = PublicHigh(
        4,
        "position_x_px"
    );
    internal static readonly BppLogFieldDefinition RowLayoutAppliedPositionYPx = PublicHigh(
        5,
        "position_y_px"
    );
    internal static readonly BppLogEventDefinition RowLayoutApplied = new(
        BppLogFeatureScope.Settings,
        "settings.row.layout_applied",
        [
            RowLayoutAppliedLayoutMode,
            RowLayoutAppliedRowId,
            RowLayoutAppliedAdditionalIndex,
            RowLayoutAppliedStepPx,
            RowLayoutAppliedPositionXPx,
            RowLayoutAppliedPositionYPx,
        ]
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

    private static BppLogFieldDefinition HashedUntrusted(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.UntrustedText,
            BppLogCorrelationPolicy.Hash,
            BppLogCardinality.High
        );

    private static BppLogFieldDefinition LocalPath(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.LocalPath,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.High
        );
}
