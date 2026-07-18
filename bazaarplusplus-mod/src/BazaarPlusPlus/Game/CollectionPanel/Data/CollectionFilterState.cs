#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.Game.Encounters;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal enum CollectionSortPriority
{
    Quality,
    Size,
}

internal enum CollectionFacetMatchMode
{
    Any,
    All,
}

// Mutable selection state held by CollectionPanel; pure data. The filter engine reads
// this and produces an ordered visible set.
internal sealed class CollectionFilterState
{
    public CollectionTabKind ActiveTab { get; private set; } = CollectionTabKind.Items;

    public ECardType ActiveType
    {
        get => ActiveTab.CardType();
        set =>
            ActiveTab =
                value == ECardType.Skill ? CollectionTabKind.Skills : CollectionTabKind.Items;
    }
    public HashSet<EHero> Heroes { get; } = new();
    public HashSet<ETier> Tiers { get; } = new();
    public HashSet<ECardTag> Tags { get; } = new();
    public HashSet<EHiddenTag> Keywords { get; } = new();
    public CollectionFacetMatchMode TagMatchMode { get; set; } = CollectionFacetMatchMode.Any;
    public CollectionFacetMatchMode KeywordMatchMode { get; set; } = CollectionFacetMatchMode.Any;

    // Item card size (Small/Medium/Large). The active tab profile decides whether this set is
    // shown and applied.
    public HashSet<ECardSize> Sizes { get; } = new();
    public string? SelectedSourceKey { get; set; }
    public string SearchQuery { get; set; } = string.Empty;

    // User-selected run "Day" filter; null means no day filtering. Starts enabled so the panel
    // binds it to Data.Run.Day on open; outside a run, OutOfRunDay keeps the toggle visibly active
    // without narrowing the catalog.
    public int? SelectedRunDay { get; set; } = DayTierSchedule.OutOfRunDay;
    public CollectionSortPriority SortPriority { get; set; } = CollectionSortPriority.Quality;

    public EHero? SelectedHero
    {
        get
        {
            if (Heroes.Count != 1)
                return null;
            foreach (var hero in Heroes)
                return hero;
            return null;
        }
    }

    public string? GetSelectedSourceKey(ECardType activeType) =>
        activeType == ActiveType ? SelectedSourceKey : null;

    public bool SelectTab(CollectionTabKind tab)
    {
        if (ActiveTab == tab)
            return false;

        ActiveTab = tab;
        return true;
    }

    public bool SelectActiveType(ECardType activeType)
    {
        return SelectTab(
            activeType == ECardType.Skill ? CollectionTabKind.Skills : CollectionTabKind.Items
        );
    }

    public void ApplySelection(CollectionPanelSelectionState selection)
    {
        if (selection == null)
            throw new System.ArgumentNullException(nameof(selection));

        Heroes.Clear();
        Heroes.Add(selection.SelectedHero ?? CollectionPanelSelectionState.DefaultHero);

        if (selection.SelectedSourceKind == CollectionSourceKind.Trainer)
        {
            ActiveTab = CollectionTabKind.Skills;
            SelectedSourceKey = selection.SelectedSourceKey;
            return;
        }

        ActiveTab = CollectionTabKind.Items;
        SelectedSourceKey = selection.SelectedSourceKey;
    }

    public CollectionPanelSelectionState ToSelectionState()
    {
        return new CollectionPanelSelectionState(
            SelectedHero,
            SelectedSourceKey,
            CollectionTabProfile.For(ActiveTab).SourceKind ?? CollectionSourceKind.Merchant
        );
    }

    public void ToggleHero(EHero hero)
    {
        if (Heroes.Count == 1 && Heroes.Contains(hero))
        {
            Heroes.Clear();
            return;
        }

        Heroes.Clear();
        Heroes.Add(hero);
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
}
