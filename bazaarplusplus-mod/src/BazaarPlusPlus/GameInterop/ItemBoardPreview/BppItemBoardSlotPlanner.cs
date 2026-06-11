#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal static class BppItemBoardSlotPlanner
{
    private const int SocketCount = 10;

    public static BppItemBoard Plan(BppItemBoard? board, Action<string>? warn = null)
    {
        if (board == null || board.Cards.Count == 0)
            return board ?? BppItemBoard.Empty;

        return board.Type switch
        {
            BppItemBoardType.SelectableShop => PlanSelectableShop(board, warn),
            BppItemBoardType.SelectableContainer => PlanContainer(board, warn),
            _ => PlanReference(board, warn),
        };
    }

    private static BppItemBoard PlanReference(BppItemBoard board, Action<string>? warn)
    {
        var planned = new List<BppItemBoardCard>(board.Cards.Count);
        var cursor = 0;
        foreach (var card in OrderedCards(board.Cards))
        {
            var span = card.DisplaySpan;
            if (!IsValidSpan(span))
            {
                warn?.Invoke(BuildWarning(board, card, $"invalid span={span}"));
                continue;
            }

            if (TryGetSocket(card.SourceSocketId, span, out var sourceSocket))
            {
                planned.Add(card.WithDisplaySocket(sourceSocket));
                cursor = Math.Max(cursor, (int)sourceSocket + span);
                continue;
            }

            if (card.SourceSocketId.HasValue)
            {
                warn?.Invoke(
                    BuildWarning(
                        board,
                        card,
                        $"source socket {(int)card.SourceSocketId.Value} cannot fit span={span}; using fallback"
                    )
                );
            }
            else
            {
                warn?.Invoke(BuildWarning(board, card, "missing source socket; using fallback"));
            }

            if (!TryFindNextSpanStart(cursor, span, out var fallbackSocket))
            {
                warn?.Invoke(BuildWarning(board, card, $"fallback cannot fit span={span}"));
                continue;
            }

            planned.Add(card.WithDisplaySocket((EContainerSocketId)fallbackSocket));
            cursor = fallbackSocket + span;
        }

        return board.WithCards(planned);
    }

    private static BppItemBoard PlanContainer(BppItemBoard board, Action<string>? warn)
    {
        var planned = new List<BppItemBoardCard>(board.Cards.Count);
        foreach (var card in OrderedCards(board.Cards))
        {
            var span = card.DisplaySpan;
            if (!IsValidSpan(span))
            {
                warn?.Invoke(BuildWarning(board, card, $"invalid span={span}"));
                continue;
            }

            if (!TryGetSocket(card.SourceSocketId, span, out var sourceSocket))
            {
                warn?.Invoke(
                    BuildWarning(
                        board,
                        card,
                        card.SourceSocketId.HasValue
                            ? $"source socket {(int)card.SourceSocketId.Value} cannot fit span={span}"
                            : "missing source socket"
                    )
                );
                continue;
            }

            planned.Add(card.WithDisplaySocket(sourceSocket));
        }

        return board.WithCards(planned);
    }

    private static BppItemBoard PlanSelectableShop(BppItemBoard board, Action<string>? warn)
    {
        var ordered = OrderedCards(board.Cards).ToArray();
        var totalSpan = ordered.Sum(card => Math.Max(0, card.DisplaySpan));
        var cursor = totalSpan <= SocketCount ? Math.Max(0, (SocketCount - totalSpan) / 2) : 0;

        var planned = new List<BppItemBoardCard>(ordered.Length);
        foreach (var card in ordered)
        {
            var span = card.DisplaySpan;
            if (!IsValidSpan(span))
            {
                warn?.Invoke(BuildWarning(board, card, $"invalid span={span}"));
                continue;
            }

            if (!TryFindNextSpanStart(cursor, span, out var displaySocket))
            {
                warn?.Invoke(BuildWarning(board, card, $"shop row overflow at span={span}"));
                break;
            }

            planned.Add(card.WithDisplaySocket((EContainerSocketId)displaySocket));
            cursor = displaySocket + span;
        }

        return board.WithCards(planned);
    }

    private static IEnumerable<BppItemBoardCard> OrderedCards(
        IReadOnlyList<BppItemBoardCard> cards
    ) =>
        cards
            .Select((card, index) => new { card, index })
            .OrderBy(entry => entry.card.Order)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.card);

    private static bool TryGetSocket(
        EContainerSocketId? socketId,
        int span,
        out EContainerSocketId socket
    )
    {
        socket = default;
        if (!socketId.HasValue)
            return false;

        var index = (int)socketId.Value;
        if (!CanFit(index, span))
            return false;

        socket = socketId.Value;
        return true;
    }

    private static bool TryFindNextSpanStart(int start, int span, out int socket)
    {
        socket = -1;
        if (!IsValidSpan(span))
            return false;

        var normalizedStart = Math.Max(0, start);
        for (var index = normalizedStart; index < SocketCount; index++)
        {
            if (!CanFit(index, span))
                continue;

            socket = index;
            return true;
        }

        return false;
    }

    private static bool CanFit(int socket, int span) =>
        socket >= 0 && span > 0 && socket + span <= SocketCount && socket < SocketCount;

    private static bool IsValidSpan(int span) => span > 0 && span <= SocketCount;

    private static string BuildWarning(BppItemBoard board, BppItemBoardCard card, string reason) =>
        $"board={board.Id} type={board.Type} order={card.Order} templateId={card.TemplateId} {reason}";
}
