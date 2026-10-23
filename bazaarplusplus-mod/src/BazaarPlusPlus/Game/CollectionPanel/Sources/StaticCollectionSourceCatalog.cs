#nullable enable
using BazaarPlusPlus.GameInterop.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

// The desktop host's one source catalog: Core's embedded catalog against this game build's heroes.
internal static class StaticCollectionSourceCatalog
{
    public static EmbeddedCollectionSourceCatalog Instance { get; } =
        new(DesktopCardFacets.IsHeroAvailable);

    public static IReadOnlyList<CollectionSourceEntry> Entries => Instance.Entries;
}
