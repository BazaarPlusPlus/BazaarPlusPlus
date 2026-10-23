#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.EventPreview;

public sealed class EncounterRewardFilter
{
    public EncounterRewardFilter(
        CardType cardType,
        int? quantity,
        bool fromAnyHero,
        IReadOnlyList<CardSize> sizes,
        IReadOnlyList<CardTier> tiers,
        IReadOnlyList<CardTag> tags,
        IReadOnlyList<CardHiddenTag> keywords,
        string filterSummary,
        IReadOnlyList<CardTag>? excludedTags = null,
        IReadOnlyList<CardHiddenTag>? excludedKeywords = null,
        bool usesDayTierTable = true,
        bool? usesDayTierDistribution = null
    )
    {
        CardType = cardType;
        Quantity = quantity;
        FromAnyHero = fromAnyHero;
        Sizes = sizes ?? Array.Empty<CardSize>();
        Tiers = tiers ?? Array.Empty<CardTier>();
        Tags = tags ?? Array.Empty<CardTag>();
        Keywords = keywords ?? Array.Empty<CardHiddenTag>();
        FilterSummary = filterSummary ?? string.Empty;
        ExcludedTags = excludedTags ?? Array.Empty<CardTag>();
        ExcludedKeywords = excludedKeywords ?? Array.Empty<CardHiddenTag>();
        UsesDayTierTable = usesDayTierTable;
        UsesDayTierDistribution = usesDayTierDistribution ?? usesDayTierTable;
    }

    public CardType CardType { get; }

    public int? Quantity { get; }

    public bool FromAnyHero { get; }

    public IReadOnlyList<CardSize> Sizes { get; }

    public IReadOnlyList<CardTier> Tiers { get; }

    public IReadOnlyList<CardTag> Tags { get; }

    public IReadOnlyList<CardHiddenTag> Keywords { get; }

    public string FilterSummary { get; }

    public IReadOnlyList<CardTag> ExcludedTags { get; }

    public IReadOnlyList<CardHiddenTag> ExcludedKeywords { get; }

    public bool UsesDayTierTable { get; }

    // DownShiftTier still starts from the daily table, but changes the final tier after
    // the roll. The tooltip intentionally shows only the source distribution, so this
    // remains true for downshift rewards while fixed/ignored/inherited tiers set it false.
    public bool UsesDayTierDistribution { get; }

    public EncounterRewardFilter WithFromAnyHero(bool fromAnyHero)
    {
        if (FromAnyHero == fromAnyHero)
            return this;

        return new EncounterRewardFilter(
            CardType,
            Quantity,
            fromAnyHero,
            Sizes,
            Tiers,
            Tags,
            Keywords,
            FilterSummary,
            ExcludedTags,
            ExcludedKeywords,
            UsesDayTierTable,
            UsesDayTierDistribution
        );
    }
}
