#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

public sealed class RandomHeroPoolState
{
    private readonly string[] _unlockedHeroIds;
    private readonly HashSet<string> _unlockedHeroIdSet;
    private readonly HashSet<string> _selectedHeroIds;

    public RandomHeroPoolState(
        IEnumerable<string> unlockedHeroIds,
        IEnumerable<string>? selectedHeroIds
    )
    {
        if (unlockedHeroIds is null)
        {
            throw new ArgumentNullException(nameof(unlockedHeroIds));
        }

        if (selectedHeroIds is null)
        {
            throw new ArgumentNullException(nameof(selectedHeroIds));
        }

        _unlockedHeroIds = unlockedHeroIds
            .Where(IsValidHeroId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (_unlockedHeroIds.Length == 0)
        {
            throw new ArgumentException(
                "Random hero pool requires at least one unlocked hero.",
                nameof(unlockedHeroIds)
            );
        }

        _unlockedHeroIdSet = new HashSet<string>(_unlockedHeroIds, StringComparer.Ordinal);
        var filteredSelected = selectedHeroIds
            .Where(IsValidHeroId)
            .Where(id => _unlockedHeroIdSet.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (filteredSelected.Length == 0)
        {
            filteredSelected = _unlockedHeroIds;
        }

        _selectedHeroIds = new HashSet<string>(filteredSelected, StringComparer.Ordinal);
    }

    public IReadOnlyList<string> UnlockedHeroIds => _unlockedHeroIds.ToArray();

    public IReadOnlyCollection<string> SelectedHeroIds =>
        _unlockedHeroIds.Where(_selectedHeroIds.Contains).ToArray();

    public bool IsSelected(string heroId)
    {
        return IsValidHeroId(heroId) && _selectedHeroIds.Contains(heroId);
    }

    public RandomHeroPoolState SetSelected(string? heroId, bool isSelected)
    {
        if (!IsValidHeroId(heroId))
        {
            return this;
        }

        var normalizedHeroId = heroId!;
        if (!_unlockedHeroIdSet.Contains(normalizedHeroId))
        {
            return this;
        }

        var next = new HashSet<string>(_selectedHeroIds, StringComparer.Ordinal);
        if (isSelected)
        {
            next.Add(normalizedHeroId);
        }
        else if (next.Count > 1)
        {
            next.Remove(normalizedHeroId);
        }

        return new RandomHeroPoolState(_unlockedHeroIds, next);
    }

    private static bool IsValidHeroId(string? heroId)
    {
        return !string.IsNullOrWhiteSpace(heroId);
    }
}
