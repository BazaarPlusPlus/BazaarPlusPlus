#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

public sealed class CollectionFilterContext
{
    public IReadOnlyCollection<Guid>? OfferedCardIds { get; init; }

    public bool ApplyHeroFilter { get; init; } = true;

    // The run day's highest dealable tier; null disables the Day gate.
    public CardTier? DayMaximumTier { get; init; }

    // A fixed-tier source offers its tier regardless of the run day, so skip the day ceiling for its resolved pool.
    public bool SuppressDayGate { get; init; }
}
