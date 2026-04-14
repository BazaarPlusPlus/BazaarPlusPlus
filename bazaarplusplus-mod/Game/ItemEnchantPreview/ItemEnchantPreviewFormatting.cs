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
    private const string EnchantmentPrefix = "";

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
            $"<size={PrefixSizePercent}%>{EnchantmentPrefix}<color=#{colorHex}>{enchantmentLabel}</color></size><size={EffectSizePercent}%>: {scaledText}</size>",
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
            EEnchantmentType.Heavy => "CAA77A",
            EEnchantmentType.Golden => "E9C45E",
            EEnchantmentType.Icy => "67C8E6",
            EEnchantmentType.Turbo => "46D6C8",
            EEnchantmentType.Shielded => "D9C05C",
            EEnchantmentType.Restorative => "95D46B",
            EEnchantmentType.Toxic => "63BE67",
            EEnchantmentType.Fiery => "E9A15B",
            EEnchantmentType.Shiny => "A9B2F1",
            EEnchantmentType.Deadly => "E36E5A",
            EEnchantmentType.Radiant => "A9B2F1",
            EEnchantmentType.Obsidian => "AE6D89",
            _ => "FFFFFF",
        };
    }
}
