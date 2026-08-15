#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.Supporters;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

internal sealed class CollectionPanelViewModel
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public IReadOnlyList<BPPSupporterSample> Supporters { get; set; } =
        Array.Empty<BPPSupporterSample>();
    public int VisibleCount { get; set; }
    public string? StatusMessage { get; set; }
    public bool IsLoading { get; set; }
    public CollectionTabKind ActiveTab { get; set; } = CollectionTabKind.Items;
    public ECardType ActiveType { get; set; } = ECardType.Item;
    public CollectionTabProfile TabProfile { get; set; } =
        CollectionTabProfile.For(CollectionTabKind.Items);
    public bool HeroFilterVisible { get; set; } = true;
    public bool HeroFilterEnabled { get; set; } = true;
    public EHero? SelectedHero { get; set; }
    public bool AllHeroesSelected { get; set; }
    public HashSet<ETier> SelectedTiers { get; set; } = new();
    public HashSet<ECardSize> SelectedSizes { get; set; } = new();
    public HashSet<ECardTag> SelectedTags { get; set; } = new();
    public HashSet<EHiddenTag> SelectedKeywords { get; set; } = new();
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
    public bool DayFilterVisible { get; set; } = true;
    public bool DayFilterEnabled { get; set; } = true;
    public bool DayFilterActive { get; set; }
    public int? DayFilterValue { get; set; }
    public IReadOnlyList<EHero> AvailableHeroes { get; set; } = Array.Empty<EHero>();
    public IReadOnlyList<ETier> AvailableTiers { get; set; } = Array.Empty<ETier>();
    public IReadOnlyList<ECardSize> AvailableSizes { get; set; } = Array.Empty<ECardSize>();
    public IReadOnlyList<ECardTag> AvailableTags { get; set; } = Array.Empty<ECardTag>();
    public IReadOnlyList<CollectionKeywordFacetOption> AvailableKeywordOptions { get; set; } =
        Array.Empty<CollectionKeywordFacetOption>();
    public IReadOnlyList<CollectionSourceOptionViewModel> AvailableSources { get; set; } =
        Array.Empty<CollectionSourceOptionViewModel>();
    public float ContentHeight { get; set; }
}
