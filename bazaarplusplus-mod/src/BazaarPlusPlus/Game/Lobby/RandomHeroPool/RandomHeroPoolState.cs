#nullable enable
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

public sealed class RandomHeroPoolState : SelectionPoolState<RandomHeroPoolState>
{
    public RandomHeroPoolState(
        IEnumerable<string> unlockedHeroIds,
        IEnumerable<string>? selectedHeroIds
    )
        : base(
            unlockedHeroIds,
            selectedHeroIds,
            "Random hero pool requires at least one unlocked hero.",
            nameof(unlockedHeroIds)
        ) { }

    public IReadOnlyList<string> UnlockedHeroIds => AvailableIdsSnapshot;

    public IReadOnlyCollection<string> SelectedHeroIds => SelectedIdsSnapshot;

    public bool IsSelected(string heroId) => IsIdSelected(heroId);

    public RandomHeroPoolState SetSelected(string? heroId, bool isSelected) =>
        WithSelected(heroId, isSelected);

    protected override RandomHeroPoolState CreateNext(
        string[] availableIds,
        IEnumerable<string> selectedIds
    ) => new(availableIds, selectedIds);
}
