#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;

internal sealed class RandomHeroSkinPoolState
{
    private readonly string[] _availableSkinIds;
    private readonly HashSet<string> _availableSkinIdSet;
    private readonly HashSet<string> _selectedSkinIds;

    public RandomHeroSkinPoolState(
        IEnumerable<string> availableSkinIds,
        IEnumerable<string> selectedSkinIds
    )
    {
        if (availableSkinIds == null)
            throw new ArgumentNullException(nameof(availableSkinIds));
        if (selectedSkinIds == null)
            throw new ArgumentNullException(nameof(selectedSkinIds));

        _availableSkinIds = availableSkinIds
            .Where(IsValidSkinId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (_availableSkinIds.Length == 0)
            throw new ArgumentException("Random hero skin pool requires at least one skin.");

        _availableSkinIdSet = new HashSet<string>(_availableSkinIds, StringComparer.Ordinal);

        var filteredSelected = selectedSkinIds
            .Where(IsValidSkinId)
            .Where(_availableSkinIdSet.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (filteredSelected.Length == 0)
            filteredSelected = _availableSkinIds;

        _selectedSkinIds = new HashSet<string>(filteredSelected, StringComparer.Ordinal);
    }

    public IReadOnlyList<string> AvailableSkinIds => _availableSkinIds;

    public IReadOnlyCollection<string> SelectedSkinIds =>
        _availableSkinIds.Where(_selectedSkinIds.Contains).ToArray();

    public bool IsSelected(string? skinId)
    {
        return IsValidSkinId(skinId) && _selectedSkinIds.Contains(skinId!);
    }

    public RandomHeroSkinPoolState SetSelected(string? skinId, bool isSelected)
    {
        if (!IsValidSkinId(skinId))
            return this;

        var normalizedSkinId = skinId!;
        if (!_availableSkinIdSet.Contains(normalizedSkinId))
            return this;

        var next = new HashSet<string>(_selectedSkinIds, StringComparer.Ordinal);
        if (isSelected)
        {
            next.Add(normalizedSkinId);
        }
        else if (next.Count > 1)
        {
            next.Remove(normalizedSkinId);
        }

        return new RandomHeroSkinPoolState(_availableSkinIds, next);
    }

    private static bool IsValidSkinId(string? skinId)
    {
        return !string.IsNullOrWhiteSpace(skinId);
    }
}
