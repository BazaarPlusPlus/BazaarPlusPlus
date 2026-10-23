#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.Supporters;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

public sealed class CollectionPanelViewModel
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public IReadOnlyList<BPPSupporterSample> Supporters { get; set; } =
        Array.Empty<BPPSupporterSample>();
    public int VisibleCount { get; set; }
    public string? StatusMessage { get; set; }
    public bool IsLoading { get; set; }
    public CollectionTabKind ActiveTab { get; set; } = CollectionTabKind.Items;
    public CardType ActiveType { get; set; } = CardType.Item;
    public CollectionTabProfile TabProfile { get; set; } =
        CollectionTabProfile.For(CollectionTabKind.Items);
    public CardHero? SelectedHero { get; set; }
    public bool AllHeroesSelected { get; set; }
    public HashSet<CardTier> SelectedTiers { get; set; } = new();
    public HashSet<CardSize> SelectedSizes { get; set; } = new();
    public HashSet<CardTag> SelectedTags { get; set; } = new();
    public HashSet<CardHiddenTag> SelectedKeywords { get; set; } = new();
    public HashSet<CollectionMechanic> SelectedMechanics { get; set; } = new();
    public CollectionFacetMatchMode TagMatchMode { get; set; } = CollectionFacetMatchMode.Any;
    public CollectionFacetMatchMode KeywordMatchMode { get; set; } = CollectionFacetMatchMode.Any;
    public bool SearchExpanded { get; set; }
    public string SearchQuery { get; set; } = string.Empty;
    public string? SelectedSourceKey { get; set; }
    public HashSet<string> EncounteredMerchantSourceKeys { get; set; } =
        new(StringComparer.Ordinal);
    public bool SourceSelectorEnabled { get; set; } = true;
    public CollectionSortPriority SortPriority { get; set; } = CollectionSortPriority.Quality;

    // Day filter icon: DayFilterValue is the current run day, or null when unavailable;
    // DayFilterActive highlights it when the day participates in filtering.
    public bool DayFilterActive { get; set; }
    public int? DayFilterValue { get; set; }
    public IReadOnlyList<CardHero> AvailableHeroes { get; set; } = Array.Empty<CardHero>();
    public IReadOnlyList<CardTier> AvailableTiers { get; set; } = Array.Empty<CardTier>();
    public IReadOnlyList<CardSize> AvailableSizes { get; set; } = Array.Empty<CardSize>();
    public IReadOnlyList<CardTag> AvailableTags { get; set; } = Array.Empty<CardTag>();
    public IReadOnlyList<CollectionKeywordFacetOption> AvailableKeywordOptions { get; set; } =
        Array.Empty<CollectionKeywordFacetOption>();
    public IReadOnlyList<CollectionSourceOptionViewModel> AvailableSources { get; set; } =
        Array.Empty<CollectionSourceOptionViewModel>();
    public float ContentHeight { get; set; }
}
