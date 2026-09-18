#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarPlusPlus.GameInterop.AssetLoading;
using BazaarPlusPlus.GameInterop.StaticCards;
using DG.Tweening;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.UI;
using TheBazaar.UI.Tooltips;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.GameInterop.MonsterBoardPreview;

internal enum NativeMonsterBoardStatus
{
    Loading,
    Ready,
    Partial,
    Empty,
    Failed,
}

// Owns a clone of the native monster board, never the active tooltip's view.
internal sealed partial class OwnedMonsterBoardPreview : IDisposable
{
    private const string TooltipAddress = "Assets/Prefabs/UIPrefabs/Tooltips/Tooltip_P.prefab";
    private readonly GameObject _root;
    private readonly RectTransform _region;
    private readonly CanvasGroup _gate;
    private readonly RectTransform _boardRegion;
    private readonly RectTransform _skillViewport;
    private readonly RectTransform _skillContent;
    private readonly ScrollRect _skillScroll;
    private RectTransform? _ownedSkillParent;
    private readonly Vector3[] _carpetCorners = new Vector3[4];
    private readonly Action<NativeMonsterBoardStatus, Exception?> _report;
    private readonly bool _itemsOnly;
    private MonsterBoardTooltip? _view;
    private Task _pending = Task.CompletedTask;
    private int _generation;
    private string? _signature;
    private bool _disposed;
    private bool _ready;
    private readonly List<NativeBoardCardRental> _rentals = new();
    private readonly HashSet<Guid> _missingTemplates = new();
    private object? _catalog;
    private bool _fitDirty = true;
    private int _fitFrames;
    internal int GeometryVersion { get; private set; }
    private (int Generation, List<TCardInstanceItem> Items, List<TCardInstanceSkill> Skills)? _next;
    private List<TCardInstanceItem>? _lastItems;
    private List<TCardInstanceSkill>? _lastSkills;
    private string? _lastRequest;
    private bool _draining;
    private float _itemFooterHeight;
    private float _itemTopInset;

    internal OwnedMonsterBoardPreview(
        Transform parent,
        int sortingOrder,
        bool skillsBelow,
        Action<NativeMonsterBoardStatus, Exception?> report,
        bool itemsOnly = false
    )
    {
        _report = report;
        _itemsOnly = itemsOnly;
        _root = new GameObject(
            "OwnedMonsterBoardPreview",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(GraphicRaycaster)
        );
        _root.transform.SetParent(parent, false);
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        _region = new GameObject(
            "BoardRegion",
            typeof(RectTransform),
            typeof(CanvasGroup)
        ).GetComponent<RectTransform>();
        _region.SetParent(_root.transform, false);
        _region.anchorMin = _region.anchorMax = Vector2.zero;
        _region.pivot = Vector2.zero;
        _region.sizeDelta = Vector2.zero;
        _gate = _region.GetComponent<CanvasGroup>();
        _gate.alpha = 0;
        _gate.blocksRaycasts = false;
        _boardRegion = Region("Cards", _region);
        _skillViewport = Region("SkillsViewport", _region);
        _boardRegion.anchorMin = new Vector2(0, skillsBelow ? .26f : 0);
        _boardRegion.anchorMax = new Vector2(1, skillsBelow ? 1 : .74f);
        _skillViewport.anchorMin = new Vector2(0, skillsBelow ? 0 : .78f);
        _skillViewport.anchorMax = new Vector2(1, skillsBelow ? .22f : 1);
        _skillViewport.gameObject.AddComponent<RectMask2D>();
        _skillViewport.gameObject.AddComponent<Image>().color = Color.clear;
        _skillContent = Region("SkillsContent", _skillViewport);
        _skillContent.anchorMin = Vector2.zero;
        _skillContent.anchorMax = new Vector2(0, 1);
        _skillContent.pivot = new Vector2(0, .5f);
        _skillScroll = _skillViewport.gameObject.AddComponent<ScrollRect>();
        _skillScroll.viewport = _skillViewport;
        _skillScroll.content = _skillContent;
        _skillScroll.horizontal = true;
        _skillScroll.vertical = false;
        _skillScroll.movementType = ScrollRect.MovementType.Clamped;
        _skillScroll.scrollSensitivity = 45f;
        var track = Region("SkillsScrollbar", _skillViewport);
        track.anchorMax = new Vector2(1, 0);
        track.pivot = Vector2.zero;
        track.sizeDelta = new Vector2(0, 4);
        track.gameObject.AddComponent<Image>().color = new Color(.18f, .13f, .07f);
        var handle = Region("Handle", track);
        var image = handle.gameObject.AddComponent<Image>();
        image.color = new Color(.65f, .52f, .28f);
        var scrollbar = track.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = image;
        scrollbar.direction = Scrollbar.Direction.LeftToRight;
        _skillScroll.horizontalScrollbar = scrollbar;
        _skillScroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        if (itemsOnly)
        {
            _boardRegion.anchorMin = Vector2.zero;
            _boardRegion.anchorMax = Vector2.one;
            _skillViewport.gameObject.SetActive(false);
        }
    }

    private static RectTransform Region(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    internal void SetBounds(Rect bounds, float itemFooterHeight = 0, float itemTopInset = 0)
    {
        if (
            _region.anchoredPosition == bounds.position
            && _region.sizeDelta == bounds.size
            && _itemFooterHeight == itemFooterHeight
            && _itemTopInset == itemTopInset
        )
            return;
        _fitDirty = true;
        _region.anchoredPosition = bounds.position;
        _region.sizeDelta = bounds.size;
        _itemFooterHeight = itemFooterHeight;
        _itemTopInset = itemTopInset;
        if (_itemsOnly)
        {
            _boardRegion.offsetMin = new Vector2(0, itemFooterHeight);
            _boardRegion.offsetMax = new Vector2(0, -itemTopInset);
        }
        Fit();
        if (_ready)
            _gate.alpha = 1;
    }

    internal void Render(
        string signature,
        List<TCardInstanceItem> items,
        List<TCardInstanceSkill> skills
    )
    {
        if (_disposed)
            return;
        _lastRequest = signature;
        _lastItems = items;
        _lastSkills = skills;
        var catalog = BppStaticDataAccess.TryGetReadyManagerObject();
        if (!ReferenceEquals(catalog, _catalog))
        {
            _catalog = catalog;
            _missingTemplates.Clear();
            _signature = null;
        }
        signature += NativeBoardSignature.For(items.Cast<TCardInstance>().Concat(skills));
        if (signature == _signature)
            return;
        _signature = signature;
        EndPointer();
        _generation++;
        _ready = false;
        GeometryVersion++;
        _gate.alpha = 0;
        _gate.blocksRaycasts = false;
        _report(NativeMonsterBoardStatus.Loading, null);
        _next = (_generation, items, skills);
        if (!_draining)
            _pending = Drain();
    }

    private async Task Drain()
    {
        _draining = true;
        try
        {
            while (_next is { } work && !_disposed)
            {
                _next = null;
                await Replace(work.Generation, work.Items, work.Skills);
            }
        }
        finally
        {
            _draining = false;
        }
    }

    private async Task Replace(
        int generation,
        List<TCardInstanceItem> items,
        List<TCardInstanceSkill> skills
    )
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                ClearView();
                if (!Services.TryGet<AssetLoader>(out var loader) || loader == null)
                    throw new InvalidOperationException("Native asset loader is unavailable.");
                var prefab = await NativeGlobalAssetLoader.LoadByAddressAsync<GameObject>(
                    loader,
                    TooltipAddress
                );
                if (_disposed || generation != _generation)
                    return;
                var donor =
                    prefab != null
                        ? prefab.GetComponentInChildren<MonsterBoardTooltip>(true)
                        : null;
                if (donor == null)
                    throw new InvalidOperationException(
                        "Native monster board prefab is unavailable."
                    );
                _view = UnityEngine.Object.Instantiate(donor, _boardRegion, false);
                // The tooltip prefab overrides sorting at order zero. Owned previews must
                // inherit this host canvas, or the opaque history panel covers every card.
                foreach (var nestedCanvas in _view.GetComponentsInChildren<Canvas>(true))
                    nestedCanvas.overrideSorting = false;
                _view.gameObject.SetActive(true);
                _view._healthText.gameObject.SetActive(false);
                _view.transform.Find("MainSize/Healthbar_Parent")?.gameObject.SetActive(false);
                var rect = (RectTransform)_view.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                rect.localScale = Vector3.one;
                if (
                    !Services.TryGet<CollectionManager>(out var collection)
                    || collection?.defaultCarpet == null
                )
                    throw new InvalidOperationException("Native default carpet is unavailable.");
                var carpet = await NativeGlobalAssetLoader.LoadByReferenceAsync<Texture2D>(
                    loader,
                    collection.defaultCarpet.mainImageReference
                );
                if (carpet == null)
                    throw new InvalidOperationException(
                        "Native default carpet texture is unavailable."
                    );
                if (_disposed || generation != _generation)
                {
                    ClearView();
                    return;
                }
                _view._carpetImage.texture = carpet;
                if (_view._skillParent.TryGetComponent<HorizontalLayoutGroup>(out var skillLayout))
                {
                    _view._skillSpacing = skillLayout.spacing;
                    _view._skillHeight = _view._skillParent.sizeDelta.y;
                    skillLayout.enabled = false;
                }
                Exception? partialFailure = null;
                var missingCount = 0;
                foreach (var instance in items.Cast<TCardInstance>().Concat(skills))
                {
                    if (_disposed || generation != _generation)
                    {
                        ClearView();
                        return;
                    }
                    try
                    {
                        await AddOwnedCard(instance);
                    }
                    catch (Exception error)
                    {
                        partialFailure ??= error;
                        missingCount++;
                    }
                }
                if (_disposed || generation != _generation)
                {
                    ClearView();
                    return;
                }
                RegisterTooltips();
                _view.Show(0f);
                _view._visibilitySequence?.Complete();
                // Keep native skill visuals and pooling, but own their layout independently.
                _ownedSkillParent = _view._skillParent;
                _ownedSkillParent.SetParent(_skillContent, false);
                _ownedSkillParent.anchorMin = _ownedSkillParent.anchorMax = new Vector2(0, .5f);
                _skillContent.anchoredPosition = Vector2.zero;
                _skillScroll.horizontalNormalizedPosition = 0;
                _ready = true;
                _fitDirty = true;
                _fitFrames = 2;
                Fit();
                _gate.alpha = _region.rect.width > 1 && _region.rect.height > 1 ? 1 : 0;
                _gate.blocksRaycasts = true;
                _report(
                    _view._activeCards.Count + _view._activeSkills.Count == 0
                        ? items.Count + skills.Count == 0
                            ? NativeMonsterBoardStatus.Empty
                            : NativeMonsterBoardStatus.Failed
                        : partialFailure != null
                            ? NativeMonsterBoardStatus.Partial
                            : NativeMonsterBoardStatus.Ready,
                    partialFailure == null
                        ? null
                        : new NativeBoardPartialFailure(missingCount, partialFailure)
                );
            }
            catch (Exception ex)
            {
                ClearView();
                if (_disposed || generation != _generation)
                    return;
                if (attempt == 2)
                {
                    // Failed requests are retryable when the same battle is selected again.
                    _signature = null;
                    _report(NativeMonsterBoardStatus.Failed, ex);
                    return;
                }
                await Task.Delay(500 * (attempt + 1));
                if (_disposed || generation != _generation)
                    return;
                continue;
            }
            return;
        }
    }

    private async Task AddOwnedCard(TCardInstance instance)
    {
        if (_missingTemplates.Contains(instance.TemplateId))
            throw new InvalidOperationException($"Template unavailable: {instance.TemplateId}.");
        var template = BppStaticDataAccess.GetCardTemplate(_catalog, instance.TemplateId);
        if (template == null)
        {
            if (_catalog != null)
                _missingTemplates.Add(instance.TemplateId);
            throw new InvalidOperationException($"Template unavailable: {instance.TemplateId}.");
        }
        var parent = instance is TCardInstanceItem item
            ? item.SocketId.HasValue
            && (int)item.SocketId.Value >= 0
            && (int)item.SocketId.Value < _view!._sockets.Length
                ? _view!._sockets[(int)item.SocketId.Value]
                : _view!._carpetImage.transform
            : _view!._skillParent;
        var prefab = await NativeCardPrefabLoader.LoadPrefabAsync(template);
        if (prefab == null)
            throw new InvalidOperationException("Native card prefab is unavailable.");
        var root = await NativeCardPrefabLoader.RentInactiveAsync(template, parent);
        if (root == null)
            throw new InvalidOperationException("Native card rental failed.");
        if (!root.TryGetComponent<CardPreviewBase>(out var card))
        {
            UnityEngine.Object.Destroy(root);
            throw new InvalidOperationException("Native card component is unavailable.");
        }
        var rental = new NativeBoardCardRental(card, prefab);
        _rentals.Add(rental);
        try
        {
            await rental.Prepare(template, instance);
            if (card is CardPreviewItem itemCard)
                _view!._activeCards.Add(itemCard);
            else
                _view!._activeSkills.Add(card);
        }
        catch
        {
            rental.Dispose();
            throw;
        }
    }

    internal void Fit()
    {
        if (
            !_disposed
            && _lastItems != null
            && _lastSkills != null
            && !ReferenceEquals(BppStaticDataAccess.TryGetReadyManagerObject(), _catalog)
        )
            Render(_lastRequest!, _lastItems, _lastSkills);
        if (!_fitDirty && _fitFrames <= 0)
            return;
        if (!_ready || _view == null || _region.rect.width <= 1 || _region.rect.height <= 1)
            return;
        _fitDirty = false;
        _fitFrames--;
        GeometryVersion++;
        var rect = (RectTransform)_view.transform;
        // Fit the carpet itself. Recursive bounds include card frames and stat gems,
        // whose asymmetric overhang moves the center differently for each lineup.
        _view._carpetImage.rectTransform.GetWorldCorners(_carpetCorners);
        var boardBounds = new Bounds(rect.InverseTransformPoint(_carpetCorners[0]), Vector3.zero);
        for (var i = 1; i < _carpetCorners.Length; i++)
            boardBounds.Encapsulate(rect.InverseTransformPoint(_carpetCorners[i]));
        var skillBounds =
            _ownedSkillParent != null
                ? RectTransformUtility.CalculateRelativeRectTransformBounds(_ownedSkillParent)
                : new Bounds(Vector3.zero, Vector3.one);
        var layout = NativeMonsterBoardLayout.Calculate(
            _region.rect.width,
            _region.rect.height,
            boardBounds.size.x,
            boardBounds.size.y,
            skillBounds.size.x,
            skillBounds.size.y,
            _itemsOnly,
            _itemFooterHeight,
            _itemTopInset
        );
        rect.localScale = Vector3.one * layout.BoardScale;
        rect.anchoredPosition = -(Vector2)boardBounds.center * layout.BoardScale;
        _skillViewport.gameObject.SetActive(!_itemsOnly && _view._activeSkills.Count > 0);
        _skillContent.sizeDelta = new Vector2(layout.SkillContentWidth, 0);
        if (_ownedSkillParent != null)
        {
            _ownedSkillParent.localScale = Vector3.one * layout.SkillScale;
            var x =
                layout.SkillContentWidth <= _region.rect.width
                    ? (_region.rect.width - skillBounds.size.x * layout.SkillScale) * .5f
                    : 0;
            _ownedSkillParent.anchoredPosition = new Vector2(
                x - skillBounds.min.x * layout.SkillScale,
                -skillBounds.center.y * layout.SkillScale
            );
        }
    }

    private void ClearView()
    {
        _ready = false;
        if (_view == null)
            return;
        _view._visibilitySequence?.Kill();
        EndPointer();
        foreach (var rental in _rentals)
            rental.Dispose();
        _rentals.Clear();
        _view._activeCards.Clear();
        _view._activeSkills.Clear();
        if (_ownedSkillParent != null)
            UnityEngine.Object.Destroy(_ownedSkillParent.gameObject);
        _ownedSkillParent = null;
        UnityEngine.Object.Destroy(_view.gameObject);
        _view = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _generation++;
        EndPointer();
        _gate.alpha = 0;
        _gate.blocksRaycasts = false;
        // The owner may destroy its UI this frame. Keep native loading alive until
        // all rented cards are registered and can safely be detached and pooled.
        _root.transform.SetParent(null, false);
        _ = DisposeAfterLoad();
    }

    private async Task DisposeAfterLoad()
    {
        await _pending;
        ClearView();
        UnityEngine.Object.Destroy(_root);
    }
}
