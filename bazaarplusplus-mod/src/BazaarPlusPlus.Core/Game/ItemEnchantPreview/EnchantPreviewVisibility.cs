#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

/// <summary>
/// When the enchant preview shows without its hold key, under <c>[EnchantPreview] Mode</c>:
/// <c>Always</c> on every eligible tooltip, <c>AutoOnPedestalChoice</c> only while an enchant
/// pedestal is offered (on the choice screen, or the pedestal encounter itself) and then only
/// for the enchantment(s) that pedestal applies, <c>Off</c> never. Both hosts apply this rule;
/// the desktop's hold keys win over it before it is asked.
/// </summary>
public static class EnchantPreviewVisibility
{
    /// <summary>Whether <paramref name="mode"/> needs the offered pedestal read at all.</summary>
    public static bool ReadsPedestal(PreviewVisibilityMode mode) =>
        mode == PreviewVisibilityMode.AutoOnPedestalChoice;

    public static bool Shows(PreviewVisibilityMode mode, ChoicePedestalSnapshot? pedestal) =>
        mode switch
        {
            PreviewVisibilityMode.Always => true,
            PreviewVisibilityMode.AutoOnPedestalChoice => pedestal?.IsEnchantChoice == true,
            _ => false,
        };

    /// <summary>
    /// The enchantment names the preview keeps, or null for every enchantment the item can take:
    /// a fixed enchant pedestal names its one type, a random one (The Artist) none.
    /// </summary>
    public static IReadOnlyList<string>? Restriction(
        PreviewVisibilityMode mode,
        ChoicePedestalSnapshot? pedestal
    ) =>
        ReadsPedestal(mode) && pedestal?.IsEnchantChoice == true
            ? pedestal.Value.EnchantmentTypeNames
            : null;
}
