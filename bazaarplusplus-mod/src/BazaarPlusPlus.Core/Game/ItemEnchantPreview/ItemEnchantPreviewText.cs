#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

/// <summary>
/// The host-free parts of the enchant preview's text: each enchantment's label color, keyed by
/// the game's enchantment name so that both hosts' enum types map onto one table, and the
/// scaling of the game's inline size tags into the preview's smaller text.
/// </summary>
public static class ItemEnchantPreviewText
{
    private static readonly Regex SizeTagRegex = new(
        "<size=(\\d+)%>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

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

    /// <summary>Multiplies every <c>&lt;size=N%&gt;</c> tag of rendered game text by a scale.</summary>
    public static string ScaleInlineSizes(string text, float scale)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return SizeTagRegex.Replace(
            text,
            match =>
            {
                if (
                    !int.TryParse(
                        match.Groups[1].Value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var size
                    )
                )
                    return match.Value;

                var scaledSize = Math.Max(1, (int)Math.Round(size * scale));
                return $"<size={scaledSize.ToString(CultureInfo.InvariantCulture)}%>";
            }
        );
    }
}
