#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarPlusPlus.GameInterop.CardPreview;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal sealed class ItemBoardPreviewSurface : IDisposable
{
    private readonly ItemBoardPreviewGenerationGuard _generation = new();
    private readonly List<NativeCardPreviewHandle> _active = new();
    private readonly List<Task> _activeSetUpTasks = new();

    private ItemBoardPreviewOptions _options = new();
    private GameObject? _root;
    private Canvas? _canvas;
    private CanvasGroup? _canvasGroup;
    private RectTransform? _rootRect;
    private RectTransform? _clipRect;
    private RectTransform? _boardRect;
    private RectTransform[]? _sockets;
    private NativeCardPreviewFactory? _factory;
    private NativeCardPreviewHoverRelay? _hoverRelay;
    private NativeCardPreviewHandle? _hoveredHandle;
    private string? _renderedSignature;
    private Vector2 _position = Vector2.zero;
    private Vector2 _clipSize = new(
        ItemBoardSocketLayout.NativeBoardWidth,
        ItemBoardSocketLayout.NativeBoardHeight
    );
    private float _cardScale = 1f;
    private int _runtimeLayer = int.MinValue;

    public bool EnsureInitialized()
    {
        return EnsureInitialized(_options);
    }

    public void SetPosition(Vector2 screenBottomLeft)
    {
        var rounded = new Vector2(Mathf.Round(screenBottomLeft.x), Mathf.Round(screenBottomLeft.y));
        if (
            Mathf.Approximately(_position.x, rounded.x)
            && Mathf.Approximately(_position.y, rounded.y)
        )
        {
            return;
        }

        _position = rounded;
        ApplyTransform();
    }

    public void SetClipSize(Vector2 pixels)
    {
        var rounded = new Vector2(
            Mathf.Max(1f, Mathf.Round(pixels.x)),
            Mathf.Max(1f, Mathf.Round(pixels.y))
        );
        if (
            Mathf.Approximately(_clipSize.x, rounded.x)
            && Mathf.Approximately(_clipSize.y, rounded.y)
        )
        {
            return;
        }

        _clipSize = rounded;
        ApplyTransform();
        _renderedSignature = null;
    }

    public bool SetCardScale(float scale)
    {
        var clamped = Mathf.Max(0.05f, scale);
        if (Mathf.Approximately(_cardScale, clamped))
            return false;

        _cardScale = clamped;
        ApplyTransform();
        _renderedSignature = null;
        return true;
    }

    public IEnumerator Render(
        IReadOnlyList<NativeCardPreviewSpec>? cards,
        ItemBoardPreviewOptions options,
        string? signature = null,
        Action<ItemBoardPreviewPhase>? onPhase = null,
        Action? onComplete = null
    )
    {
        _options = options ?? new ItemBoardPreviewOptions();
        var snapshot = _generation.Bump();
        DispatchHoverOut();

        if (cards == null || cards.Count == 0)
        {
            onPhase?.Invoke(ItemBoardPreviewPhase.Empty);
            Hide();
            onComplete?.Invoke();
            yield break;
        }

        if (!EnsureInitialized(_options))
        {
            onPhase?.Invoke(ItemBoardPreviewPhase.InitFailed);
            onComplete?.Invoke();
            yield break;
        }

        if (
            !string.IsNullOrEmpty(_renderedSignature)
            && !string.IsNullOrEmpty(signature)
            && string.Equals(_renderedSignature, signature, StringComparison.Ordinal)
        )
        {
            onPhase?.Invoke(ItemBoardPreviewPhase.Done);
            onComplete?.Invoke();
            yield break;
        }

        onPhase?.Invoke(ItemBoardPreviewPhase.Loading);
        _root!.SetActive(true);
        if (_canvasGroup != null)
            _canvasGroup.alpha = 1f;

        ApplyTransform();
        ReturnActiveCardsToPool();
        _activeSetUpTasks.Clear();
        SpawnCards(cards);
        if (_active.Count == 0)
        {
            onPhase?.Invoke(ItemBoardPreviewPhase.Empty);
            Hide();
            onComplete?.Invoke();
            yield break;
        }

        var aggregate = Task.WhenAll(_activeSetUpTasks);
        while (!aggregate.IsCompleted && _generation.IsCurrent(snapshot))
            yield return null;

        if (!_generation.IsCurrent(snapshot))
            yield break;

        ShowSetUpCards();
        Canvas.ForceUpdateCanvases();

        yield return null;
        if (!_generation.IsCurrent(snapshot))
            yield break;

        Canvas.ForceUpdateCanvases();
        if (_options.LayoutMode == ItemBoardPreviewLayoutMode.SlotGrid)
            LayoutCardsSlotGrid();
        else if (_options.LayoutMode == ItemBoardPreviewLayoutMode.Packed)
            LayoutCardsPacked();

        if (ItemBoardPreviewSignatureGate.ShouldCache(aggregate))
            _renderedSignature = signature;

        onPhase?.Invoke(ItemBoardPreviewPhase.Done);
        onComplete?.Invoke();
    }

    public void PollHover(Vector2 mousePixels)
    {
        if (!_options.ShowHover || _root == null || !_root.activeSelf || _clipRect == null)
        {
            DispatchHoverOut();
            return;
        }

        var clipBounds = new Rect(_position.x, _position.y, _clipSize.x, _clipSize.y);
        if (!clipBounds.Contains(mousePixels))
        {
            DispatchHoverOut();
            return;
        }

        var next = FindHoveredHandle(mousePixels);
        if (ReferenceEquals(next, _hoveredHandle))
            return;

        DispatchHoverOut();
        if (next == null)
            return;

        _hoveredHandle = next;
        _hoverRelay ??= new NativeCardPreviewHoverRelay(_options.LogComponent);
        _hoverRelay.Bind(next.Card);
        _hoverRelay.InvokeHover();
    }

    public void Hide()
    {
        CancelPending();
        _renderedSignature = null;
        ReturnActiveCardsToPool();
        if (_canvasGroup != null)
            _canvasGroup.alpha = 0f;
        if (_root != null)
            _root.SetActive(false);
    }

    public void CancelPending()
    {
        _generation.Bump();
        DispatchHoverOut();
    }

    public void Dispose()
    {
        CancelPending();
        _renderedSignature = null;
        ReturnActiveCardsToPool();
        _factory?.DestroyAll();
        _factory = null;
        _sockets = null;

        if (_root != null)
            Object.Destroy(_root);

        _root = null;
        _canvas = null;
        _canvasGroup = null;
        _rootRect = null;
        _clipRect = null;
        _boardRect = null;
        _hoverRelay = null;
        _runtimeLayer = int.MinValue;
    }

    private bool EnsureInitialized(ItemBoardPreviewOptions options)
    {
        if (NativeCardPreviewReflection.CardPreviewBaseType == null)
            return false;

        if (_runtimeLayer != int.MinValue && _runtimeLayer != options.Layer)
            DisposeRuntimeObjects();

        _runtimeLayer = options.Layer;
        _factory ??= new NativeCardPreviewFactory(options.Layer, options.LogComponent);
        _hoverRelay ??= new NativeCardPreviewHoverRelay(options.LogComponent);

        if (
            _root != null
            && _canvas != null
            && _rootRect != null
            && _clipRect != null
            && _boardRect != null
            && _sockets != null
        )
        {
            ApplyOptions(options);
            ApplyTransform();
            return _factory.EnsureReady(requireSkill: false);
        }

        DisposeRuntimeObjects();
        _runtimeLayer = options.Layer;
        _factory = new NativeCardPreviewFactory(options.Layer, options.LogComponent);
        _hoverRelay = new NativeCardPreviewHoverRelay(options.LogComponent);

        if (!_factory.EnsureReady(requireSkill: false))
        {
            DisposeRuntimeObjects();
            return false;
        }

        _root = new GameObject("BppItemBoardPreviewSurface", typeof(RectTransform), typeof(Canvas));
        _root.layer = options.Layer;
        _root.SetActive(false);

        _canvas = _root.GetComponent<Canvas>();
        _rootRect = _root.GetComponent<RectTransform>();
        ApplyOptions(options);

        var clipObject = new GameObject(
            "BppItemBoardPreviewClip",
            typeof(RectTransform),
            typeof(RectMask2D)
        );
        clipObject.layer = options.Layer;
        clipObject.transform.SetParent(_root.transform, worldPositionStays: false);
        _clipRect = clipObject.GetComponent<RectTransform>();

        var boardObject = new GameObject("BppItemBoardPreviewBoard", typeof(RectTransform));
        boardObject.layer = options.Layer;
        boardObject.transform.SetParent(_clipRect, worldPositionStays: false);
        _boardRect = boardObject.GetComponent<RectTransform>();
        _sockets = ItemBoardSocketLayout.BuildSockets(
            _boardRect,
            options.Layer,
            "BppItemBoardPreviewSocket"
        );

        ApplyTransform();
        return true;
    }

    private void ApplyOptions(ItemBoardPreviewOptions options)
    {
        if (_canvas != null)
        {
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = options.SortingOrder;
            _canvas.pixelPerfect = false;
        }

        if (!options.UseCanvasGroup)
            return;

        if (_root != null && _canvasGroup == null)
            _canvasGroup = _root.AddComponent<CanvasGroup>();
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = _root?.activeSelf == true ? 1f : 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }
    }

    private void ApplyTransform()
    {
        if (_rootRect == null || _clipRect == null || _boardRect == null)
            return;

        _rootRect.anchorMin = Vector2.zero;
        _rootRect.anchorMax = Vector2.zero;
        _rootRect.pivot = Vector2.zero;
        _rootRect.anchoredPosition = Vector2.zero;
        _rootRect.sizeDelta = new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
        _rootRect.localScale = Vector3.one;

        _clipRect.anchorMin = Vector2.zero;
        _clipRect.anchorMax = Vector2.zero;
        _clipRect.pivot = Vector2.zero;
        _clipRect.anchoredPosition = _position;
        _clipRect.sizeDelta = _clipSize;
        _clipRect.localScale = Vector3.one;

        if (_options.LayoutMode == ItemBoardPreviewLayoutMode.SlotGrid)
        {
            _boardRect.anchorMin = Vector2.zero;
            _boardRect.anchorMax = Vector2.one;
            _boardRect.pivot = new Vector2(0.5f, 0.5f);
            _boardRect.anchoredPosition = Vector2.zero;
            _boardRect.sizeDelta = Vector2.zero;
            _boardRect.localScale = Vector3.one;
            return;
        }

        _boardRect.anchorMin = new Vector2(0.5f, 0.5f);
        _boardRect.anchorMax = new Vector2(0.5f, 0.5f);
        _boardRect.pivot = new Vector2(0.5f, 0.5f);
        _boardRect.anchoredPosition = Vector2.zero;
        _boardRect.sizeDelta = new Vector2(
            ItemBoardSocketLayout.NativeBoardWidth,
            ItemBoardSocketLayout.NativeBoardHeight
        );
        _boardRect.localScale = Vector3.one * _cardScale;
    }

    private void SpawnCards(IReadOnlyList<NativeCardPreviewSpec> cards)
    {
        if (_factory == null || _sockets == null)
            return;

        var fallbackIndex = 0;
        foreach (var spec in cards)
        {
            if (!_factory.TryResolveSpan(spec, out var span))
                continue;

            var socketIndex = ItemBoardSocketResolver.ResolveIndex(
                _sockets.Length,
                spec.SocketId.HasValue ? (int)spec.SocketId.Value : (int?)null,
                fallbackIndex,
                span
            );
            if (socketIndex < 0)
                continue;

            var handle = _factory.TryCreate(spec, _sockets[socketIndex], fallbackIndex);
            if (handle == null)
                continue;

            _active.Add(handle);
            _activeSetUpTasks.Add(handle.SetUpTask);
            fallbackIndex++;
        }
    }

    private void ShowSetUpCards()
    {
        if (_factory == null)
            return;

        foreach (var handle in _active)
        {
            if (handle.SetUpTask.IsCompletedSuccessfully)
                _factory.Show(handle);
        }
    }

    private void LayoutCardsPacked()
    {
        if (_boardRect == null || _active.Count == 0)
            return;

        var corners = new Vector3[4];
        var laid = new List<(Transform card, float frameLeft, float frameWidth)>();
        foreach (var handle in _active)
        {
            if (!handle.SetUpTask.IsCompletedSuccessfully || handle.Card == null || handle.Rect == null)
                continue;

            var root = handle.Rect;
            var frame = FindDescendant(root, "FrameContainer") ?? root;
            frame.GetWorldCorners(corners);
            laid.Add((handle.Card.transform, corners[0].x, corners[2].x - corners[0].x));
        }

        if (laid.Count == 0)
            return;

        laid.Sort((a, b) => a.frameLeft.CompareTo(b.frameLeft));

        var totalFrameWidth = 0f;
        foreach (var entry in laid)
            totalFrameWidth += entry.frameWidth;

        var cursor = _boardRect.position.x - totalFrameWidth * 0.5f;
        foreach (var entry in laid)
        {
            entry.card.position += new Vector3(cursor - entry.frameLeft, 0f, 0f);
            cursor += entry.frameWidth;
        }
    }

    private void LayoutCardsSlotGrid()
    {
        if (_active.Count == 0)
            return;

        var corners = new Vector3[4];
        foreach (var handle in _active)
        {
            if (!handle.SetUpTask.IsCompletedSuccessfully || handle.Card == null || handle.Rect == null)
                continue;
            if (!handle.Card.gameObject.activeInHierarchy)
                continue;

            var frame = FindDescendant(handle.Rect, "FrameContainer") ?? handle.Rect;
            frame.GetWorldCorners(corners);
            var frameWidth = corners[2].x - corners[0].x;
            var frameHeight = corners[2].y - corners[0].y;
            if (frameWidth <= 0f || frameHeight <= 0f)
                continue;

            var socketIndex = handle.Spec.SocketId.HasValue ? (int)handle.Spec.SocketId.Value : 0;
            var occupied = ItemBoardSlotGridGeometry.ResolveOccupiedRect(
                _clipSize.x,
                _clipSize.y,
                socketIndex,
                handle.Spec.DisplaySpan,
                _options.SlotGridHorizontalInsetPixels,
                _options.SlotGridVerticalInsetPixels
            );
            var targetHeight = ItemBoardSlotGridGeometry.ResolveScaledTargetHeight(
                occupied.Height,
                ItemBoardSocketLayout.NativeBoardHeight,
                _cardScale,
                _options.SlotGridMaxHeightRatio
            );
            var scale = ItemBoardSlotGridGeometry.ResolveHeightScale(
                frameHeight,
                targetHeight,
                _options.SlotGridMaxScale
            );

            var cardTransform = handle.Card.transform;
            cardTransform.localScale = new Vector3(
                cardTransform.localScale.x * scale,
                cardTransform.localScale.y * scale,
                cardTransform.localScale.z
            );

            frame.GetWorldCorners(corners);
            var frameCenter = new Vector2(
                (corners[0].x + corners[2].x) * 0.5f,
                (corners[0].y + corners[2].y) * 0.5f
            );
            var targetCenter = new Vector2(
                _position.x + occupied.CenterX,
                _position.y + occupied.CenterY
            );
            cardTransform.position += new Vector3(
                targetCenter.x - frameCenter.x,
                targetCenter.y - frameCenter.y,
                0f
            );
        }
    }

    private NativeCardPreviewHandle? FindHoveredHandle(Vector2 mousePixels)
    {
        var corners = new Vector3[4];
        foreach (var handle in _active)
        {
            if (!handle.SetUpTask.IsCompletedSuccessfully || handle.Card == null || handle.Rect == null)
                continue;
            if (!handle.Card.gameObject.activeInHierarchy)
                continue;

            handle.Rect.GetWorldCorners(corners);
            var rect = new Rect(
                corners[0].x,
                corners[0].y,
                corners[2].x - corners[0].x,
                corners[2].y - corners[0].y
            );
            if (rect.Contains(mousePixels))
                return handle;
        }

        return null;
    }

    private void DispatchHoverOut()
    {
        _hoverRelay?.Clear();
        _hoveredHandle = null;
    }

    private void ReturnActiveCardsToPool()
    {
        DispatchHoverOut();
        if (_factory != null)
        {
            foreach (var handle in _active)
                _factory.Return(handle);
        }

        _active.Clear();
        _activeSetUpTasks.Clear();
    }

    private void DisposeRuntimeObjects()
    {
        ReturnActiveCardsToPool();
        _factory?.DestroyAll();
        _factory = null;
        _sockets = null;

        if (_root != null)
            Object.Destroy(_root);

        _root = null;
        _canvas = null;
        _canvasGroup = null;
        _rootRect = null;
        _clipRect = null;
        _boardRect = null;
        _hoverRelay = null;
    }

    private static RectTransform? FindDescendant(Transform root, string childName)
    {
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
        {
            if (rt != null && rt.name == childName)
                return rt;
        }
        return null;
    }
}
