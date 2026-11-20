using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.History;

internal sealed class TouchHistoryBoard
{
    private readonly string _owner;
    private readonly Action<BppItemBoardCard> _onTap;
    private readonly Dictionary<string, Cell> _cells = new(StringComparer.Ordinal);
    private readonly Stack<Cell> _free = new();
    private RectTransform? _region;
    private HistoryTouchLayout? _layout;
    private bool _shown;

    public TouchHistoryBoard(string owner, Action<BppItemBoardCard> onTap)
    {
        _owner = owner;
        _onTap = onTap;
    }

    public string Owner => _owner;

    public int Leases => _cells.Count;

    public void Attach(RectTransform region, HistoryTouchLayout layout)
    {
        ReturnAll();
        _free.Clear();
        _region = region;
        _layout = layout;
    }

    public void Render(BppItemBoard board)
    {
        if (_region == null || _layout == null)
            return;
        _shown = true;
        var wanted = new Dictionary<string, BppItemBoardCard>(StringComparer.Ordinal);
        foreach (var card in board.Cards)
        {
            if (card.TemplateId == Guid.Empty || card.DisplaySocketId is not { })
                continue;
            wanted.TryAdd(Key(card), card);
        }
        foreach (var key in _cells.Keys.Where(key => !wanted.ContainsKey(key)).ToArray())
        {
            Recycle(_cells[key]);
            _cells.Remove(key);
        }
        foreach (var (key, card) in wanted)
        {
            if (_cells.ContainsKey(key))
                continue;
            var cell = Take();
            cell.Card = card;
            _cells[key] = cell;
            Place(cell);
            cell.Lease = MobilePlatform.Port.CardRental.Rent(
                CardRentalSubject.Of(card),
                cell.Slot,
                _owner
            );
        }
    }

    public HistoryBoardFacts Facts()
    {
        if (!_shown)
            return default;
        var pending = 0;
        var failed = 0;
        foreach (var cell in _cells.Values)
        {
            switch (cell.Lease?.State)
            {
                case CardRentalLeaseState.Pending:
                    pending++;
                    break;
                case CardRentalLeaseState.Failed:
                    failed++;
                    break;
            }
        }
        return HistoryBoardProjection.Facts(_cells.Count, pending, failed);
    }

    public int ReturnAll()
    {
        var returned = _cells.Count;
        foreach (var cell in _cells.Values)
            Recycle(cell);
        _cells.Clear();
        _shown = false;
        return returned;
    }

    private static string Key(BppItemBoardCard card) =>
        $"{card.InstanceId}|{card.TemplateId:N}|{card.DisplaySocketId}|{card.DisplaySpan}|{card.Tier}|{card.EnchantmentType}|{CardRentalSubject.AttributesKey(card.Attributes)}";

    private void Place(Cell cell)
    {
        var layout = _layout!;
        var card = cell.Card!;
        var rect = layout.CardRect(card.DisplaySocketId ?? 0, card.DisplaySpan);
        TouchScreen.Place(cell.Root, rect);
        // The rental centers the card on its slot's top-left corner.
        cell.Slot.anchoredPosition = new Vector2(rect.Width / 2f, -rect.Height / 2f);
        cell.Slot.localScale = new Vector3(layout.CardScale, layout.CardScale, 1f);
    }

    private Cell Take()
    {
        if (_free.Count > 0)
        {
            var reused = _free.Pop();
            reused.Root.gameObject.SetActive(true);
            return reused;
        }
        var root = UiKit.Rect(
            "Card",
            _region!,
            Vector2.zero,
            Vector2.zero,
            Vector2.zero,
            Vector2.zero
        );
        var slot = UiKit.Rect("Slot", root, new(0f, 1f), new(0f, 1f), Vector2.zero, Vector2.zero);
        var cell = new Cell(root, slot);
        TouchHistoryUi.TapArea(
            root,
            "board_card",
            () =>
            {
                if (cell.Card is { } card)
                    _onTap(card);
            }
        );
        return cell;
    }

    private void Recycle(Cell cell)
    {
        MobilePlatform.Port.CardRental.Return(cell.Lease);
        cell.Lease = null;
        cell.Card = null;
        if (cell.Root != null)
            cell.Root.gameObject.SetActive(false);
        _free.Push(cell);
    }

    private sealed class Cell(RectTransform root, RectTransform slot)
    {
        public RectTransform Root { get; } = root;

        public RectTransform Slot { get; } = slot;

        public BppItemBoardCard? Card { get; set; }

        public CardRentalLease<GameCardPreview>? Lease { get; set; }
    }
}
