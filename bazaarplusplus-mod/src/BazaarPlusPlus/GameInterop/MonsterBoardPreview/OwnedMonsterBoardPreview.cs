#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarPlusPlus.Core.GameState;
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
    private SkillParentHome _skillParentHome;
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
                // The shell outlives a replacement; only its cards are re-rented.
                ClearCards();
                var view = _view != null ? _view : await CreateShell(generation);
                if (view == null || _disposed || generation != _generation)
                    return;
                Exception? partialFailure = null;
                var missingCount = 0;
                foreach (var instance in items.Cast<TCardInstance>().Concat(skills))
                {
                    if (_disposed || generation != _generation)
                    {
                        ClearCards();
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
                    ClearCards();
                    return;
                }
                RegisterTooltips();
                view.Show(0f);
                view._visibilitySequence?.Complete();
                // Keep native skill visuals and pooling, but own their layout independently.
                // ClearCards puts the skill parent back before the next replacement.
                _ownedSkillParent = view._skillParent;
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
                    view._activeCards.Count + view._activeSkills.Count == 0
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
                // A failed attempt may leave the shell half-built: retry from a fresh clone.
                DestroyShell();
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

    // Clones the native board and does its one-time setup; the clone then serves every
    // replacement until Dispose or a failed attempt. Null when the request went stale first.
    private async Task<MonsterBoardTooltip?> CreateShell(int generation)
    {
        if (!Services.TryGet<AssetLoader>(out var loader) || loader == null)
            throw new InvalidOperationException("Native asset loader is unavailable.");
        var prefab = await NativeGlobalAssetLoader.LoadByAddressAsync<GameObject>(
            loader,
            TooltipAddress
        );
        if (_disposed || generation != _generation)
            return null;
        var donor =
            prefab != null ? prefab.GetComponentInChildren<MonsterBoardTooltip>(true) : null;
        if (donor == null)
            throw new InvalidOperationException("Native monster board prefab is unavailable.");
        var view = UnityEngine.Object.Instantiate(donor, _boardRegion, false);
        try
        {
            // The tooltip prefab overrides sorting at order zero. Owned previews must
            // inherit this host canvas, or the opaque history panel covers every card.
            foreach (var nestedCanvas in view.GetComponentsInChildren<Canvas>(true))
                nestedCanvas.overrideSorting = false;
            view.gameObject.SetActive(true);
            view._healthText.gameObject.SetActive(false);
            view.transform.Find("MainSize/Healthbar_Parent")?.gameObject.SetActive(false);
            var rect = (RectTransform)view.transform;
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
            view._carpetImage.texture = carpet;
            if (view._skillParent.TryGetComponent<HorizontalLayoutGroup>(out var skillLayout))
            {
                view._skillSpacing = skillLayout.spacing;
                view._skillHeight = view._skillParent.sizeDelta.y;
                skillLayout.enabled = false;
            }
            _skillParentHome = SkillParentHome.Capture(view._skillParent);
        }
        catch
        {
            UnityEngine.Object.Destroy(view.gameObject);
            throw;
        }
        // A stale or disposed request still keeps the finished shell: the next replacement
        // reuses it, and DisposeAfterLoad destroys it once this drain completes.
        _view = view;
        return view;
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

    // Returns every rental and puts the skill parent back where the prefab had it, so the
    // shell is again as CreateShell left it. Rentals settle, restore prefab layout and
    // detach before pooling, because native pooling does not reparent.
    private void ClearCards()
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
            _skillParentHome.Restore(_ownedSkillParent);
        _ownedSkillParent = null;
    }

    private void DestroyShell()
    {
        ClearCards();
        if (_view == null)
            return;
        UnityEngine.Object.Destroy(_view.gameObject);
        _view = null;
    }

    // Where the clone's skill parent sits inside the shell before a render moves it into
    // the owned scroll content.
    private readonly record struct SkillParentHome(
        Transform Parent,
        int SiblingIndex,
        Vector2 AnchorMin,
        Vector2 AnchorMax,
        Vector2 AnchoredPosition,
        Vector3 LocalScale
    )
    {
        internal static SkillParentHome Capture(RectTransform rect) =>
            new(
                rect.parent,
                rect.GetSiblingIndex(),
                rect.anchorMin,
                rect.anchorMax,
                rect.anchoredPosition,
                rect.localScale
            );

        internal void Restore(RectTransform rect)
        {
            rect.SetParent(Parent, false);
            rect.SetSiblingIndex(SiblingIndex);
            rect.anchorMin = AnchorMin;
            rect.anchorMax = AnchorMax;
            rect.anchoredPosition = AnchoredPosition;
            rect.localScale = LocalScale;
        }
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
        DestroyShell();
        UnityEngine.Object.Destroy(_root);
    }
}
