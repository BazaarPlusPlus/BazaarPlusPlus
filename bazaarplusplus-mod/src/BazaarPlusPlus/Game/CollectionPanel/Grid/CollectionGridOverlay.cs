#nullable enable
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Sibling ScreenSpaceOverlay canvas where native CardPreviewBase instances are parented
// and clipped above the UITK panel. The UITK panel publishes a
// pixel-space rect each time its grid viewport's geometry changes; this overlay reapplies
// that rect to its clip RectTransform so the grid scrolls underneath the same hole that
// the UITK viewport opens.
//
// Mirrors item-board preview overlay scaffolding (sortingOrder, RectMask2D, ApplyTransform
// math). A GraphicRaycaster is added only when the raycaster-hover dispatch path is
// selected via CollectionGridConstants.UsePolledHover = false; under the default
// (polled hover) the overlay is purely visual and the lower UITK panel receives every
// click / wheel uninterrupted.
internal sealed class CollectionGridOverlay
{
    public const int DefaultLayer = 30;

    private readonly int _layer;
    private GameObject? _root;
    private Canvas? _canvas;
    private CanvasGroup? _canvasGroup;
    private RectTransform? _rootRect;
    private RectTransform? _clipRect;
    private RectTransform? _boardRect;

    private Vector2 _position = Vector2.zero;
    private Vector2 _clipSize = new(1f, 1f);

    public CollectionGridOverlay(int layer = DefaultLayer)
    {
        _layer = layer;
    }

    public RectTransform? BoardRoot => _boardRect;

    public bool EnsureInitialized()
    {
        if (
            _root != null
            && _canvas != null
            && _rootRect != null
            && _clipRect != null
            && _boardRect != null
        )
        {
            ApplyTransform();
            return true;
        }

        Dispose();

        _root = new GameObject("CollectionPanelOverlayRoot", typeof(RectTransform), typeof(Canvas));
        _root.layer = _layer;
        _root.SetActive(false);

        _canvas = _root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.overrideSorting = true;
        _canvas.sortingOrder = CollectionGridConstants.OverlaySortingOrder;
        _canvas.pixelPerfect = false;

        // CanvasGroup at the overlay root lets the panel cross-fade the entire card layer
        // in sync with the UITK panel's opacity transition. Starts at 0 because the panel
        // is created hidden; CollectionPanel.SetAlpha pushes the live value each frame.
        _canvasGroup = _root.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;

        // Only attach a GraphicRaycaster when the raycaster-hover path is selected. Under
        // the default (polled hover) the overlay is purely visual: the card prefab's
        // RawImage has raycastTarget=true, so a raycaster here would silently swallow
        // every wheel event landing on a card and freeze ScrollView scrolling.
        if (!CollectionGridConstants.UsePolledHover)
            _root.AddComponent<GraphicRaycaster>();

        _rootRect = _root.GetComponent<RectTransform>();

        var clipObject = new GameObject(
            "CollectionPanelOverlayClip",
            typeof(RectTransform),
            typeof(RectMask2D)
        );
        clipObject.layer = _layer;
        clipObject.transform.SetParent(_root.transform, worldPositionStays: false);
        _clipRect = clipObject.GetComponent<RectTransform>();

        var boardObject = new GameObject("CollectionPanelOverlayBoard", typeof(RectTransform));
        boardObject.layer = _layer;
        boardObject.transform.SetParent(_clipRect, worldPositionStays: false);
        _boardRect = boardObject.GetComponent<RectTransform>();

        ApplyTransform();
        return true;
    }

    public void SetVisible(bool visible)
    {
        if (_root != null)
            _root.SetActive(visible);
    }

    public void SetAlpha(float alpha)
    {
        if (_canvasGroup != null)
            _canvasGroup.alpha = Mathf.Clamp01(alpha);
    }

    public void SetPosition(Vector2 position)
    {
        var rounded = new Vector2(Mathf.Round(position.x), Mathf.Round(position.y));
        if (
            Mathf.Approximately(_position.x, rounded.x)
            && Mathf.Approximately(_position.y, rounded.y)
        )
            return;

        _position = rounded;
        ApplyTransform();
    }

    public void SetClipSize(Vector2 size)
    {
        var rounded = new Vector2(
            Mathf.Max(1f, Mathf.Round(size.x)),
            Mathf.Max(1f, Mathf.Round(size.y))
        );
        if (
            Mathf.Approximately(_clipSize.x, rounded.x)
            && Mathf.Approximately(_clipSize.y, rounded.y)
        )
            return;

        _clipSize = rounded;
        ApplyTransform();
    }

    public void Dispose()
    {
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
            _canvas = null;
            _canvasGroup = null;
            _rootRect = null;
            _clipRect = null;
            _boardRect = null;
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

        // Board: pinned to clip top-left with pivot at top-left, so anchoredPosition (x, y)
        // for cells uses (right of origin, down from origin). y values are negative.
        _boardRect.anchorMin = new Vector2(0f, 1f);
        _boardRect.anchorMax = new Vector2(0f, 1f);
        _boardRect.pivot = new Vector2(0f, 1f);
        _boardRect.anchoredPosition = Vector2.zero;
        _boardRect.sizeDelta = _clipSize;
        _boardRect.localScale = Vector3.one;
    }
}
