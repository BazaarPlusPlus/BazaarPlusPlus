#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Data;

/// <summary>
/// The Mono host's <see cref="ICollectionCardCatalog"/> for the touch Collection (ADR-0025
/// decision 4), over the desktop's <see cref="CollectionCatalog"/>: the open panel steps it at the
/// shared waiting budget, and <see cref="Warm"/> builds it in the desktop panel's smaller closed
/// slices.
/// </summary>
internal sealed class DesktopCollectionCardCatalog(CollectionCatalog catalog)
    : ICollectionCardCatalog
{
    private const float WarmFrameBudgetMs = 1f;

    private readonly CollectionCatalog _catalog =
        catalog ?? throw new ArgumentNullException(nameof(catalog));

    public IReadOnlyList<CollectionCardVm>? Cached =>
        _catalog.TryGetCached(out var built) ? built.Cards : null;

    public CollectionCardCatalogStatus Advance() =>
        _catalog.AdvanceBuild((float)CollectionCardCatalogBudget.WaitingFrameBudgetMs, out _) switch
        {
            CollectionCatalogWarmupStatus.Ready => CollectionCardCatalogStatus.Ready,
            CollectionCatalogWarmupStatus.Unavailable => CollectionCardCatalogStatus.Unavailable,
            _ => CollectionCardCatalogStatus.Building,
        };

    // The desktop catalog builds again by itself once the game swaps its static data; the same
    // data would fail the same way.
    public void RetryIfUnavailable() { }

    /// <summary>Main thread, each frame the panel is closed: a small slice of the build.</summary>
    public void Warm() => _catalog.AdvanceBuild(WarmFrameBudgetMs, out _);

    /// <summary>The Chinese mode changed: the labels are built again on the next open.</summary>
    public void InvalidateForLocale() =>
        _catalog.InvalidateCache(CollectionPanelLogReasonCode.LocaleChange);
}
