#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Sources;

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
    public ECardType ActiveType { get; set; } = ECardType.Item;
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
    public bool PackagesOnly { get; set; }

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

    public bool SelectActiveType(ECardType activeType)
    {
        if (ActiveType == activeType && !PackagesOnly)
            return false;

        ActiveType = activeType;
        PackagesOnly = false;
        return true;
    }

    public bool SelectPackagesOnly()
    {
        if (ActiveType == ECardType.Item && PackagesOnly)
            return false;

        ActiveType = ECardType.Item;
        PackagesOnly = true;
        return true;
    }

    public void ApplySelection(CollectionPanelSelectionState selection)
    {
        if (selection == null)
            throw new System.ArgumentNullException(nameof(selection));

        Heroes.Clear();
        Heroes.Add(selection.SelectedHero ?? CollectionPanelSelectionState.DefaultHero);

        if (selection.SelectedSourceKind == CollectionSourceKind.Trainer)
        {
            ActiveType = ECardType.Skill;
            PackagesOnly = false;
            SelectedSourceKey = selection.SelectedSourceKey;
            return;
        }

        ActiveType = ECardType.Item;
        SelectedSourceKey = selection.SelectedSourceKey;
    }

    public CollectionPanelSelectionState ToSelectionState()
    {
        return new CollectionPanelSelectionState(
            SelectedHero,
            SelectedSourceKey,
            CollectionTabProfile.For(ActiveType).SourceKind
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

    public void ToggleSource(ECardType activeType, string sourceKey)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
            return;

        ActiveType = activeType;
        if (ActiveType == ECardType.Skill)
            PackagesOnly = false;

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
