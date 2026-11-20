using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Game.LiveBuildPanel.Ui;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.LiveBuild;

internal sealed class TouchLiveBuildBoard
{
    private readonly NativeSkin _skin;
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

    public TouchLiveBuildBoard(
        RectTransform region,
        TMP_FontAsset font,
        BppItemBoardId id,
        NativeSkin skin,
        Action<BppItemBoardCard, bool> onTap
    )
    {
        _skin = skin;
        _region = region;
        _font = font;
        _owner = "live_build." + id;
        _onTap = onTap;
        _tags = UiKit.Rect("Tags", region, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    public int Leases => _cells.Count;

    public string Owner => _owner;

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
            cell.Lease = MobilePlatform.Port.CardRental.Rent(
                CardRentalSubject.Of(card),
                cell.Slot,
                _owner
            );
        }
        RenderTags(tagged, tagText);
    }

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
                tag.Background.color = _selectable ? UiKit.ActiveColor : UiKit.ButtonColor;
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
        var root = UiKit.Rect("Tag", _tags, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        var background = _skin.Tag(root);
        var labelRect = UiKit.Rect(
            "Label",
            root,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        var label = UiKit.Text(
            labelRect,
            _font,
            "",
            _layout!.TagFont,
            TextAlignmentOptions.Center,
            UiKit.TextColor
        );
        Il2CppUi.SetWordWrapping(label, false);
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
        var root = UiKit.Rect(
            "Card",
            _region,
            Vector2.zero,
            Vector2.zero,
            Vector2.zero,
            Vector2.zero
        );
        var hit = UiKit.HitArea(root);
        var slot = UiKit.Rect("Slot", root, new(0f, 1f), new(0f, 1f), Vector2.zero, Vector2.zero);
        var cell = new Cell(root, slot);
        var button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.None;
        UiActions.OnTap(
            button,
            "live_build.card",
            () =>
            {
                if (cell.Card is { } card)
                    _onTap(card, _selectable);
            }
        );
        _tags.SetAsLastSibling();
        return cell;
    }

    private void Recycle(Cell cell)
    {
        MobilePlatform.Port.CardRental.Return(cell.Lease);
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

        public CardRentalLease<GameCardPreview>? Lease { get; set; }
    }
}
