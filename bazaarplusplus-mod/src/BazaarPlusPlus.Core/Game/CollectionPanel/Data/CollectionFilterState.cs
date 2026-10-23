#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Sources;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

public enum CollectionSortPriority
{
    Quality,
    Size,
}

public enum CollectionFacetMatchMode
{
    Any,
    All,
}

// Mutable selection state held by CollectionPanel; pure data. The filter engine reads
// this and produces an ordered visible set.
public sealed class CollectionFilterState
{
    public CollectionTabKind ActiveTab { get; private set; } = CollectionTabKind.Items;

    public CardType ActiveType
    {
        get => ActiveTab.CardType();
        set =>
            ActiveTab =
                value == CardType.Skill ? CollectionTabKind.Skills : CollectionTabKind.Items;
    }
    public CardHero? SelectedHero { get; private set; }

    // Keeps the currently selected concrete hero as the return point when the all-heroes scope
    // is turned back off. The query uses this flag to omit hero scoping entirely.
    public bool AllHeroesSelected { get; private set; }
    public HashSet<CardTier> Tiers { get; } = new();
    public HashSet<CardTag> Tags { get; } = new();
    public HashSet<CardHiddenTag> Keywords { get; } = new();
    public HashSet<CollectionMechanic> Mechanics { get; } = new();
    public CollectionFacetMatchMode TagMatchMode { get; set; } = CollectionFacetMatchMode.Any;
    public CollectionFacetMatchMode KeywordMatchMode { get; set; } = CollectionFacetMatchMode.Any;

    // Item card size (Small/Medium/Large). The active tab profile decides whether this set is
    // shown and applied.
    public HashSet<CardSize> Sizes { get; } = new();
    public string? SelectedSourceKey { get; set; }
    public string SearchQuery { get; set; } = string.Empty;

    // The Day filter is a toggle. Its actual day and ceiling always come from the shared GameData
    // resolver; unavailable data therefore leaves this selected while failing open.
    public bool UseRunDayFilter { get; set; } = true;
    public CollectionSortPriority SortPriority { get; set; } = CollectionSortPriority.Quality;

    public CardHero EffectiveHero => SelectedHero ?? CardHero.Common;

    public bool SelectTab(CollectionTabKind tab)
    {
        if (ActiveTab == tab)
            return false;

        ActiveTab = tab;
        return true;
    }

    public void ApplySelection(CollectionPanelSelectionState selection)
    {
        if (selection == null)
            throw new System.ArgumentNullException(nameof(selection));

        SelectedHero = NormalizeConcreteHero(selection.SelectedHero);
        AllHeroesSelected = false;

        if (selection.SelectedSourceKind == CollectionSourceKind.Trainer)
        {
            ActiveTab = CollectionTabKind.Skills;
            SelectedSourceKey = selection.SelectedSourceKey;
            return;
        }

        ActiveTab = CollectionTabKind.Items;
        SelectedSourceKey = selection.SelectedSourceKey;
    }

    public CardHero ToggleHero(CardHero hero)
    {
        var concreteHero = NormalizeConcreteHero(hero);
        var wasAllHeroesSelected = AllHeroesSelected;
        AllHeroesSelected = false;
        SelectedHero = !wasAllHeroesSelected && SelectedHero == concreteHero ? null : concreteHero;
        return EffectiveHero;
    }

    public bool ToggleAllHeroes()
    {
        AllHeroesSelected = !AllHeroesSelected;
        return AllHeroesSelected;
    }

    public void ToggleSource(CollectionTabKind activeTab, string sourceKey)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
            return;

        ActiveTab = activeTab;

        SelectedSourceKey = string.Equals(
            SelectedSourceKey,
            sourceKey,
            System.StringComparison.Ordinal
        )
            ? null
            : sourceKey;
    }

    public void ResetFacets()
    {
        SelectedHero = null;
        AllHeroesSelected = false;
        Tiers.Clear();
        Tags.Clear();
        Keywords.Clear();
        Mechanics.Clear();
        Sizes.Clear();
        SelectedSourceKey = null;
        SearchQuery = string.Empty;
        TagMatchMode = CollectionFacetMatchMode.Any;
        KeywordMatchMode = CollectionFacetMatchMode.Any;
        UseRunDayFilter = true;
        SortPriority = CollectionSortPriority.Quality;
    }

    public bool ClearSelectedSource()
    {
        if (string.IsNullOrWhiteSpace(SelectedSourceKey))
            return false;
        SelectedSourceKey = null;
        return true;
    }

    public bool PruneSelectedSource(IReadOnlyCollection<string> visibleSourceKeys)
    {
        if (
            !string.IsNullOrWhiteSpace(SelectedSourceKey)
            && !ContainsOrdinal(visibleSourceKeys, SelectedSourceKey!)
        )
        {
            SelectedSourceKey = null;
            return true;
        }

        return false;
    }

    private static bool ContainsOrdinal(IReadOnlyCollection<string> values, string value)
    {
        foreach (var candidate in values)
        {
            if (string.Equals(candidate, value, System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static CardHero? NormalizeConcreteHero(CardHero? hero) =>
        hero.HasValue && hero.Value != CardHero.Common ? hero.Value : null;
}
