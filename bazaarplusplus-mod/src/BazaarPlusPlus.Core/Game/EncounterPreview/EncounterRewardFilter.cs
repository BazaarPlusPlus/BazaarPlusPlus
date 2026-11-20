#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.EncounterPreview;

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

    // DownShiftTier still uses the day table. The tooltip shows the distribution before the shift.
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
