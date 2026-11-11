#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.EncounterPreview;

/// <summary>
/// The quality a merchant sells, from its spawn behaviours: one inclusive tier pins it
/// (<see cref="FixedTier"/>), a merchant that ignores the day's tier table without pinning one
/// leaves nothing to show, and every other merchant sells by the day's tier distribution. Both
/// hosts read it off the card's JSON (<see cref="EncounterChoicePlanParser.TryParseMerchantTierRule"/>)
/// into the merchant's <see cref="EncounterPreviewEventPlan.Merchant"/>.
/// </summary>
public readonly record struct EncounterMerchantTierRule(
    CardTier? FixedTier,
    bool UsesDayDistribution
)
{
    public static EncounterMerchantTierRule DayDistribution { get; } = new(null, true);

    /// <summary>
    /// Whether a quality line can be drawn: a pinned tier with no facet, or no tier table, has
    /// none.
    /// </summary>
    public bool IsSupported => FixedTier.HasValue || UsesDayDistribution;
}
