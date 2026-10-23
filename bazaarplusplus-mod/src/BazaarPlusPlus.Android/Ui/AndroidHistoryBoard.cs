using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using Il2CppTheBazaar.UI;
using UnityEngine;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// One of History's native boards on Android, the counterpart of the desktop's owned
/// <c>MonsterBoardTooltip</c> preview: each planned card of a stored battle
/// (<see cref="HistoryBoardProjection.Items"/>) rents the game's own card prefab at its tier and
/// enchantment, on its display socket (<see cref="HistoryTouchLayout.CardRect"/>). A re-render
/// keeps the cards whose identity, socket, tier, and enchantment did not change and returns the
/// rest, so stepping through a run's battles re-rents only what changed. A card's footprint takes
/// the tap (the rented card takes none) and hands the card to the panel's inspection.
/// <see cref="Facts"/> reports what the rented cards show as Core's board facts.
/// <see cref="ReturnAll"/> gives every card back.
/// </summary>
internal sealed class AndroidHistoryBoard
{
    private readonly string _owner;
    private readonly Action<BppItemBoardCard> _onTap;
    private readonly Dictionary<string, Cell> _cells = new(StringComparer.Ordinal);
    private readonly Stack<Cell> _free = new();
    private RectTransform? _region;
    private HistoryTouchLayout? _layout;
    private bool _shown;

    public AndroidHistoryBoard(string owner, Action<BppItemBoardCard> onTap)
    {
        _owner = owner;
        _onTap = onTap;
    }

    /// <summary>The rental owner the board's leases are filed under.</summary>
    public string Owner => _owner;

    /// <summary>Cards rented and not yet returned, Pending or Ready.</summary>
    public int Leases => _cells.Count;

    /// <summary>
    /// Main thread. The view laid out a new board region: the old cells go back to the rental and
    /// the next <see cref="Render"/> rents into the new one.
    /// </summary>
    public void Attach(RectTransform region, HistoryTouchLayout layout)
    {
        ReturnAll();
        _free.Clear();
        _region = region;
        _layout = layout;
    }

    /// <summary>Main thread. Rents, keeps, or returns cards so the board shows <paramref name="board"/>.</summary>
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
            cell.Lease = AndroidCardRental.Rent(
                new CardRentalSubject(card.TemplateId, card.Tier, card.EnchantmentType),
                cell.Slot,
                _owner
            );
        }
    }

    /// <summary>
    /// What the board shows, as Core's board facts; none (no message) while it shows nothing.
    /// </summary>
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

    /// <summary>The panel closed, the scene changed, or the view was laid out again.</summary>
    public int ReturnAll()
    {
        var returned = _cells.Count;
        foreach (var cell in _cells.Values)
            Recycle(cell);
        _cells.Clear();
        _shown = false;
        return returned;
    }

    // Instance, template, socket, tier, and enchantment: what the rented card shows and where.
    private static string Key(BppItemBoardCard card) =>
        $"{card.InstanceId}|{card.TemplateId:N}|{card.DisplaySocketId}|{card.DisplaySpan}|{card.Tier}|{card.EnchantmentType}";

    private void Place(Cell cell)
    {
        var layout = _layout!;
        var card = cell.Card!;
        var rect = layout.CardRect(card.DisplaySocketId ?? 0, card.DisplaySpan);
        AndroidTouchScreen.Place(cell.Root, rect);
        // The rental centres the card on its slot's top-left corner at the prefab's size.
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
        var root = AndroidUiKit.Rect(
            "Card",
            _region!,
            Vector2.zero,
            Vector2.zero,
            Vector2.zero,
            Vector2.zero
        );
        var slot = AndroidUiKit.Rect(
            "Slot",
            root,
            new(0f, 1f),
            new(0f, 1f),
            Vector2.zero,
            Vector2.zero
        );
        var cell = new Cell(root, slot);
        AndroidHistoryUi.TapArea(
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
        AndroidCardRental.Return(cell.Lease);
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

        public CardRentalLease<CardPreviewBase>? Lease { get; set; }
    }
}
