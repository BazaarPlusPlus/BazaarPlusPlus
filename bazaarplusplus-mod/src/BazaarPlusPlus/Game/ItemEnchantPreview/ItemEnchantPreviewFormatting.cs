#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using TheBazaar.Tooltips;
using TheBazaar.Utilities;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

public static class ItemEnchantPreviewFormatting
{
    private const int PrefixSizePercent = 60;
    private const int EffectSizePercent = 55;
    private const string NativeLineHeight = "<line-height=1.6em>";
    private const string WrappedLineHeight = "<line-height=1.9em>";
    private const string EntryBreakLineHeight = "<line-height=2.1em>";
    private const string LineHeightEnd = "</line-height>";
    private static readonly string EntryBreak = BuildEntryBreak(EntryBreakLineHeight);

    // Localized labels for the active game language; a language switch empties the table. Only
    // successful lookups are kept, so a failing one still reports and falls back every time.
    private static readonly Dictionary<EEnchantmentType, string> EnchantmentLabels = new();
    private static string? _enchantmentLabelLanguage;

    public static TooltipSegment CreateSegment(
        EEnchantmentType enchantmentType,
        string renderedText
    )
    {
        var enchantmentLabel = GetEnchantmentLabel(enchantmentType);
        var colorHex = GetEnchantmentColorHex(enchantmentType);
        var normalizedText = renderedText.Replace(NativeLineHeight, WrappedLineHeight);
        var scaledText = ScaleInlineSizes(normalizedText, EffectSizePercent / 100f);

        return new TooltipSegment(
            $"<size={PrefixSizePercent}%><color=#{colorHex}>{enchantmentLabel}</color>: </size><size={EffectSizePercent}%>{WrappedLineHeight}{scaledText}{LineHeightEnd}</size>",
            null,
            null,
            -1
        );
    }

    public static string BuildSectionText(IReadOnlyList<TooltipSegment> segments)
    {
        if (segments == null || segments.Count == 0)
            return string.Empty;

        var lines = segments
            .Where(segment => !string.IsNullOrWhiteSpace(segment.Text))
            .Select(segment => NormalizeEntryLineEndings(segment.Text));
        return string.Join(EntryBreak, lines);
    }

    private static string BuildEntryBreak(string lineHeight) =>
        $"<size={EffectSizePercent}%>{lineHeight}\n{LineHeightEnd}</size>";

    private static string NormalizeEntryLineEndings(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n');

    internal static string ScaleInlineSizes(string text, float scale) =>
        ItemEnchantPreviewText.ScaleInlineSizes(text, scale);

    public static string GetEnchantmentLabel(EEnchantmentType enchantmentType)
    {
        try
        {
            var language = L.CurrentLanguageCode;
            lock (EnchantmentLabels)
            {
                if (!string.Equals(_enchantmentLabelLanguage, language, StringComparison.Ordinal))
                {
                    EnchantmentLabels.Clear();
                    _enchantmentLabelLanguage = language;
                }
                if (EnchantmentLabels.TryGetValue(enchantmentType, out var cached))
                    return cached;
            }

            var label = new LocalizableText(enchantmentType.ToString()).GetLocalizedText();
            lock (EnchantmentLabels)
            {
                if (string.Equals(_enchantmentLabelLanguage, language, StringComparison.Ordinal))
                    EnchantmentLabels[enchantmentType] = label;
            }
            return label;
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.ItemEnchantPreview,
                    "item_enchant_preview.render.degraded",
                    storm: ["stage", "reason_code"]
                ),
                ex,
                ("stage", ItemEnchantRenderStage.Localization),
                ("reason_code", ItemEnchantLogReasonCode.LocalizationFallback),
                ("enchantment", enchantmentType)
            );
            return enchantmentType.ToString();
        }
    }

    public static string GetEnchantmentColorHex(EEnchantmentType enchantmentType) =>
        ItemEnchantPreviewText.ColorHex(enchantmentType.ToString());
}
