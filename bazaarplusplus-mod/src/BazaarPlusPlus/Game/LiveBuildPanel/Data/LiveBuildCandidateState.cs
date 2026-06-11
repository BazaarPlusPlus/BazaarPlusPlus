#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Data;

internal sealed class LiveBuildCandidateState
{
    private readonly HashSet<Guid> _templateIds = new();

    public IReadOnlyCollection<Guid> TemplateIds => _templateIds.ToArray();

    public bool HasCandidates => _templateIds.Count > 0;

    public bool Contains(Guid templateId) =>
        templateId != Guid.Empty && _templateIds.Contains(templateId);

    public bool Toggle(Guid templateId)
    {
        if (templateId == Guid.Empty)
            return false;

        if (_templateIds.Remove(templateId))
            return true;

        _templateIds.Add(templateId);
        return true;
    }

    public void Clear() => _templateIds.Clear();

    public void PruneToSelectableRows(IEnumerable<BppItemBoard?> boards)
    {
        var stillSelectable = new HashSet<Guid>();
        foreach (var board in boards)
        {
            if (board == null || !CanToggle(board.Type))
                continue;

            foreach (var card in board.Cards)
            {
                if (card.TemplateId != Guid.Empty)
                    stillSelectable.Add(card.TemplateId);
            }
        }

        _templateIds.RemoveWhere(templateId => !stillSelectable.Contains(templateId));
    }

    public static bool CanToggle(BppItemBoardType type) =>
        type is BppItemBoardType.SelectableContainer or BppItemBoardType.SelectableShop;
}
