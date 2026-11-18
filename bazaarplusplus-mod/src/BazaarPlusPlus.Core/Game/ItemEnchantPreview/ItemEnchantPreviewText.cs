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

    // The named styles in the game's Attributes_Style Sheet also set an absolute font size.
    // Copy their label colors so the preview can size its own text.
    public static string ColorHex(string enchantmentName) =>
        enchantmentName switch
        {
            "Deadly" => "F4523C",
            "Ethereal" => "2177E2",
            "Fiery" => "FB943E",
            "Golden" or "Shielded" => "F5D021",
            "Heavy" => "CDA06E",
            "Icy" => "3EC8F8",
            "Mystical" => "FF6DFA",
            "Obsidian" => "B44177",
            "Radiant" => "A5FF82",
            "Restorative" or "Mossy" => "8FE930",
            "Shiny" => "FFFFFF",
            "Tiny" => "F09FFF",
            "Toxic" => "0EBE4E",
            "Turbo" => "00EAC2",
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
