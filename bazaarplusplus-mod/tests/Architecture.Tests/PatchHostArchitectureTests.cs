#nullable enable
using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

public sealed class PatchHostArchitectureTests
{
    private const string Patches = "BazaarPlusPlus.Patches";
    private const string Game = "BazaarPlusPlus.Game";
    private const string HostAssembly = "BazaarPlusPlus";

    // A static reach is a static call, a static field access or a method pointer. An instance
    // call on a service that BppPatchHost handed out compiles to callvirt and is not one.
    private static readonly string[] StaticReach = ["call", "ldsfld", "ldsflda", "stsfld", "ldftn"];

    // The reaches that existed when the rule was written (#994). Remove a line when its patch
    // goes through BppPatchHost; never add one.
    private static readonly string[] Debt =
    [
        "Patches.CollectionPanel.CollectionCardPreviewDestroyPatch -> Game.CollectionPanel.Grid.CollectionNativeCardPreviewOwner",
        "Patches.CollectionPanel.CollectionItemLoadArtPatch -> Game.CollectionPanel.Grid.CollectionCardCacheHost",
        "Patches.CollectionPanel.CollectionLockVariantPanelPatch -> Game.CollectionPanel.Tooltips.CollectionTierTooltipRegistry",
        "Patches.CollectionPanel.CollectionTierTooltipPreview -> Game.CollectionPanel.Tooltips.CollectionTierTooltipRegistry",
        "Patches.Combat.CombatReplayBoardSpawnReadinessPatch -> Game.CombatReplay.Bootstrap.ReplayItemPresentationReadiness",
        "Patches.Combat.CombatReplayClockInjectedEncounterPickerPatch -> Game.CombatReplay.CombatReplayRuntime",
        "Patches.Combat.CombatReplayEncounterPickerMapShowPatch -> Game.CombatReplay.CombatReplayRuntime",
        "Patches.Combat.CombatReplayInjectedEncounterPickerMapShowPatch -> Game.CombatReplay.CombatReplayRuntime",
        "Patches.Combat.CombatReplayItemPresentationReadinessPatch -> Game.CombatReplay.Bootstrap.ReplayItemPresentationReadiness",
        "Patches.Combat.CombatReplayPatchGuard -> Game.CombatReplay.CombatReplayRuntime",
        "Patches.Combat.CombatReplayStateExitPatch -> Game.CombatReplay.CombatReplayRuntime",
        "Patches.Combat.CombatSimPatch -> Game.CombatReplay.CombatReplayRuntime",
        "Patches.Combat.CombatSpeedPatch -> Game.CombatStatusBar.CombatStatusBar",
        "Patches.Combat.CurrentReplayRecordingButtonPatch -> Game.CombatReplay.CombatReplayRuntime",
        "Patches.Combat.CurrentReplayRecordingButtonPatch -> Game.CombatReplay.CurrentReplayRecordingButtonController",
        "Patches.Combat.CurrentReplayRecordingDockButtonPatch -> Game.CombatReplay.CurrentReplayRecordingButtonController",
        "Patches.LegendaryPosition.EndOfRunSetLeaderboardPositionPatch -> Game.LegendaryPosition.LegendaryPositionDisplayFormatter",
        "Patches.LegendaryPosition.HeroBannerSetLeaderboardPositionPatch -> Game.LegendaryPosition.LegendaryPositionDisplayFormatter",
        "Patches.Lobby.MainMenuVersionLabelBuildPatch -> Game.Lobby.MainMenuVersionLabelUpdater",
        "Patches.Settings.BppNativeSettingsOnEnablePatch -> Game.Settings.BppNativeSettingsSectionController",
        "Patches.Settings.BppNativeSettingsScrollSpyAwakePatch -> Game.Settings.BppNativeSettingsSectionController",
        "Patches.Settings.CollectionPanelDockButtonAwakePatch -> Game.Settings.BppPanelDockButtonsController",
        "Patches.Settings.CollectionPanelDockButtonAwakePatch -> Game.Settings.BppSettingsDockPlacement",
        "Patches.Settings.CollectionPanelDockButtonFightMenuPatch -> Game.Settings.BppPanelDockButtonsController",
        "Patches.Settings.CollectionPanelDockButtonFightMenuPatch -> Game.Settings.BppSettingsDockPlacement",
        "Patches.Settings.OptionsDialogLanguageRefreshPatch -> Game.HistoryPanel.HistoryPanel",
        "Patches.Settings.OptionsDialogLanguageRefreshPatch -> Game.Settings.BppNativeSettingsSectionController",
        "Patches.Tooltips.AggregateItemMissingTypesTooltipPatch -> Game.DetailedPreview.DetailedPreviewGate",
        "Patches.Tooltips.AggregateItemMissingTypesTooltipPatch -> Game.Tooltips.AggregateItemMissingTypesText",
        "Patches.Tooltips.AggregateItemMissingTypesTooltipPatch -> Game.Tooltips.AggregateItemTypeSource",
        "Patches.Tooltips.AggregateItemMissingTypesTooltipPatch -> Game.Tooltips.AggregateItemTypeSourceResolver",
        "Patches.Tooltips.AggregateItemMissingTypesTooltipPatch -> Game.Tooltips.BppTooltipText",
        "Patches.Tooltips.BilingualItemNamePatch -> Game.BilingualItemNames.BilingualItemNamePresentation",
        "Patches.Tooltips.BilingualItemNamePatch -> Game.BilingualItemNames.BilingualItemNameSubtitle",
        "Patches.Tooltips.BilingualItemNamePatch -> Game.BilingualItemNames.BilingualNameCardEligibility",
        "Patches.Tooltips.BilingualItemNameResetPatch -> Game.BilingualItemNames.BilingualItemNameSubtitle",
        "Patches.Tooltips.BppTooltipSectionRenderPatch -> Game.ItemEnchantPreview.ItemEnchantPreviewFormatting",
        "Patches.Tooltips.BppTooltipSectionRenderPatch -> Game.ItemEnchantPreview.ItemEnchantPreviewService",
        "Patches.Tooltips.BppTooltipSectionRenderPatch -> Game.Tooltips.TooltipPreviewModePolicy",
        "Patches.Tooltips.QuestRewardPreviewTooltipPatch -> Game.DetailedPreview.DetailedPreviewGate",
        "Patches.Tooltips.TooltipLayerOverride -> Game.ItemEnchantPreview.ItemEnchantPreviewTooltipLayerPolicy",
        "Patches.Tooltips.UpgradePreviewAuraValueContextPatch -> Game.Tooltips.UpgradePreviewValueRegistry",
        "Patches.Tooltips.UpgradePreviewCooldownPatch -> Game.Tooltips.UpgradePreviewValueRegistry",
        "Patches.Tooltips.UpgradePreviewTooltipPatch -> Game.Tooltips.UpgradeTooltipScheduler",
        "Patches.Tooltips.UpgradePreviewTooltipRenderScopePatch -> Game.Tooltips.UpgradePreviewValueRegistry",
        "Patches.Tooltips.UpgradePreviewValuePatch -> Game.Tooltips.UpgradePreviewValueRegistry",
    ];

    [Fact]
    public void Patches_reach_feature_code_only_through_the_patch_host()
    {
        Holds(
            "A patch reaches Game/ feature code through BppPatchHost, not through a static member. "
                + "'new' is a reach to route through the host; 'stale' is a Debt line to delete.",
            build =>
            {
                var host = build.Type(Patches + ".BppPatchHost").FullName;
                var features = build.Type(Patches + ".BppPatchFeatures").FullName;
                var actual = build
                    .TypesIn(Patches)
                    .Where(type => Outer(type.FullName) != host && Outer(type.FullName) != features)
                    .SelectMany(type =>
                        type.Instructions.Where(op =>
                                op.TargetMember != null
                                && StaticReach.Contains(op.OpCode)
                                && op.TargetType is { } target
                                && target.Assembly == HostAssembly
                                && CompiledBuild.IsInNamespace(target.Namespace, Game)
                            )
                            .Select(op =>
                                $"{Short(Outer(type.FullName))} -> {Short(Outer(op.TargetType!.Value.FullName))}"
                            )
                    )
                    .ToHashSet(StringComparer.Ordinal);
                return actual
                    .Except(Debt)
                    .Select(reach => "new: " + reach)
                    .Concat(Debt.Except(actual).Select(reach => "stale: " + reach));
            }
        );
    }

    private static string Outer(string fullName) => fullName.Split('+')[0];

    private static string Short(string fullName) => fullName["BazaarPlusPlus.".Length..];
}
