#nullable enable
using BazaarPlusPlus.GameInterop.MonsterBoardPreview;
using TheBazaar;
using TheBazaar.UI;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed partial class NativeCardPreviewHost
{
    private static readonly Dictionary<CardPreviewBase, BorrowedHover> Borrowed = new();
    private static BorrowedHover? _borrowedHovered;

    // Registration observes hover and tooltip data only. The board still owns the rental.
    internal static IDisposable RegisterBorrowedHover(CardPreviewBase card)
    {
        if (Borrowed.TryGetValue(card, out var previous))
            previous.Dispose();
        var lease = new BorrowedHover(card);
        Borrowed.Add(card, lease);
        return lease;
    }

    internal static void ForgetBorrowed(CardPreviewBase card)
    {
        if (Borrowed.TryGetValue(card, out var lease))
            lease.Dispose();
    }

    internal static void ObserveBorrowedHover(CardPreviewBase card, bool entered)
    {
        if (!Borrowed.TryGetValue(card, out var lease))
            return;
        if (entered)
        {
            lease.RefreshData();
            _borrowedHovered = lease;
        }
        else
        {
            lease.Retire();
            if (ReferenceEquals(_borrowedHovered, lease))
                _borrowedHovered = null;
        }
    }

    private bool TryRefreshBorrowed(
        NativeTooltipRefreshRequest request,
        out NativeTooltipRefreshResult result
    )
    {
        result = default;
        var lease = _borrowedHovered;
        if (lease == null || lease.Card == null || !lease.Card.gameObject.activeInHierarchy)
            return false;
        try
        {
            result = RefreshHoveredTooltip(
                lease.Card,
                lease.Card._cardData.Id,
                _ => { },
                request,
                lease.RefreshData
            );
        }
        catch (Exception error)
        {
            result = Result(
                NativeTooltipRefreshStatus.Failed,
                new(
                    NativeCardPreviewOperation.GetTooltipData,
                    NativeCardPreviewFailureReason.Unexpected,
                    lease.Card._cardData.Id,
                    error
                )
            );
        }
        return true;
    }

    private sealed class BorrowedHover : IDisposable
    {
        internal readonly CardPreviewBase Card;
        private NativeMonsterBoardTooltipGate.Lease? _dataLease;

        internal BorrowedHover(CardPreviewBase card)
        {
            Card = card;
            RefreshData();
        }

        internal void Retire()
        {
            _dataLease?.Retire();
            _dataLease = null;
        }

        internal void RefreshData()
        {
            Retire();
            if (Card != null && Card._tooltipData != null)
                _dataLease = NativeMonsterBoardTooltipPatch.Gate.Register(Card._tooltipData);
        }

        public void Dispose()
        {
            Retire();
            Borrowed.Remove(Card);
            if (!ReferenceEquals(_borrowedHovered, this))
                return;
            _borrowedHovered = null;
            // Only hide the primary tooltip if it still belongs to this card.
            var parent = Data.TooltipParentComponent;
            if (
                Card != null
                && parent != null
                && !parent.HasAnyLockedTooltipControllers()
                && ReferenceEquals(
                    parent.CardTooltipController?.CurrentTooltipData,
                    Card._tooltipData
                )
            )
                parent.HideCardTooltipController();
        }
    }
}
