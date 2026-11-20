#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

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
