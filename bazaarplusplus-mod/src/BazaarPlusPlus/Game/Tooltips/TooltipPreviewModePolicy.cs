#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.ItemEnchantPreview;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class TooltipPreviewModePolicy
{
    private const PreviewVisibilityMode DefaultMode = BppSettings.DefaultEnchantPreviewMode;

    internal static TooltipPreviewMode Resolve(
        BppSettings? config,
        IEncounterStateProbe? encounterState
    )
    {
        return Resolve(
            config,
            encounterState,
            BppHotkeyService.IsActive(BppHotkeyActionId.HoldUpgradePreview),
            BppHotkeyService.IsHeld(BppHotkeyActionId.HoldEnchantPreview)
        );
    }

    internal static TooltipPreviewMode Resolve(
        BppSettings? config,
        IEncounterStateProbe? encounterState,
        bool holdUpgrade,
        bool holdEnchant
    )
    {
        var choicePedestal = ShouldReadChoicePedestal(config, holdUpgrade, holdEnchant)
            ? TooltipEncounterProbeReader.ReadChoice(encounterState)
            : null;
        return Resolve(config, choicePedestal, holdUpgrade, holdEnchant);
    }

    internal static TooltipPreviewMode Resolve(
        BppSettings? config,
        ChoicePedestalSnapshot? choicePedestal
    )
    {
        return Resolve(
            config,
            choicePedestal,
            BppHotkeyService.IsActive(BppHotkeyActionId.HoldUpgradePreview),
            BppHotkeyService.IsHeld(BppHotkeyActionId.HoldEnchantPreview)
        );
    }

    internal static TooltipPreviewMode Resolve(
        BppSettings? config,
        ChoicePedestalSnapshot? choicePedestal,
        bool holdUpgrade,
        bool holdEnchant
    )
    {
        // Upgrade preview remains key-only; the shared key can be configured as hold or toggle.
        if (holdUpgrade)
            return TooltipPreviewMode.Upgrade;
        if (holdEnchant)
            return TooltipPreviewMode.Enchant;

        return EnchantPreviewVisibility.Shows(
            config?.EnchantPreviewModeConfig.Value ?? DefaultMode,
            choicePedestal
        )
            ? TooltipPreviewMode.Enchant
            : TooltipPreviewMode.Normal;
    }

    internal static IReadOnlyList<string>? ResolveEnchantRestriction(
        BppSettings? config,
        ChoicePedestalSnapshot? choicePedestal
    )
    {
        if (!ShouldReadChoicePedestal(config))
            return null;
        return EnchantPreviewVisibility.Restriction(
            config?.EnchantPreviewModeConfig.Value ?? DefaultMode,
            choicePedestal
        );
    }

    internal static bool ShouldReadChoicePedestal(BppSettings? config)
    {
        return ShouldReadChoicePedestal(
            config,
            BppHotkeyService.IsActive(BppHotkeyActionId.HoldUpgradePreview),
            BppHotkeyService.IsHeld(BppHotkeyActionId.HoldEnchantPreview)
        );
    }

    internal static bool ShouldReadChoicePedestal(
        BppSettings? config,
        bool holdUpgrade,
        bool holdEnchant
    )
    {
        if (holdUpgrade || holdEnchant)
            return false;
        return EnchantPreviewVisibility.ReadsPedestal(
            config?.EnchantPreviewModeConfig.Value ?? DefaultMode
        );
    }
}
