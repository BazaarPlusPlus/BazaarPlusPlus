using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Mobile.Collection;

/// <summary>Where a <see cref="ICollectionCardCatalog"/> build stands after a step.</summary>
internal enum CollectionCardCatalogStatus
{
    Building,
    Ready,
    Unavailable,
}

/// <summary>
/// The touch Collection's card catalog (ADR-0025 decision 4): the Android host builds it from its
/// game data, in frame slices on the main thread, and the panel only steps it while it waits.
/// Warming the catalog ahead of the first open stays the host's own.
/// </summary>
internal interface ICollectionCardCatalog
{
    /// <summary>The cards for the current game language, or null while a build is needed.</summary>
    IReadOnlyList<CollectionCardVm>? Cached { get; }

    /// <summary>
    /// Main thread, once a frame while the open panel waits: one step of the build, of at most
    /// <see cref="CollectionCardCatalogBudget.WaitingFrameBudgetMs"/>. <see cref="CollectionCardCatalogStatus.Ready"/> means
    /// <see cref="Cached"/> holds the cards.
    /// </summary>
    CollectionCardCatalogStatus Advance();

    /// <summary>Main thread, on each open: a failed build is tried again.</summary>
    void RetryIfUnavailable();
}

/// <summary>The frame budget of a catalog step while the open panel waits.</summary>
internal static class CollectionCardCatalogBudget
{
    public const double WaitingFrameBudgetMs = 33;
}
