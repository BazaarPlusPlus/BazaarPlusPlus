#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

// The panel mutations the view's controls can request: closing the panel, switching the
// active tab, toggling facet selections (hero, tier, size, tag, keyword, source, run-day),
// flipping facet match modes, and choosing the sort priority.
// CollectionPanel owns the filter state and re-renders after each command.
internal interface ICollectionPanelCommands
{
    void Close();
    void ToggleSearch();
    void SetActiveTab(CollectionTabKind tab);
    void ResetFilters();
    void ToggleHero(CardHero hero);
    void ToggleAllHeroes();
    void ToggleTier(CardTier tier);
    void ToggleRunDayFilter();
    void ToggleSize(CardSize size);
    void ToggleTag(CardTag tag);
    void ToggleKeyword(CollectionKeywordFacetOption option);
    void SetKeywordMatchMode(CollectionFacetMatchMode mode);
    void SetTagMatchMode(CollectionFacetMatchMode mode);
    void ToggleSource(string sourceKey);
    void SetSortPriority(CollectionSortPriority priority);
    void SetSearchQuery(string query);
}
