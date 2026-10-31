using BazaarPlusPlus.Game.CollectionPanel.Tooltips;

namespace BazaarPlusPlus.Mobile.Previews;

/// <summary>
/// A host's read of the game's card tooltip for <see cref="TouchEnchantPreview{TTooltip}"/>
/// (ADR-0025 decision 5). <typeparamref name="TTooltip"/> is the host's tooltip data, which the
/// Mobile UI only hands back; enchantments are named as the game's enum names them. Main thread.
/// </summary>
internal interface IEnchantPreviewHost<in TTooltip>
    where TTooltip : class
{
    /// <summary>
    /// The enchantments the tooltip's item can take besides its own, in the game's order; none
    /// during a fight or a replay, for a card that is no item, or for an item that is neither on a
    /// board, in a stash nor unowned (a shop's), as the desktop's
    /// <c>ItemEnchantPreviewEligibility</c>.
    /// </summary>
    IReadOnlyList<string> Candidates(TTooltip tooltip);

    /// <summary>Whether the tooltip's item shows its upgrade preview, which wins over the enchantments.</summary>
    bool HoldsUpgrade(TTooltip tooltip);

    /// <summary>
    /// <paramref name="enchantment"/>'s effect on the tooltip's item at its own tier, as the game
    /// renders it, or null when it cannot be rendered.
    /// </summary>
    string? Effect(TTooltip tooltip, string enchantment);

    /// <summary>
    /// <paramref name="enchantment"/>'s effect on the tooltip's item at its tier and each higher
    /// one, as the game renders it; a tier it cannot render is left out.
    /// </summary>
    IReadOnlyList<CollectionTierTooltipText> TierEffects(TTooltip tooltip, string enchantment);

    /// <summary>The game's localized name for <paramref name="enchantment"/>, or the name itself.</summary>
    string Label(string enchantment);
}
