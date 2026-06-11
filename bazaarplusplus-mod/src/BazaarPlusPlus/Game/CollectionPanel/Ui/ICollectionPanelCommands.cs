#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

// The panel mutations the view's controls can request: closing the panel, switching the
// active tab, toggling facet selections (hero, tier, size, tag, keyword, source,
// packages-only, run-day), flipping facet match modes, and choosing the sort priority.
// CollectionPanel owns the filter state and re-renders after each command.
internal interface ICollectionPanelCommands
{
    void Close();
    void SetActiveType(ECardType type);
    void ToggleHero(EHero hero);
    void ToggleTier(ETier tier);
    void ToggleRunDayFilter();
    void ToggleSize(ECardSize size);
    void ToggleTag(ECardTag tag);
    void ToggleKeyword(EHiddenTag keyword);
    void ToggleTagMatchMode();
    void ToggleKeywordMatchMode();
    void ToggleSource(string sourceKey);
    void TogglePackagesOnly();
    void SetSortPriority(CollectionSortPriority priority);
}
