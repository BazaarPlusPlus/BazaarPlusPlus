#nullable enable
using System.Text;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Interfaces;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Tooltips;
using BazaarPlusPlus.GameInterop.Cards;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.Utilities;

namespace BazaarPlusPlus.Patches.CollectionPanel;

internal static class CollectionTierTooltipPreview
{
    [ThreadStatic]
    private static bool _isRenderingVariant;

    // One card's render runs the active, passive and cooldown postfixes back to back, and each
    // needs the same tier list and tier variants. The slot holds the last card's, keyed by its
    // instance, template and tier; a different card replaces it whole. Reuse is safe: rendering
    // only fills CardTooltipData's own compiled-tooltip memo, and native lock-variant previews
    // render their own card copies, which the registry never holds.
    [ThreadStatic]
    private static VariantSlot? _variantSlot;

    public static bool IsRenderingVariant => _isRenderingVariant;

    public static void MergeActive(
        CardTooltipData tooltipData,
        List<TooltipSegment>? originalSegments
    )
    {
        if (
            originalSegments == null
            || !TryGetPreviewTiers(tooltipData, out var slot)
            || slot.Tiers.Length < 2
        )
            return;

        var tiers = slot.Tiers;

        var variants = new List<IReadOnlyList<string>>(tiers.Length);
        foreach (var tier in tiers)
        {
            if (tier == tooltipData.CardInstance.Tier)
            {
                variants.Add(originalSegments.Select(segment => segment.Text).ToArray());
                continue;
            }

            var tierData = slot.VariantFor(tooltipData, tier);
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
            || !TryGetPreviewTiers(tooltipData, out var slot)
            || slot.Tiers.Length < 2
        )
            return;

        var tiers = slot.Tiers;

        var variants = new List<IReadOnlyList<string>>(tiers.Length);
        foreach (var tier in tiers)
        {
            if (tier == tooltipData.CardInstance.Tier)
            {
                variants.Add(SplitLines(original.Item1));
                continue;
            }

            var tierData = slot.VariantFor(tooltipData, tier);
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
        if (!TryGetPreviewTiers(tooltipData, out var slot) || slot.Tiers.Length < 2)
            return false;

        var tiers = slot.Tiers;

        var result = new List<CollectionTierTooltipText>(tiers.Length);
        foreach (var tier in tiers)
        {
            var tierData =
                tier == tooltipData.CardInstance.Tier
                    ? tooltipData
                    : slot.VariantFor(tooltipData, tier);
            var value = tierData.GetCurrentAttributeValue(attributeType);
            if (!value.HasValue || value.Value <= 0f)
                return false;
            result.Add(
                new CollectionTierTooltipText(
                    TierFacet(tier),
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
                lineVariants.Add(
                    new CollectionTierTooltipText(TierFacet(tiers[tierIndex]), lines[lineIndex])
                );
        }

        return CollectionTierTooltipTextMerger.Merge(lineVariants);
    }

    // The merger colors each variant by its tier facet; a tier without one reads as Bronze.
    private static CardTier TierFacet(ETier tier) =>
        DesktopCardFacets.Tier.TryToFacet(tier, out var facet) ? facet : CardTier.Bronze;

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

    // The registry check runs before the slot is read, so a released card never reuses it.
    private static bool TryGetPreviewTiers(CardTooltipData tooltipData, out VariantSlot slot)
    {
        slot = null!;
        if (
            tooltipData == null
            || !CollectionTierTooltipRegistry.Contains(tooltipData.CardInstance)
            || tooltipData!.CardTemplate is not IHasTierData tierData
        )
            return false;

        var cached = _variantSlot;
        if (cached == null || !cached.Matches(tooltipData))
        {
            var tiers = tierData
                .Tiers.Keys.Where(tier => tier >= tooltipData.CardTemplate.StartingTier)
                .OrderBy(tier => tier)
                .ToArray();
            cached = new VariantSlot(tooltipData, tiers);
            _variantSlot = cached;
        }

        slot = cached;
        return slot.Tiers.Length > 0;
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

    private sealed class VariantSlot(CardTooltipData source, ETier[] tiers)
    {
        private readonly Card _card = source.CardInstance;
        private readonly ITCard _template = source.CardTemplate;
        private readonly ETier _tier = source.CardInstance.Tier;
        private readonly Dictionary<ETier, CardTooltipData> _variants = new();

        public ETier[] Tiers { get; } = tiers;

        public bool Matches(CardTooltipData data) =>
            ReferenceEquals(_card, data.CardInstance)
            && ReferenceEquals(_template, data.CardTemplate)
            && _tier == data.CardInstance.Tier;

        public CardTooltipData VariantFor(CardTooltipData source, ETier tier)
        {
            if (!_variants.TryGetValue(tier, out var variant))
            {
                variant = CreateTierTooltipData(source, tier);
                _variants[tier] = variant;
            }
            return variant;
        }
    }

    private static IReadOnlyList<string> SplitLines(StringBuilder builder) =>
        builder
            .ToString()
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
}
