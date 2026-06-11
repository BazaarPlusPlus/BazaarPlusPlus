#nullable enable
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;

internal sealed class RandomHeroSkinPoolState : SelectionPoolState<RandomHeroSkinPoolState>
{
    public RandomHeroSkinPoolState(
        IEnumerable<string> availableSkinIds,
        IEnumerable<string> selectedSkinIds
    )
        : base(
            availableSkinIds,
            selectedSkinIds,
            "Random hero skin pool requires at least one skin.",
            availableParamName: null
        ) { }

    public IReadOnlyList<string> AvailableSkinIds => AvailableIdsSnapshot;

    public IReadOnlyCollection<string> SelectedSkinIds => SelectedIdsSnapshot;

    public bool IsSelected(string? skinId) => IsIdSelected(skinId);

    public RandomHeroSkinPoolState SetSelected(string? skinId, bool isSelected) =>
        WithSelected(skinId, isSelected);

    protected override RandomHeroSkinPoolState CreateNext(
        string[] availableIds,
        IEnumerable<string> selectedIds
    ) => new(availableIds, selectedIds);
}
