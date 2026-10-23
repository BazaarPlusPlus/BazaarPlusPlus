#nullable enable
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.GameInterop.DayTiers;

namespace BazaarPlusPlus.Game.CollectionPanel;

// The desktop GameData day-tier resolver behind the Core Collection port.
internal sealed class CollectionDayTierSource(IGameDataDayTierResolver resolver)
    : ICollectionDayTierSource
{
    private readonly IGameDataDayTierResolver _resolver =
        resolver ?? throw new ArgumentNullException(nameof(resolver));

    public CollectionDayTiers Resolve()
    {
        var resolution = _resolver.Resolve();
        return new CollectionDayTiers(resolution.Day, resolution.MaximumTier);
    }
}
