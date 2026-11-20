#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

// Desktop hold keys override auto visibility. Android uses its per-tooltip switch.
public static class EnchantPreviewVisibility
{
    public static bool ReadsPedestal(PreviewVisibilityMode mode) =>
        mode == PreviewVisibilityMode.AutoOnPedestalChoice;

    public static bool Shows(PreviewVisibilityMode mode, ChoicePedestalSnapshot? pedestal) =>
        mode switch
        {
            PreviewVisibilityMode.Always => true,
            PreviewVisibilityMode.AutoOnPedestalChoice => pedestal?.IsEnchantChoice == true,
            _ => false,
        };

    public static IReadOnlyList<string>? Restriction(
        PreviewVisibilityMode mode,
        ChoicePedestalSnapshot? pedestal
    ) =>
        ReadsPedestal(mode) && pedestal?.IsEnchantChoice == true
            ? pedestal.Value.EnchantmentTypeNames
            : null;
}
