#nullable enable
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Native CardPreviewBase instances need a sibling ScreenSpaceOverlay canvas above the UITK viewport.
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
    private Image? _topOccluder;
    private Image? _bottomOccluder;

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
            && _topOccluder != null
            && _bottomOccluder != null
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
        _canvas.sortingOrder = CollectionGridHostConstants.OverlaySortingOrder;
        _canvas.pixelPerfect = false;

        _canvasGroup = _root.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;

        if (!CollectionGridHostConstants.UsePolledHover)
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

        // Native card shaders ignore RectMask2D, so foreground panels cover vertical overflow while the mask clips standard UI Images.
        _bottomOccluder = CreateVerticalOccluder("CollectionPanelOverlayBottomOccluder");
        _topOccluder = CreateVerticalOccluder("CollectionPanelOverlayTopOccluder");

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
            _topOccluder = null;
            _bottomOccluder = null;
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

        // The board's top-left pivot makes downward cell offsets negative in Unity.
        _boardRect.anchorMin = new Vector2(0f, 1f);
        _boardRect.anchorMax = new Vector2(0f, 1f);
        _boardRect.pivot = new Vector2(0f, 1f);
        _boardRect.anchoredPosition = Vector2.zero;
        _boardRect.sizeDelta = _clipSize;
        _boardRect.localScale = Vector3.one;

        LayoutVerticalOccluders();
    }

    private Image CreateVerticalOccluder(string name)
    {
        var occluderObject = new GameObject(name, typeof(RectTransform));
        occluderObject.layer = _layer;
        occluderObject.transform.SetParent(_root!.transform, worldPositionStays: false);
        var image = occluderObject.AddComponent<Image>();
        image.color = InGameTheme.NeutralGround;
        image.raycastTarget = false;
        return image;
    }

    private void LayoutVerticalOccluders()
    {
        if (_topOccluder == null || _bottomOccluder == null)
            return;

        var screenHeight = Mathf.Max(1f, Screen.height);
        var viewportBottom = Mathf.Clamp(_position.y, 0f, screenHeight);
        var viewportTop = Mathf.Clamp(_position.y + _clipSize.y, 0f, screenHeight);
        LayoutOccluder(
            _bottomOccluder,
            new Vector2(_position.x, 0f),
            new Vector2(_clipSize.x, viewportBottom)
        );
        LayoutOccluder(
            _topOccluder,
            new Vector2(_position.x, viewportTop),
            new Vector2(_clipSize.x, screenHeight - viewportTop)
        );
    }

    private static void LayoutOccluder(Image image, Vector2 position, Vector2 size)
    {
        var visible = size.x > 0.5f && size.y > 0.5f;
        image.gameObject.SetActive(visible);
        if (!visible)
            return;

        var rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        rect.SetAsLastSibling();
    }
}
