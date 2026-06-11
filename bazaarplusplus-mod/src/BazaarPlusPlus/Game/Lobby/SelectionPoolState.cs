#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarPlusPlus.Game.Lobby;

/// <summary>
/// Shared immutable selection-pool state: a validated set of available ids plus a
/// selected subset that can never shrink below one entry. Toggling produces a new
/// instance (copy-on-write) rather than mutating in place.
/// </summary>
/// <typeparam name="TSelf">The concrete pool-state type, returned by mutating operations.</typeparam>
public abstract class SelectionPoolState<TSelf>
    where TSelf : SelectionPoolState<TSelf>
{
    private readonly string[] _availableIds;
    private readonly HashSet<string> _availableIdSet;
    private readonly HashSet<string> _selectedIds;

    protected SelectionPoolState(
        IEnumerable<string> availableIds,
        IEnumerable<string>? selectedIds,
        string emptyAvailableMessage,
        string? availableParamName
    )
    {
        if (availableIds is null)
        {
            throw new ArgumentNullException(availableParamName ?? nameof(availableIds));
        }

        if (selectedIds is null)
        {
            throw new ArgumentNullException(nameof(selectedIds));
        }

        _availableIds = availableIds.Where(IsValidId).Distinct(StringComparer.Ordinal).ToArray();

        if (_availableIds.Length == 0)
        {
            throw availableParamName is null
                ? new ArgumentException(emptyAvailableMessage)
                : new ArgumentException(emptyAvailableMessage, availableParamName);
        }

        _availableIdSet = new HashSet<string>(_availableIds, StringComparer.Ordinal);

        var filteredSelected = selectedIds
            .Where(IsValidId)
            .Where(_availableIdSet.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (filteredSelected.Length == 0)
        {
            filteredSelected = _availableIds;
        }

        _selectedIds = new HashSet<string>(filteredSelected, StringComparer.Ordinal);
    }

    /// <summary>A defensive copy of the validated available ids, in their stable order.</summary>
    protected IReadOnlyList<string> AvailableIdsSnapshot => _availableIds.ToArray();

    /// <summary>A snapshot of the selected ids, ordered to match <see cref="AvailableIdsSnapshot"/>.</summary>
    protected IReadOnlyCollection<string> SelectedIdsSnapshot =>
        _availableIds.Where(_selectedIds.Contains).ToArray();

    protected bool IsIdSelected(string? id)
    {
        return IsValidId(id) && _selectedIds.Contains(id!);
    }

    /// <summary>
    /// Returns a new state with <paramref name="id"/> added or removed. Unknown/invalid ids and
    /// removals that would empty the selection are ignored, returning the current instance.
    /// </summary>
    protected TSelf WithSelected(string? id, bool isSelected)
    {
        if (!IsValidId(id))
        {
            return (TSelf)this;
        }

        var normalizedId = id!;
        if (!_availableIdSet.Contains(normalizedId))
        {
            return (TSelf)this;
        }

        var next = new HashSet<string>(_selectedIds, StringComparer.Ordinal);
        if (isSelected)
        {
            next.Add(normalizedId);
        }
        else if (next.Count > 1)
        {
            next.Remove(normalizedId);
        }

        return CreateNext(_availableIds, next);
    }

    /// <summary>
    /// Builds the next concrete state from an already-validated available set and a selected set.
    /// </summary>
    protected abstract TSelf CreateNext(string[] availableIds, IEnumerable<string> selectedIds);

    private static bool IsValidId(string? id)
    {
        return !string.IsNullOrWhiteSpace(id);
    }
}
