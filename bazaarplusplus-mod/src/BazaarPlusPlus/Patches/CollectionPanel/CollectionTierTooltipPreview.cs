#nullable enable
using System.Text;
using BazaarGameShared.Domain.Cards.Interfaces;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Tooltips;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.Utilities;

namespace BazaarPlusPlus.Patches.CollectionPanel;

internal static class CollectionTierTooltipPreview
{
    [ThreadStatic]
    private static bool _isRenderingVariant;

    public static bool IsRenderingVariant => _isRenderingVariant;

    public static void MergeActive(
        CardTooltipData tooltipData,
        List<TooltipSegment>? originalSegments
    )
    {
        if (
            originalSegments == null
            || !TryGetPreviewTiers(tooltipData, out var tiers)
            || tiers.Count < 2
        )
            return;

        var variants = new List<IReadOnlyList<string>>(tiers.Count);
        foreach (var tier in tiers)
        {
            if (tier == tooltipData.CardInstance.Tier)
            {
                variants.Add(originalSegments.Select(segment => segment.Text).ToArray());
                continue;
            }

            var tierData = CreateTierTooltipData(tooltipData, tier);
            var tierSegments = RenderVariant(tierData.GetActiveAbilityTooltipBlock);
            try
            {
                variants.Add(tierSegments.Select(segment => segment.Text).ToArray());
            }
            finally
            {
                tierSegments.ReturnTooltipSegments();
            }
        }

        var originalCount = originalSegments.Count;
        var maximumCount = variants.Max(lines => lines.Count);
        for (var lineIndex = 0; lineIndex < maximumCount; lineIndex++)
        {
            var merged = MergeLine(tiers, variants, lineIndex);
            if (string.IsNullOrWhiteSpace(merged))
                continue;

            if (lineIndex < originalCount)
            {
                var original = originalSegments[lineIndex];
                originalSegments[lineIndex] = new TooltipSegment(
                    merged,
                    original.Attribute,
                    original.Value,
                    original.AttributeCharacterIndex,
                    original.NestedSegements
                );
            }
            else
            {
                originalSegments.Add(new TooltipSegment(merged, null, null, -1));
            }
        }
    }

    public static void MergePassive(
        CardTooltipData tooltipData,
        ref ValueTuple<StringBuilder, TooltipSegment?> original
    )
    {
        if (
            original.Item1 == null
            || !TryGetPreviewTiers(tooltipData, out var tiers)
            || tiers.Count < 2
        )
            return;

        var variants = new List<IReadOnlyList<string>>(tiers.Count);
        foreach (var tier in tiers)
        {
            if (tier == tooltipData.CardInstance.Tier)
            {
                variants.Add(SplitLines(original.Item1));
                continue;
            }

            var tierData = CreateTierTooltipData(tooltipData, tier);
            var tierResult = RenderVariant(() =>
                tierData.GetPassiveTooltipBlock(tierData.CardInstance)
            );
            try
            {
                variants.Add(SplitLines(tierResult.Item1));
            }
            finally
            {
                tierResult.Item1.ReturnStringBuilder();
            }
        }

        original.Item1.Clear();
        var maximumCount = variants.Max(lines => lines.Count);
        for (var lineIndex = 0; lineIndex < maximumCount; lineIndex++)
        {
            var merged = MergeLine(tiers, variants, lineIndex);
            if (!string.IsNullOrWhiteSpace(merged))
                original.Item1.AppendLine(merged);
        }
    }

    public static bool TryGetTierAttributeValues(
        CardTooltipData tooltipData,
        ECardAttributeType attributeType,
        string numberFormat,
        out IReadOnlyList<CollectionTierTooltipText> values
    )
    {
        values = Array.Empty<CollectionTierTooltipText>();
        if (!TryGetPreviewTiers(tooltipData, out var tiers) || tiers.Count < 2)
            return false;

        var result = new List<CollectionTierTooltipText>(tiers.Count);
        foreach (var tier in tiers)
        {
            var tierData =
                tier == tooltipData.CardInstance.Tier
                    ? tooltipData
                    : CreateTierTooltipData(tooltipData, tier);
            var value = tierData.GetCurrentAttributeValue(attributeType);
            if (!value.HasValue || value.Value <= 0f)
                return false;
            result.Add(
                new CollectionTierTooltipText(
                    tier,
                    value.Value.ToString(
                        numberFormat,
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                )
            );
        }

        values = result;
        return result.Select(value => value.Text).Distinct(StringComparer.Ordinal).Skip(1).Any();
    }

    private static string MergeLine(
        IReadOnlyList<ETier> tiers,
        IReadOnlyList<IReadOnlyList<string>> variants,
        int lineIndex
    )
    {
        var lineVariants = new List<CollectionTierTooltipText>(tiers.Count);
        for (var tierIndex = 0; tierIndex < tiers.Count; tierIndex++)
        {
            var lines = variants[tierIndex];
            if (lineIndex < lines.Count && !string.IsNullOrWhiteSpace(lines[lineIndex]))
                lineVariants.Add(new CollectionTierTooltipText(tiers[tierIndex], lines[lineIndex]));
        }

        return CollectionTierTooltipTextMerger.Merge(lineVariants);
    }

    private static CardTooltipData CreateTierTooltipData(CardTooltipData source, ETier tier)
    {
        var previewCard = source.CardInstance.CreateCopy();
        previewCard.Tier = tier;
        previewCard.Template ??= source.CardTemplate;
        previewCard.Attributes = TheBazaar.CardExtensions.BuildAttributeDictionaryForTier(
            source.CardInstance,
            source.CardTemplate,
            tier
        );
        return new CardTooltipData(previewCard, source.CardTemplate);
    }

    private static bool TryGetPreviewTiers(
        CardTooltipData tooltipData,
        out IReadOnlyList<ETier> tiers
    )
    {
        tiers = Array.Empty<ETier>();
        if (
            tooltipData == null
            || !CollectionTierTooltipRegistry.Contains(tooltipData.CardInstance)
            || tooltipData!.CardTemplate is not IHasTierData tierData
        )
            return false;

        tiers = tierData
            .Tiers.Keys.Where(tier => tier >= tooltipData.CardTemplate.StartingTier)
            .OrderBy(tier => tier)
            .ToArray();
        return tiers.Count > 0;
    }

    private static T RenderVariant<T>(Func<T> render)
    {
        var previous = _isRenderingVariant;
        _isRenderingVariant = true;
        try
        {
            return render();
        }
        finally
        {
            _isRenderingVariant = previous;
        }
    }

    private static IReadOnlyList<string> SplitLines(StringBuilder builder) =>
        builder
            .ToString()
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
}
