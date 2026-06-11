#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.Input;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class TooltipPreviewModePolicy
{
    private const PreviewVisibilityMode DefaultMode = BppConfig.DefaultEnchantPreviewMode;

    internal static TooltipPreviewMode Resolve(
        IBppConfig? config,
        IEncounterStateProbe? encounterState
    )
    {
        return Resolve(
            config,
            encounterState,
            BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview),
            BppHotkeyService.IsHeld(BppHotkeyActionId.HoldEnchantPreview)
        );
    }

    internal static TooltipPreviewMode Resolve(
        IBppConfig? config,
        IEncounterStateProbe? encounterState,
        bool holdUpgrade,
        bool holdEnchant
    )
    {
        var choicePedestal = ShouldReadChoicePedestal(config, holdUpgrade, holdEnchant)
            ? encounterState?.GetChoicePedestal()
            : null;
        return Resolve(config, choicePedestal, holdUpgrade, holdEnchant);
    }

    internal static TooltipPreviewMode Resolve(
        IBppConfig? config,
        ChoicePedestalSnapshot? choicePedestal
    )
    {
        return Resolve(
            config,
            choicePedestal,
            BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview),
            BppHotkeyService.IsHeld(BppHotkeyActionId.HoldEnchantPreview)
        );
    }

    internal static TooltipPreviewMode Resolve(
        IBppConfig? config,
        ChoicePedestalSnapshot? choicePedestal,
        bool holdUpgrade,
        bool holdEnchant
    )
    {
        // Upgrade preview is hold-Shift only — it has no visibility mode of its own.
        if (holdUpgrade)
            return TooltipPreviewMode.Upgrade;
        if (holdEnchant)
            return TooltipPreviewMode.Enchant;

        var enchantMode = config?.EnchantPreviewModeConfig?.Value ?? DefaultMode;
        if (enchantMode == PreviewVisibilityMode.Always)
            return TooltipPreviewMode.Enchant;
        if (enchantMode != PreviewVisibilityMode.AutoOnPedestalChoice)
            return TooltipPreviewMode.Normal;

        return choicePedestal?.Kind == ChoiceScreenPedestalKind.Enchant
            ? TooltipPreviewMode.Enchant
            : TooltipPreviewMode.Normal;
    }

    internal static IReadOnlyList<string>? ResolveEnchantRestriction(
        IBppConfig? config,
        ChoicePedestalSnapshot? choicePedestal
    )
    {
        if (!ShouldReadChoicePedestal(config))
            return null;
        return choicePedestal?.IsEnchantChoice == true
            ? choicePedestal.Value.EnchantmentTypeNames
            : null;
    }

    internal static bool ShouldReadChoicePedestal(IBppConfig? config)
    {
        return ShouldReadChoicePedestal(
            config,
            BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview),
            BppHotkeyService.IsHeld(BppHotkeyActionId.HoldEnchantPreview)
        );
    }

    internal static bool ShouldReadChoicePedestal(
        IBppConfig? config,
        bool holdUpgrade,
        bool holdEnchant
    )
    {
        if (holdUpgrade || holdEnchant)
            return false;
        return (config?.EnchantPreviewModeConfig?.Value ?? DefaultMode)
            == PreviewVisibilityMode.AutoOnPedestalChoice;
    }
}
