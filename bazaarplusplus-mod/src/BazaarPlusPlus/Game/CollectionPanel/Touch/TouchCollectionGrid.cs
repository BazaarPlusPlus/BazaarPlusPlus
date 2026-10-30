#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile.Ui;
using TheBazaar.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CollectionPanel.Touch;

/// <summary>
/// The Collection grid on a touch screen: the desktop's shelf geometry from Core
/// (<see cref="CollectionGridLayout"/>, <see cref="CollectionGridPixelization"/>,
/// <see cref="CollectionCardFitMath"/>) laid out in a uGUI scroll view, with a rented native card
/// in each cell near the viewport. Cells scrolled more than a shelf out of view return their card
/// to the rental; a republished grid keeps the cards whose template stays visible
/// (<see cref="CollectionGridRetentionPlan"/>). A cell takes the tap, never the card under it.
/// </summary>
internal sealed class TouchCollectionGrid
{
    public const string RentalOwner = "collection.grid";

    // New rentals per frame: each one instantiates a prefab or sets a pooled card up.
    private const int RentBudgetPerFrame = 8;

    // A rent past the first stops here: a template the catalog did not hand over (a catalog read
    // from its cache) is one game data read inside Rent.
    private const double RentBudgetMs = 5;

    private readonly TouchCollectionGridPort _port;
    private readonly TMP_FontAsset _font;
    private readonly float _captionSize;
    private readonly Action<CollectionCardVm, RectTransform?> _onTap;
    private readonly ScrollRect _scroll;
    private readonly RectTransform _viewport;
    private readonly RectTransform _content;
    private readonly Dictionary<int, Cell> _realized = new();
    private readonly Stack<Cell> _free = new();
    private readonly HashSet<string> _reportedFailures = new(StringComparer.Ordinal);
    private int _revision = -1;
    private bool _layoutDirty = true;

    public TouchCollectionGrid(
        RectTransform area,
        TouchCollectionGridPort port,
        Action<CollectionCardVm, RectTransform?> onTap,
        TMP_FontAsset font,
        float captionSize
    )
    {
        _port = port;
        _font = font;
        _captionSize = captionSize;
        _onTap = onTap;
        (_scroll, _content) = UiKit.VerticalScroll(area);
        _viewport = _scroll.viewport;
    }

    /// <summary>The grid's scroll view, for the panel's drag-scroll log.</summary>
    public ScrollRect Scroll => _scroll;

    /// <summary>Cards held by the grid's cells, Pending or Ready.</summary>
    public int Leases => _realized.Count;

    /// <summary>
    /// A cell's card is still setting up: a relayout waits for it (<see cref="BazaarPlusPlus.Core.Ui.TouchRelayoutDebounce"/>).
    /// Read every frame, so a plain loop, not LINQ.
    /// </summary>
    public bool SettingUp
    {
        get
        {
            foreach (var cell in _realized.Values)
                if (cell.Lease is { State: CardRentalLeaseState.Pending })
                    return true;
            return false;
        }
    }

    public void ResetScroll()
    {
        _scroll.StopMovement();
        _content.anchoredPosition = Vector2.zero;
    }

    /// <summary>Main thread, every frame while the panel is open.</summary>
    public void Tick()
    {
        var width = _viewport.rect.width;
        if (width > 1f && Math.Abs(width - _port.ViewportWidth) > 0.5f)
        {
            _port.ViewportWidth = width;
            _layoutDirty = true;
        }
        if (_port.Revision != _revision)
        {
            Retain();
            _revision = _port.Revision;
            _layoutDirty = true;
        }
        if (_layoutDirty)
        {
            _content.sizeDelta = new Vector2(0f, _port.ContentHeight);
            foreach (var pair in _realized)
                Place(pair.Value, pair.Key);
            _layoutDirty = false;
        }
        Realize();
        ReportFailures();
    }

    // A failed rental leaves its cell empty; log each failure reason once per open.
    private void ReportFailures()
    {
        foreach (var cell in _realized.Values)
        {
            if (
                cell.Lease is not { State: CardRentalLeaseState.Failed } lease
                || !_reportedFailures.Add(lease.FailureReason ?? string.Empty)
            )
                continue;
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Touch,
                    "touch.collection.card_rent_failed",
                    storm: ["reason_code"]
                ),
                ("reason_code", lease.FailureReason),
                ("template_id", cell.TemplateId)
            );
        }
    }

    /// <summary>The panel closed or switched off: every cell returns its card.</summary>
    public void ReturnAll()
    {
        foreach (var cell in _realized.Values)
            Recycle(cell);
        _realized.Clear();
        _revision = -1;
    }

    // The publish replaced the visible list: keep a cell whose template is still visible, under
    // its new index, and return the rest.
    private void Retain()
    {
        if (_realized.Count == 0)
            return;
        var realizedIds = new Dictionary<int, Guid>(_realized.Count);
        foreach (var pair in _realized)
            realizedIds[pair.Key] = pair.Value.TemplateId;
        var nextIds = new Guid[_port.Visible.Count];
        for (var index = 0; index < nextIds.Length; index++)
            nextIds[index] = _port.Visible[index].Id;
        var retained = CollectionGridRetentionPlan.Build(realizedIds, nextIds);
        var previous = new Dictionary<int, Cell>(_realized);
        _realized.Clear();
        foreach (var pair in previous)
        {
            if (retained.TryGetValue(pair.Key, out var next))
                _realized[next] = pair.Value;
            else
                Recycle(pair.Value);
        }
    }

    private void Realize()
    {
        var layout = _port.Layout;
        if (layout.Count == 0)
        {
            ReturnOutside(0, -1);
            return;
        }
        var pixels = _port.Pixels;
        var (first, last) = layout.VisibleIndexRange(
            Math.Max(0f, _content.anchoredPosition.y),
            _viewport.rect.height,
            pixels.Unit,
            CollectionGridConstants.GridGap,
            pixels.OriginY
        );
        ReturnOutside(first, last);

        var budget = RentBudgetPerFrame;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        for (var index = first; index <= last && budget > 0; index++)
        {
            if (_realized.ContainsKey(index))
                continue;
            if (
                budget < RentBudgetPerFrame
                && (System.Diagnostics.Stopwatch.GetTimestamp() - started)
                    * 1000.0
                    / System.Diagnostics.Stopwatch.Frequency
                    >= RentBudgetMs
            )
                break;
            var card = _port.Visible[index];
            var cell = Take();
            cell.TemplateId = card.Id;
            _realized[index] = cell;
            Place(cell, index);
            cell.Lease = TouchCardRental.Rent(
                new CardRentalSubject(card.Id, card.StartingTier),
                cell.Slot,
                RentalOwner
            );
            budget--;
        }
    }

    private void ReturnOutside(int first, int last)
    {
        List<int>? leaving = null;
        foreach (var index in _realized.Keys)
            if (index < first || index > last)
                (leaving ??= new List<int>()).Add(index);
        if (leaving == null)
            return;
        foreach (var index in leaving)
        {
            Recycle(_realized[index]);
            _realized.Remove(index);
        }
    }

    private void Place(Cell cell, int index)
    {
        var pixels = _port.Pixels;
        var rect = _port.Layout.ContentRectFor(
            index,
            pixels.Unit,
            CollectionGridConstants.GridGap,
            pixels.OriginX,
            pixels.OriginY
        );
        var card = _port.Visible[index];
        cell.Root.anchoredPosition = new Vector2(rect.X, -rect.Y);
        cell.Root.sizeDelta = new Vector2(rect.Width, rect.Height);
        var bounds = NaturalBounds(card);
        var captionHeight = _captionSize * 2.6f;
        cell.Caption.text = card.DisplayName;
        var local = new CollectionGridRect(
            0f,
            0f,
            rect.Width,
            Math.Max(1f, rect.Height - captionHeight)
        );
        var scale = CollectionCardFitMath.ComputeScale(
            bounds,
            bounds.Width / NativeCardPrefab.Height,
            local,
            CollectionGridConstants.GridGap,
            CollectionGridConstants.CellContentInset,
            NativeCardPrefab.FrameHeightOverSocket
        );
        var scaleX =
            card.Type == CardType.Item ? scale * CollectionGridConstants.ItemCardWidthScale : scale;
        var (x, y) = CollectionCardFitMath.ComputeAnchoredPosition(
            bounds,
            scaleX,
            scale,
            local,
            0f
        );
        cell.Slot.anchoredPosition = new Vector2(x, y);
        cell.Slot.localScale = new Vector3(scaleX, scale, 1f);
        cell.Index = index;
    }

    /// <summary>
    /// A rented card's measured size: 484 high in the socket, the frame art 3.7% taller, an item as
    /// wide as its size makes it and a skill square. Its centre sits on its slot.
    /// </summary>
    public static CardVisualBounds NaturalBounds(CollectionCardVm card)
    {
        var width =
            card.Type == CardType.Skill
                ? NativeCardPrefab.Height
                : NativeCardPrefab.ItemWidth(card.Size);
        return new CardVisualBounds(
            width,
            NativeCardPrefab.Height * NativeCardPrefab.FrameHeightOverSocket,
            0f,
            0f
        );
    }

    private Cell Take()
    {
        if (_free.Count > 0)
        {
            var reused = _free.Pop();
            reused.Root.gameObject.SetActive(true);
            return reused;
        }
        var root = new GameObject("Cell").AddComponent<RectTransform>();
        root.SetParent(_content, false);
        root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
        root.pivot = new Vector2(0f, 1f);
        var hit = UiKit.Fill(root, new Color(0f, 0f, 0f, 0f));
        var slot = UiKit.Rect("Slot", root, new(0f, 1f), new(0f, 1f), Vector2.zero, Vector2.zero);
        var caption = UiKit.Text(
            UiKit.Rect(
                "Name",
                root,
                Vector2.zero,
                new Vector2(1f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, _captionSize * 2.6f)
            ),
            _font,
            "",
            _captionSize,
            TextAlignmentOptions.Top,
            UiKit.TextColor
        );
        caption.textWrappingMode = TextWrappingModes.Normal;
        caption.overflowMode = TextOverflowModes.Ellipsis;
        var cell = new Cell(root, slot, caption);
        var button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(
            (UnityEngine.Events.UnityAction)(
                () =>
                {
                    if (cell.Index >= 0 && cell.Index < _port.Visible.Count)
                        _onTap(
                            _port.Visible[cell.Index],
                            cell.Lease?.State == CardRentalLeaseState.Ready ? cell.Slot : null
                        );
                }
            )
        );
        return cell;
    }

    private void Recycle(Cell cell)
    {
        TouchCardRental.Return(cell.Lease);
        cell.Lease = null;
        cell.Index = -1;
        cell.Root.gameObject.SetActive(false);
        _free.Push(cell);
    }

    private sealed class Cell(RectTransform root, RectTransform slot, TextMeshProUGUI caption)
    {
        public RectTransform Root { get; } = root;

        public TextMeshProUGUI Caption { get; } = caption;

        public RectTransform Slot { get; } = slot;

        public Guid TemplateId { get; set; }

        public int Index { get; set; } = -1;

        public CardRentalLease<CardPreviewBase>? Lease { get; set; }
    }
}
