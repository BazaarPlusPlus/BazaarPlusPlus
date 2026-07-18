#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.EventPreview;

internal sealed class EncounterRewardFilter
{
    public EncounterRewardFilter(
        ECardType cardType,
        int? quantity,
        bool fromAnyHero,
        IReadOnlyList<ECardSize> sizes,
        IReadOnlyList<ETier> tiers,
        IReadOnlyList<ECardTag> tags,
        IReadOnlyList<EHiddenTag> keywords,
        string filterSummary,
        IReadOnlyList<ECardTag>? excludedTags = null,
        IReadOnlyList<EHiddenTag>? excludedKeywords = null,
        bool usesDayTierTable = true,
        bool? usesDayTierDistribution = null
    )
    {
        CardType = cardType;
        Quantity = quantity;
        FromAnyHero = fromAnyHero;
        Sizes = sizes ?? Array.Empty<ECardSize>();
        Tiers = tiers ?? Array.Empty<ETier>();
        Tags = tags ?? Array.Empty<ECardTag>();
        Keywords = keywords ?? Array.Empty<EHiddenTag>();
        FilterSummary = filterSummary ?? string.Empty;
        ExcludedTags = excludedTags ?? Array.Empty<ECardTag>();
        ExcludedKeywords = excludedKeywords ?? Array.Empty<EHiddenTag>();
        UsesDayTierTable = usesDayTierTable;
        UsesDayTierDistribution = usesDayTierDistribution ?? usesDayTierTable;
    }

    public ECardType CardType { get; }

    public int? Quantity { get; }

    public bool FromAnyHero { get; }

    public IReadOnlyList<ECardSize> Sizes { get; }

    public IReadOnlyList<ETier> Tiers { get; }

    public IReadOnlyList<ECardTag> Tags { get; }

    public IReadOnlyList<EHiddenTag> Keywords { get; }

    public string FilterSummary { get; }

    public IReadOnlyList<ECardTag> ExcludedTags { get; }

    public IReadOnlyList<EHiddenTag> ExcludedKeywords { get; }

    public bool UsesDayTierTable { get; }

    // DownShiftTier still starts from the daily table, but changes the final tier after
    // the roll. The tooltip intentionally shows only the source distribution, so this
    // remains true for downshift rewards while fixed/ignored/inherited tiers set it false.
    public bool UsesDayTierDistribution { get; }

    public bool HasTierGateOverride => Tiers.Count > 0;

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
