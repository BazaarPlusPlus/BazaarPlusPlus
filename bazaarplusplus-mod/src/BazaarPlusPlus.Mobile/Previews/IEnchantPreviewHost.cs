using BazaarPlusPlus.Game.CollectionPanel.Tooltips;

namespace BazaarPlusPlus.Mobile.Previews;

// Main thread only. Enchantment keys are the game's enum member names.
internal interface IEnchantPreviewHost<in TTooltip>
    where TTooltip : class
{
    // Return eligible enchantments in game order for items on a board, in a stash or unowned.
    IReadOnlyList<string> Candidates(TTooltip tooltip);

    bool HoldsUpgrade(TTooltip tooltip);

    string? Effect(TTooltip tooltip, string enchantment);

    IReadOnlyList<CollectionTierTooltipText> TierEffects(TTooltip tooltip, string enchantment);

    string Label(string enchantment);
}
