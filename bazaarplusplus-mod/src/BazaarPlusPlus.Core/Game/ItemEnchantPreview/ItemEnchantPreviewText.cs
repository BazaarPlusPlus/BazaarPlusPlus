#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

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
