using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Mobile.Collection;

internal enum CollectionCardCatalogStatus
{
    Building,
    Ready,
    Unavailable,
}

// Catalog builds read game data on the main thread. The host owns warming before first open.
internal interface ICollectionCardCatalog
{
    IReadOnlyList<CollectionCardVm>? Cached { get; }

    // Step at most once per frame within CollectionCardCatalogBudget.WaitingFrameBudgetMs.
    CollectionCardCatalogStatus Advance();

    void RetryIfUnavailable();
}

internal static class CollectionCardCatalogBudget
{
    public const double WaitingFrameBudgetMs = 33;
}
