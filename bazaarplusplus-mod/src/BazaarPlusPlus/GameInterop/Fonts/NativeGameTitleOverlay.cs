#nullable enable
using TMPro;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.GameInterop.Fonts;

/// <summary>
/// Renders a UI Toolkit panel title through a selected native TMP primary while the transparent
/// UI Toolkit label remains the layout anchor. UI Toolkit cannot consume the packaged static TMP
/// assets directly because they have no source <see cref="Font"/>.
/// </summary>
internal sealed class NativeGameTitleOverlay : IDisposable
{
    private const int OverlayLayer = 30;

    private readonly GameObject _root;
    private readonly RectTransform _rootRect;
    private readonly RectTransform _titleRect;
    private readonly CanvasGroup _canvasGroup;
    private readonly TextMeshProUGUI _title;
    private readonly float _fontSizePoints;
    private NativeGameTypography.OwnedTextRole _role;
    private VisualElement? _layoutAnchor;
    private float _requestedAlpha = 1f;
    private bool _hasValidBounds;
    private bool _disposed;

    private NativeGameTitleOverlay(
        string rootName,
        Transform parent,
        int sortingOrder,
        float fontSizePoints,
        Color color,
        NativeGameTypography.OwnedTextPreparation typography,
        NativeGameTypography.OwnedTextRole role
    )
    {
        _fontSizePoints = fontSizePoints;
        _role = role;
        _root = new GameObject(
            rootName,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasGroup)
        );
        _root.layer = OverlayLayer;
        _root.transform.SetParent(parent, worldPositionStays: false);
        _root.SetActive(false);

        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;
        canvas.pixelPerfect = false;

        _canvasGroup = _root.GetComponent<CanvasGroup>();
        _canvasGroup.alpha = 1f;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;

        _rootRect = _root.GetComponent<RectTransform>();
        _rootRect.anchorMin = Vector2.zero;
        _rootRect.anchorMax = Vector2.zero;
        _rootRect.pivot = Vector2.zero;
        _rootRect.anchoredPosition = Vector2.zero;
        _rootRect.localScale = Vector3.one;

        var titleObject = new GameObject(
            $"{rootName}Text",
            typeof(RectTransform),
            typeof(CanvasRenderer)
        );
        titleObject.layer = OverlayLayer;
        titleObject.transform.SetParent(_root.transform, worldPositionStays: false);
        _titleRect = titleObject.GetComponent<RectTransform>();
        _titleRect.anchorMin = Vector2.zero;
        _titleRect.anchorMax = Vector2.zero;
        _titleRect.pivot = Vector2.zero;
        _titleRect.localScale = Vector3.one;

        _title = titleObject.AddComponent<TextMeshProUGUI>();
        if (typography.Apply(_title) != NativeGameTypography.Outcome.Applied)
        {
            Object.Destroy(_root);
            throw new InvalidOperationException("Native game title typography became unavailable.");
        }
        _title.fontStyle = FontStyles.Normal;
        _title.alignment = TextAlignmentOptions.MidlineLeft;
        _title.textWrappingMode = TextWrappingModes.NoWrap;
        // TMP's Ellipsis mode can cache an empty mesh when this inactive overlay still has its
        // default 200x50 rect. Masking keeps the title bounded without persisting that first-frame
        // truncation after the real UI Toolkit geometry arrives.
        _title.overflowMode = TextOverflowModes.Masking;
        _title.richText = false;
        _title.raycastTarget = false;
        _title.color = color;
    }

    internal static bool TryCreate(
        string rootName,
        Transform parent,
        int sortingOrder,
        float fontSizePoints,
        Color color,
        out NativeGameTitleOverlay? overlay
    ) =>
        TryCreate(
            rootName,
            parent,
            sortingOrder,
            fontSizePoints,
            color,
            NativeGameTypography.OwnedTextRole.Heading,
            out overlay
        );

    internal static bool TryCreate(
        string rootName,
        Transform parent,
        int sortingOrder,
        float fontSizePoints,
        Color color,
        NativeGameTypography.OwnedTextRole role,
        out NativeGameTitleOverlay? overlay
    )
    {
        overlay = null;
        if (
            NativeGameTypography.PrepareOwnedText(role, out var typography)
                != NativeGameTypography.Outcome.Ready
            || typography == null
        )
            return false;

        try
        {
            overlay = new NativeGameTitleOverlay(
                rootName,
                parent,
                sortingOrder,
                fontSizePoints,
                color,
                typography,
                role
            );
            return true;
        }
        catch
        {
            overlay?.Dispose();
            overlay = null;
            return false;
        }
    }

    internal void Attach(VisualElement layoutAnchor)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(NativeGameTitleOverlay));
        if (layoutAnchor == null)
            throw new ArgumentNullException(nameof(layoutAnchor));

        Detach();
        _layoutAnchor = layoutAnchor;
        _layoutAnchor.pickingMode = PickingMode.Ignore;
        _layoutAnchor.style.opacity = 0f;
        _layoutAnchor.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        SyncBounds();
    }

    internal void SetText(string? text)
    {
        if (_disposed)
            return;

        _title.text = text ?? string.Empty;
        var desiredRole = UnicodeFontCoverage.ContainsCjk(_title.text)
            ? NativeGameTypography.OwnedTextRole.Body
            : NativeGameTypography.OwnedTextRole.Heading;
        if (
            desiredRole != _role
            && NativeGameTypography.PrepareOwnedText(desiredRole, out var typography)
                == NativeGameTypography.Outcome.Ready
            && typography != null
        )
        {
            if (typography.Apply(_title) == NativeGameTypography.Outcome.Applied)
                _role = desiredRole;
        }
        if (!string.IsNullOrEmpty(_title.text))
        {
            // The native locale fallbacks populate glyphs lazily. Resolve coverage and preferred
            // values before the forced rebuild so an all-CJK title cannot produce a zero-vertex
            // mesh on the first clean game launch.
            _title.font?.HasCharacters(
                _title.text,
                out _,
                searchFallbacks: true,
                tryAddCharacter: true
            );
            _title.GetPreferredValues(_title.text);
        }
        _title.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
    }

    internal void SetVisible(bool visible)
    {
        if (_disposed)
            return;
        _root.SetActive(visible);
        if (visible)
        {
            // The layout anchor normally starts under a display:none panel. Its first Attach-time
            // SyncBounds therefore has no usable geometry, and ancestor display changes do not
            // reliably emit a GeometryChangedEvent for the transparent child. Retry after the
            // next UI Toolkit layout pass so the native title cannot remain permanently hidden.
            SyncBounds();
            _layoutAnchor?.schedule.Execute(SyncBounds);
        }
        _canvasGroup.alpha = visible && _hasValidBounds ? _requestedAlpha : 0f;
    }

    internal void SetAlpha(float alpha)
    {
        if (_disposed)
            return;
        _requestedAlpha = Mathf.Clamp01(alpha);
        if (_root.activeSelf && !_hasValidBounds)
            SyncBounds();
        _canvasGroup.alpha = _root.activeSelf && _hasValidBounds ? _requestedAlpha : 0f;
    }

    private void OnGeometryChanged(GeometryChangedEvent evt) => SyncBounds();

    private void SyncBounds()
    {
        if (_disposed || _layoutAnchor?.panel == null)
            return;

        var worldBound = _layoutAnchor.worldBound;
        if (
            !IsFinite(worldBound.x)
            || !IsFinite(worldBound.y)
            || !IsFinite(worldBound.width)
            || !IsFinite(worldBound.height)
            || worldBound.width <= 0f
            || worldBound.height <= 0f
        )
            return;
        var pixelsPerPoint = _layoutAnchor.scaledPixelsPerPoint;
        if (!IsFinite(pixelsPerPoint) || pixelsPerPoint <= 0f)
            return;
        _rootRect.sizeDelta = new Vector2(
            Mathf.Max(1f, Screen.width),
            Mathf.Max(1f, Screen.height)
        );
        _titleRect.anchoredPosition = new Vector2(
            Mathf.Round(worldBound.x * pixelsPerPoint),
            Mathf.Round(Screen.height - worldBound.yMax * pixelsPerPoint)
        );
        _titleRect.sizeDelta = new Vector2(
            Mathf.Max(1f, Mathf.Ceil(worldBound.width * pixelsPerPoint)),
            Mathf.Max(1f, Mathf.Ceil(worldBound.height * pixelsPerPoint))
        );
        _title.fontSize = _fontSizePoints * pixelsPerPoint;
        _hasValidBounds = true;
        _canvasGroup.alpha = _root.activeSelf ? _requestedAlpha : 0f;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private void Detach()
    {
        if (_layoutAnchor == null)
            return;
        _layoutAnchor.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        _layoutAnchor = null;
        _hasValidBounds = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Detach();
        if (_root != null)
            Object.Destroy(_root);
    }
}
