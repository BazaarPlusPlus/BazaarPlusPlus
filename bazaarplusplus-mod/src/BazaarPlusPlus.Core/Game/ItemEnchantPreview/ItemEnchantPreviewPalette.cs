#nullable enable
namespace BazaarPlusPlus.Game.ItemEnchantPreview;

/// <summary>
/// The label color of each enchantment in the enchant preview, keyed by the game's enchantment
/// name so that both hosts' enum types map onto one table.
/// </summary>
public static class ItemEnchantPreviewPalette
{
    public static string ColorHex(string enchantmentName) =>
        enchantmentName switch
        {
            "Heavy" => "CB9F6E",
            "Golden" => "FFCD19",
            "Icy" => "3FC8F7",
            "Turbo" => "00ECC3",
            "Shielded" => "F4CF20",
            "Restorative" => "8EEA31",
            "Toxic" => "0EBE4F",
            "Fiery" => "FF9F45",
            "Shiny" => "98A8FE",
            "Deadly" => "F5503D",
            "Radiant" => "98A8FE",
            "Obsidian" => "9D4A6F",
            _ => "FFFFFF",
        };
}
