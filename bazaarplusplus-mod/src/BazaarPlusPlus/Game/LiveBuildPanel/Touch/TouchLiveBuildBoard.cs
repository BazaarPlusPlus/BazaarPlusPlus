#nullable enable
using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Game.LiveBuildPanel.Ui;
using BazaarPlusPlus.Game.Touch;
using BazaarPlusPlus.GameInterop.Touch;
using TheBazaar.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Touch;

/// <summary>
/// One of the Live Build panel's four boards on Android: the native board mapper over the card
/// rental, where the desktop renders an owned <c>MonsterBoardTooltip</c>. Each planned
/// <see cref="BppItemBoardCard"/> rents the game's own card prefab at its tier and enchantment, on
/// its display socket (<see cref="LiveBuildTouchLayout.CardRect"/>). A re-render keeps the cards
/// whose identity, socket, tier, and enchantment did not change and returns the rest, so toggling
/// a candidate or browsing re-rents only what moved. Each card's footprint takes the tap (the
/// rented card takes none) and hands the card to the panel, which toggles it as a candidate or
/// inspects it; a candidate or matched card gets its tag in the band under it.
/// <see cref="ReturnAll"/> gives every card back.
/// </summary>
internal sealed class TouchLiveBuildBoard
{
    private readonly TouchNativeSkin _skin;
    private readonly RectTransform _region;
    private readonly RectTransform _tags;
    private readonly TMP_FontAsset _font;
    private readonly string _owner;
    private readonly Action<BppItemBoardCard, bool> _onTap;
    private readonly Dictionary<string, Cell> _cells = new(StringComparer.Ordinal);
    private readonly Stack<Cell> _free = new();
    private readonly List<(RectTransform Root, Image Background, TextMeshProUGUI Label)> _tagPool =
        new();
    private LiveBuildTouchLayout? _layout;
    private bool _selectable;

    /// <param name="region">The board's cards and tag band, its top-left corner the board's origin.</param>
    /// <param name="font">The game's font, for the tags.</param>
    /// <param name="id">The board, which names its rental owner.</param>
    /// <param name="skin">Shared native tag artwork for this view.</param>
    /// <param name="onTap">The tapped card, and whether the board takes candidates.</param>
    public TouchLiveBuildBoard(
        RectTransform region,
        TMP_FontAsset font,
        BppItemBoardId id,
        TouchNativeSkin skin,
        Action<BppItemBoardCard, bool> onTap
    )
    {
        _skin = skin;
        _region = region;
        _font = font;
        _owner = "live_build." + id;
        _onTap = onTap;
        // Tags draw over the cards.
        _tags = TouchUiKit.Rect(
            "Tags",
            region,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
    }

    /// <summary>Cards rented and not yet returned, Pending or Ready.</summary>
    public int Leases => _cells.Count;

    /// <summary>The rental owner the board's leases are filed under.</summary>
    public string Owner => _owner;

    /// <summary>Main thread. Rents, keeps, or returns cards so the board shows <paramref name="board"/>.</summary>
    public void Render(
        BppItemBoard board,
        bool selectable,
        IReadOnlyCollection<Guid> tagged,
        string tagText,
        LiveBuildTouchLayout layout
    )
    {
        var relayout = !ReferenceEquals(layout, _layout);
        _layout = layout;
        _selectable = selectable;
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
            if (_cells.TryGetValue(key, out var kept))
            {
                if (relayout)
                    Place(kept);
                continue;
            }
            var cell = Take();
            cell.Card = card;
            _cells[key] = cell;
            Place(cell);
            cell.Lease = TouchCardRental.Rent(
                new CardRentalSubject(card.TemplateId, card.Tier, card.EnchantmentType),
                cell.Slot,
                _owner
            );
        }
        RenderTags(tagged, tagText);
    }

    /// <summary>How many of the board's cards are still loading and how many failed.</summary>
    public (int Pending, int Failed) Status()
    {
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
        return (pending, failed);
    }

    /// <summary>The panel closed or switched off: every card goes back to the rental.</summary>
    public int ReturnAll()
    {
        var returned = _cells.Count;
        foreach (var cell in _cells.Values)
            Recycle(cell);
        _cells.Clear();
        foreach (var tag in _tagPool)
            tag.Root.gameObject.SetActive(false);
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
        TouchScreen.Place(cell.Root, rect);
        // The rental centres the card on its slot's top-left corner at the prefab's size.
        cell.Slot.anchoredPosition = new Vector2(rect.Width / 2f, -rect.Height / 2f);
        cell.Slot.localScale = new Vector3(layout.CardScale, layout.CardScale, 1f);
    }

    private void RenderTags(IReadOnlyCollection<Guid> tagged, string tagText)
    {
        var layout = _layout!;
        var shown = 0;
        if (tagged.Count > 0)
        {
            foreach (var cell in _cells.Values)
            {
                var card = cell.Card!;
                if (!tagged.Contains(card.TemplateId))
                    continue;
                var tag = Tag(shown++);
                TouchScreen.Place(
                    tag.Root,
                    layout.TagRect(card.DisplaySocketId ?? 0, card.DisplaySpan)
                );
                tag.Background.color = _selectable
                    ? TouchUiKit.ActiveColor
                    : TouchUiKit.ButtonColor;
                tag.Label.fontSize = layout.TagFont;
                tag.Label.text = tagText;
                tag.Root.gameObject.SetActive(true);
            }
        }
        for (var index = shown; index < _tagPool.Count; index++)
            _tagPool[index].Root.gameObject.SetActive(false);
        _tags.SetAsLastSibling();
    }

    private (RectTransform Root, Image Background, TextMeshProUGUI Label) Tag(int index)
    {
        if (index < _tagPool.Count)
            return _tagPool[index];
        var root = TouchUiKit.Rect(
            "Tag",
            _tags,
            Vector2.zero,
            Vector2.zero,
            Vector2.zero,
            Vector2.zero
        );
        var background = _skin.Tag(root);
        var labelRect = TouchUiKit.Rect(
            "Label",
            root,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        var label = TouchUiKit.Text(
            labelRect,
            _font,
            "",
            _layout!.TagFont,
            TextAlignmentOptions.Center,
            TouchUiKit.TextColor
        );
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        var tag = (root, background, label);
        _tagPool.Add(tag);
        return tag;
    }

    private Cell Take()
    {
        if (_free.Count > 0)
        {
            var reused = _free.Pop();
            reused.Root.gameObject.SetActive(true);
            return reused;
        }
        var root = TouchUiKit.Rect(
            "Card",
            _region,
            Vector2.zero,
            Vector2.zero,
            Vector2.zero,
            Vector2.zero
        );
        var hit = TouchUiKit.Fill(root, new Color(0f, 0f, 0f, 0f));
        var slot = TouchUiKit.Rect(
            "Slot",
            root,
            new(0f, 1f),
            new(0f, 1f),
            Vector2.zero,
            Vector2.zero
        );
        var cell = new Cell(root, slot);
        var button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.None;
        TouchUiActions.OnTap(
            button,
            "live_build.card",
            () =>
            {
                if (cell.Card is { } card)
                    _onTap(card, _selectable);
            }
        );
        // Every card stays under the tag layer.
        _tags.SetAsLastSibling();
        return cell;
    }

    private void Recycle(Cell cell)
    {
        TouchCardRental.Return(cell.Lease);
        cell.Lease = null;
        cell.Card = null;
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
