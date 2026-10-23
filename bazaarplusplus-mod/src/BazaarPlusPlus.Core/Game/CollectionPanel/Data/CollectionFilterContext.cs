#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

public sealed class CollectionFilterContext
{
    public IReadOnlyCollection<Guid>? OfferedCardIds { get; init; }

    public bool ApplyHeroFilter { get; init; } = true;

    // The run day's highest dealable tier; null disables the Day gate.
    public CardTier? DayMaximumTier { get; init; }

    // True when the selected source deals a fixed-tier pool (its offer rule pins a starting
    // tier, e.g. Luxe/Goldie or the tier trainers). Such sources offer their tier regardless
    // of the run day, so the day's tier ceiling must not narrow their resolved pool.
    public bool SuppressDayGate { get; init; }
}
