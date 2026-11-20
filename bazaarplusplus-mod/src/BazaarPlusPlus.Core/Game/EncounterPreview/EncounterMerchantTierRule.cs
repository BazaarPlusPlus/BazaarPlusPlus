#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.EncounterPreview;

public readonly record struct EncounterMerchantTierRule(
    CardTier? FixedTier,
    bool UsesDayDistribution
)
{
    public static EncounterMerchantTierRule DayDistribution { get; } = new(null, true);

    public bool IsSupported => FixedTier.HasValue || UsesDayDistribution;
}
