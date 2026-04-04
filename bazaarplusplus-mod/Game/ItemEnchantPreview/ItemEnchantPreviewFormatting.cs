using System;
using System.Text.RegularExpressions;
using BazaarGameShared.Domain.Core.Types;
using TheBazaar.Tooltips;
using TheBazaar.Utilities;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

public static class ItemEnchantPreviewFormatting
{
    private const int PrefixSizePercent = 60;
    private const int EffectSizePercent = 55;
    private const string EnchantmentPrefix = "\u00A0\u00A0· ";

    private static readonly Regex SizeTagRegex = new Regex(
        "<size=(\\d+)%>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    public static TooltipSegment CreateSegment(
        EEnchantmentType enchantmentType,
        string renderedText
    )
    {
        var enchantmentLabel = GetEnchantmentLabel(enchantmentType);
        var colorHex = GetEnchantmentColorHex(enchantmentType);
        var scaledText = ScaleInlineSizes(renderedText, EffectSizePercent / 100f);

        return new TooltipSegment(
            $"<size={PrefixSizePercent}%>{EnchantmentPrefix}<color=#{colorHex}>{enchantmentLabel}</color>: </size><size={EffectSizePercent}%>{scaledText}</size>",
            null,
            null,
            -1
        );
    }

    private static string ScaleInlineSizes(string text, float scale)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return SizeTagRegex.Replace(
            text,
            match =>
            {
                if (!int.TryParse(match.Groups[1].Value, out var size))
                    return match.Value;

                var scaledSize = Math.Max(1, (int)Math.Round(size * scale));
                return $"<size={scaledSize}%>";
            }
        );
    }

    public static string GetEnchantmentLabel(EEnchantmentType enchantmentType)
    {
        try
        {
            return new LocalizableText(enchantmentType.ToString()).GetLocalizedText();
        }
        catch
        {
            return enchantmentType.ToString();
        }
    }

    public static string GetEnchantmentColorHex(EEnchantmentType enchantmentType)
    {
        return enchantmentType switch
        {
            EEnchantmentType.Heavy => "CB9F6E",
            EEnchantmentType.Golden => "FFCD19",
            EEnchantmentType.Icy => "3FC8F7",
            EEnchantmentType.Turbo => "00ECC3",
            EEnchantmentType.Shielded => "F4CF20",
            EEnchantmentType.Restorative => "8EEA31",
            EEnchantmentType.Toxic => "0EBE4F",
            EEnchantmentType.Fiery => "FF9F45",
            EEnchantmentType.Shiny => "98A8FE",
            EEnchantmentType.Deadly => "F5503D",
            EEnchantmentType.Radiant => "98A8FE",
            EEnchantmentType.Obsidian => "9D4A6F",
            _ => "FFFFFF",
        };
    }
}
