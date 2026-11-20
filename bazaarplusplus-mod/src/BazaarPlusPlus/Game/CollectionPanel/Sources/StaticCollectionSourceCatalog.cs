#nullable enable
using BazaarPlusPlus.GameInterop.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

internal static class StaticCollectionSourceCatalog
{
    public static EmbeddedCollectionSourceCatalog Instance { get; } =
        new(DesktopCardFacets.IsHeroAvailable);

    public static IReadOnlyList<CollectionSourceEntry> Entries => Instance.Entries;
}
