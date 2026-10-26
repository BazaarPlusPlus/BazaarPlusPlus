#nullable enable
using BazaarPlusPlus.Infrastructure.UiTokens;
using DG.Tweening;
using HarmonyLib;
using TheBazaar.UI.Tooltips;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.GameInterop.Tooltips;

// A reversible lease on the native tooltip's canvases. ScreenSpaceOverlay also works when
// the native inspection backdrop originally belongs to a camera rendered below BPP's UI.
internal sealed class NativeInspectionPresentation : IDisposable
{
    private readonly CardTooltipController _controller;
    private readonly List<CanvasLease> _canvases = new();
    private readonly GameObject _root;
    private readonly RectState _tooltipRect;
    private readonly Graphic? _nativeBackground;
    private readonly bool _backgroundEnabled;
    private readonly bool _backgroundRaycast;
    private readonly InspectionBackgroundPress _backgroundPress = new();
    private readonly List<Selectable> _controls = new();
    private readonly List<TMP_Dropdown> _dropdowns = new();
    private readonly List<TMP_Text> _captions = new();
    private readonly List<Graphic> _graphics = new();
    private readonly Vector3[] _corners = new Vector3[4];
    private readonly Action<Object> _onTextChanged;
    private Vector3 _descriptionScale;
    private bool _descriptionChanged;
    private Vector2 _contentSize;
    private Vector2 _tooltipSize;
    private static readonly System.Reflection.FieldInfo? DropdownList = AccessTools.Field(
        typeof(TMP_Dropdown),
        "m_Dropdown"
    );
    private bool _disposed;
    private bool _released;

    public RectTransform CardRoot { get; }

    public NativeInspectionPresentation(CardTooltipController controller)
    {
        _controller = controller;
        _onTextChanged = OnTextChanged;
        _tooltipRect = new RectState(controller.TooltipRectTransform);
        _root = new GameObject(
            "BppCollectionInspection",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(GraphicRaycaster)
        );
        Object.DontDestroyOnLoad(_root);
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = BppOverlaySorting.InspectionBackdrop;
        var backdrop = new GameObject("DismissBackground", typeof(RectTransform), typeof(Image));
        backdrop.transform.SetParent(_root.transform, false);
        Stretch(backdrop.GetComponent<RectTransform>());
        backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, .53f);
        var card = new GameObject("InspectionCard", typeof(RectTransform));
        card.transform.SetParent(_root.transform, false);
        CardRoot = card.GetComponent<RectTransform>();
        CardRoot.anchorMin = CardRoot.anchorMax = CardRoot.pivot = new Vector2(0f, 1f);
        CardRoot.anchoredPosition = Vector2.zero;
        CardRoot.sizeDelta = new Vector2(Screen.width, Screen.height);

        try
        {
            controller.LockingSequence?.Kill();
            controller.LockingSequence = null;
            controller.OnLockSequenceCompletedPublic();
            controller.LockModeCanvasGroup.alpha = 1f;
            _nativeBackground =
                AccessTools
                    .Field(typeof(CardTooltipController), "lockModeBackgroundImage")
                    ?.GetValue(controller) as Graphic;
            if (_nativeBackground != null)
            {
                _backgroundEnabled = _nativeBackground.enabled;
                _backgroundRaycast = _nativeBackground.raycastTarget;
                _nativeBackground.enabled = false;
                _nativeBackground.raycastTarget = false;
            }
            // Capture both hierarchies BEFORE detaching either canvas.
            foreach (
                var native in new[]
                {
                    controller.LockModeCanvas,
                    controller.RootCanvasComponent,
                }.Distinct()
            )
                _canvases.Add(new CanvasLease(native));
            foreach (var lease in _canvases)
                lease.Promote(
                    lease.Canvas == controller.RootCanvasComponent
                        ? BppOverlaySorting.InspectionContent
                        : BppOverlaySorting.InspectionControls
                );
            PromoteVariantControls();
            controller.SetCanvasInteractable(true);
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(_onTextChanged);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public Vector2 MeasureDescription(float width, float height)
    {
        if (_released || _controller == null)
            return Vector2.zero;
        CardRoot.sizeDelta = new Vector2(Screen.width, Screen.height);
        var rect = _controller.TooltipRectTransform;
        rect.localScale = _tooltipRect.Scale;
        var bounds = DescriptionVisualBounds();
        var fit = NativeInspectionSizing.FitScale(bounds.width, bounds.height, width, height);
        rect.localScale *= fit;
        _descriptionScale = rect.localScale;
        return DescriptionVisualBounds().size;
    }

    // Returns the description bounds found before the move; once they are centred on the
    // target, the previous frame already left the description in place.
    public Rect PositionDescription(float centerX, float centerY, float scale)
    {
        var rect = _controller.TooltipRectTransform;
        rect.localScale = _descriptionScale * scale;
        var bounds = DescriptionVisualBounds();
        rect.position += new Vector3(centerX - bounds.center.x, centerY - bounds.center.y, 0f);
        return bounds;
    }

    // True once after a text inside the description changed its mesh, or the description's
    // rect changed size (a section shown or hidden, an image resized). Scale and position
    // writes never change a rect's size, so measuring cannot report itself.
    public bool ConsumeDescriptionChanged()
    {
        var changed = _descriptionChanged;
        _descriptionChanged = false;
        if (_released)
            return changed;
        changed |= SizeChanged(_controller.CanvasContentRectTransform, ref _contentSize);
        changed |= SizeChanged(_controller.TooltipRectTransform, ref _tooltipSize);
        return changed;
    }

    private static bool SizeChanged(RectTransform? rect, ref Vector2 last)
    {
        if (rect == null)
            return false;
        var size = rect.rect.size;
        if (Mathf.Abs(size.x - last.x) <= .01f && Mathf.Abs(size.y - last.y) <= .01f)
            return false;
        last = size;
        return true;
    }

    // TEXT_CHANGED_EVENT is global, so only texts inside the description count. It runs inside
    // TMP's dispatch to every listener, so a failure here must not escape; re-measuring is the
    // safe answer to one.
    private void OnTextChanged(Object changed)
    {
        try
        {
            if (_released || _descriptionChanged || changed is not TMP_Text text || text == null)
                return;
            var content = _controller.CanvasContentRectTransform;
            if (content != null && text.transform.IsChildOf(content))
                _descriptionChanged = true;
        }
        catch
        {
            _descriptionChanged = true;
        }
    }

    // Only the variant controls and their open lists protect clicks from dismissal.
    // Native fullscreen dropdown blockers do not protect clicks elsewhere.
    public bool PollDismiss()
    {
        var mouse = Mouse.current;
        if (_released || mouse == null || !Application.isFocused)
        {
            _backgroundPress.Reset();
            return false;
        }
        var pressed = mouse.leftButton.wasPressedThisFrame;
        var released = mouse.leftButton.wasReleasedThisFrame;
        if (!pressed && !released)
            return false;
        Canvas.ForceUpdateCanvases();
        return _backgroundPress.Sample(
            Time.frameCount,
            pressed,
            released,
            IsVariantControlPoint(mouse.position.ReadValue())
        );
    }

    // Keep the hidden preview parent alive until the scope has settled pending SetUp calls.
    public void ReleaseNative()
    {
        if (_released)
            return;
        _released = true;
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(_onTextChanged);
        // Restore descendants before parents; captured parents remain alive throughout.
        foreach (var lease in _canvases.AsEnumerable().Reverse())
            lease.Restore();
        _tooltipRect.Restore();
        if (_nativeBackground != null)
        {
            _nativeBackground.enabled = _backgroundEnabled;
            _nativeBackground.raycastTarget = _backgroundRaycast;
        }
        _root.SetActive(false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ReleaseNative();
        Object.Destroy(_root);
    }

    private void PromoteVariantControls()
    {
        // The native panel is a separate subtree. Give it its own canvas/raycaster above
        // both description and dismiss background, regardless of its original camera canvas.
        var panelType = typeof(CardTooltipController).Assembly.GetType(
            "TheBazaar.UI.Tooltips.CardLockVariantPanel"
        );
        if (panelType == null)
            return;
        var panel = _controller.LockModeContainer.GetComponent(panelType);
        var root =
            panel == null
                ? null
                : AccessTools.Field(panelType, "_panelRoot")?.GetValue(panel) as RectTransform;
        if (root == null)
            return;
        // Capture the concrete controls before any TMP dropdown creates a fullscreen
        // blocker. Never use the panel root or a global Selectable scan as a hit region.
        _controls.AddRange(root.GetComponentsInChildren<Selectable>(true));
        _dropdowns.AddRange(root.GetComponentsInChildren<TMP_Dropdown>(true));
        _captions.AddRange(root.GetComponentsInChildren<TMP_Text>(true));
        var canvas = root.GetComponent<Canvas>();
        var ownsCanvas = canvas == null;
        if (ownsCanvas)
            canvas = root.gameObject.AddComponent<Canvas>();
        var lease = new CanvasLease(canvas!, ownsCanvas);
        _canvases.Add(lease);
        lease.Promote(BppOverlaySorting.InspectionVariantControls, detach: false);
        Canvas.ForceUpdateCanvases();
    }

    private bool IsVariantControlPoint(Vector2 point)
    {
        if (
            _controls.Any(control =>
                control != null
                && control.isActiveAndEnabled
                && control.targetGraphic != null
                && IsVisible(control.targetGraphic)
                && control.transform is RectTransform rect
                && ScreenBounds(rect).Contains(point)
            )
        )
            return true;
        if (
            _captions.Any(text =>
                text != null && IsVisible(text) && TextBounds(text).Contains(point)
            )
        )
            return true;
        return _dropdowns.Any(dropdown =>
            dropdown != null
            && DropdownList?.GetValue(dropdown) is GameObject list
            && list.activeInHierarchy
            && list.transform is RectTransform rect
            && ScreenBounds(rect).Contains(point)
        );
    }

    private Rect DescriptionVisualBounds()
    {
        var found = false;
        var bounds = default(Rect);
        _controller.CanvasContentRectTransform.GetComponentsInChildren(false, _graphics);
        foreach (var graphic in _graphics)
        {
            if (!IsVisible(graphic))
                continue;
            var next = graphic is TMP_Text text
                ? TextBounds(text)
                : ScreenBounds(graphic.rectTransform);
            if (next.width <= 0f || next.height <= 0f)
                continue;
            bounds = found
                ? Rect.MinMaxRect(
                    Mathf.Min(bounds.xMin, next.xMin),
                    Mathf.Min(bounds.yMin, next.yMin),
                    Mathf.Max(bounds.xMax, next.xMax),
                    Mathf.Max(bounds.yMax, next.yMax)
                )
                : next;
            found = true;
        }
        _graphics.Clear();
        return found ? bounds : ScreenBounds(_controller.CanvasContentRectTransform);
    }

    private static bool IsVisible(Graphic graphic) =>
        graphic.isActiveAndEnabled
        && graphic.color.a > .001f
        && !graphic.canvasRenderer.cull
        && graphic.canvasRenderer.GetInheritedAlpha() > .001f;

    private static Rect TextBounds(TMP_Text text)
    {
        var bounds = text.textBounds;
        var a = text.transform.TransformPoint(bounds.min);
        var b = text.transform.TransformPoint(bounds.max);
        return Rect.MinMaxRect(
            Mathf.Min(a.x, b.x),
            Mathf.Min(a.y, b.y),
            Mathf.Max(a.x, b.x),
            Mathf.Max(a.y, b.y)
        );
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private Rect ScreenBounds(RectTransform rect)
    {
        rect.GetWorldCorners(_corners);
        float x0 = _corners[0].x,
            x1 = x0,
            y0 = _corners[0].y,
            y1 = y0;
        for (var i = 1; i < _corners.Length; i++)
        {
            var p = _corners[i];
            x0 = Mathf.Min(x0, p.x);
            x1 = Mathf.Max(x1, p.x);
            y0 = Mathf.Min(y0, p.y);
            y1 = Mathf.Max(y1, p.y);
        }
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    private sealed class CanvasLease
    {
        public Canvas Canvas { get; }
        private readonly RectState _rect;
        private readonly RenderMode _mode;
        private readonly Camera? _camera;
        private readonly int _order;
        private readonly int _layer;
        private readonly bool _override;
        private readonly float _scale;
        private readonly CanvasScaler? _scaler;
        private readonly bool _scalerEnabled;
        private readonly (Canvas canvas, bool sorting, int order, int layer)[] _children;
        private readonly bool _ownsCanvas;
        private readonly List<RaycasterLease> _raycasters = new();
        private readonly List<(
            CanvasGroup group,
            bool interactable,
            bool blocks,
            bool ignore,
            bool owned
        )> _groups = new();

        public CanvasLease(Canvas canvas, bool ownsCanvas = false)
        {
            Canvas = canvas;
            _ownsCanvas = ownsCanvas;
            _rect = new RectState((RectTransform)canvas.transform);
            _mode = canvas.renderMode;
            _camera = canvas.worldCamera;
            _order = canvas.sortingOrder;
            _layer = canvas.sortingLayerID;
            _override = canvas.overrideSorting;
            _scale = canvas.scaleFactor;
            _scaler = canvas.GetComponent<CanvasScaler>();
            _scalerEnabled = _scaler != null && _scaler.enabled;
            _children = canvas
                .GetComponentsInChildren<Canvas>(true)
                .Where(c => c != canvas)
                .Select(c => (c, c.overrideSorting, c.sortingOrder, c.sortingLayerID))
                .ToArray();
        }

        public void Promote(int order, bool detach = true)
        {
            if (detach)
            {
                Canvas.transform.SetParent(null, false);
                if (_scaler != null)
                    _scaler.enabled = false;
                Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                Canvas.scaleFactor = 1f;
                Canvas.transform.localScale = Vector3.one;
            }
            Canvas.overrideSorting = true;
            Canvas.sortingLayerID = 0;
            Canvas.sortingOrder = order;
            _raycasters.Add(new RaycasterLease(Canvas));
            foreach (var child in _children)
            {
                if (!child.canvas.transform.IsChildOf(Canvas.transform))
                    continue;
                child.canvas.overrideSorting = true;
                child.canvas.sortingOrder = order;
                child.canvas.sortingLayerID = 0;
                _raycasters.Add(new RaycasterLease(child.canvas));
            }
            if (!detach)
            {
                // A nested override canvas starts a new raycast-filter boundary. Keep its
                // controls interactive even if their former parent group blocked raycasts.
                var group = Canvas.GetComponent<CanvasGroup>();
                var ownsGroup = group == null;
                if (ownsGroup)
                    group = Canvas.gameObject.AddComponent<CanvasGroup>();
                _groups.Add(
                    (
                        group!,
                        group!.interactable,
                        group.blocksRaycasts,
                        group.ignoreParentGroups,
                        ownsGroup
                    )
                );
                group.interactable = group.blocksRaycasts = true;
                group.ignoreParentGroups = true;
            }
        }

        public void Restore()
        {
            if (Canvas == null)
                return;
            foreach (var child in _children)
            {
                if (child.canvas == null)
                    continue;
                child.canvas.overrideSorting = child.sorting;
                child.canvas.sortingOrder = child.order;
                child.canvas.sortingLayerID = child.layer;
            }
            foreach (var group in _groups)
            {
                if (group.group == null)
                    continue;
                group.group.interactable = group.interactable;
                group.group.blocksRaycasts = group.blocks;
                group.group.ignoreParentGroups = group.ignore;
                if (group.owned)
                    Object.Destroy(group.group);
            }
            foreach (var raycaster in _raycasters.AsEnumerable().Reverse())
                raycaster.Restore();
            _rect.Restore();
            Canvas.renderMode = _mode;
            Canvas.worldCamera = _camera;
            Canvas.overrideSorting = _override;
            Canvas.sortingLayerID = _layer;
            Canvas.sortingOrder = _order;
            Canvas.scaleFactor = _scale;
            if (_scaler != null)
                _scaler.enabled = _scalerEnabled;
            if (_ownsCanvas)
            {
                Canvas.enabled = false;
                Object.Destroy(Canvas);
            }
        }
    }

    private sealed class RaycasterLease
    {
        private readonly GraphicRaycaster _raycaster;
        private readonly bool _owned;
        private readonly bool _enabled;
        private readonly bool _reversed;
        private readonly GraphicRaycaster.BlockingObjects _blocking;

        public RaycasterLease(Canvas canvas)
        {
            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            _owned = raycaster == null;
            _raycaster = raycaster ?? canvas.gameObject.AddComponent<GraphicRaycaster>();
            _enabled = _raycaster.enabled;
            _reversed = _raycaster.ignoreReversedGraphics;
            _blocking = _raycaster.blockingObjects;
            _raycaster.enabled = true;
            _raycaster.ignoreReversedGraphics = false;
            _raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
        }

        public void Restore()
        {
            if (_raycaster == null)
                return;
            _raycaster.enabled = !_owned && _enabled;
            _raycaster.ignoreReversedGraphics = _reversed;
            _raycaster.blockingObjects = _blocking;
            if (_owned)
                Object.Destroy(_raycaster);
        }
    }

    private sealed class RectState
    {
        private readonly RectTransform _rect;
        private readonly Transform? _parent;
        private readonly int _sibling;
        private readonly Vector2 _min,
            _max,
            _pivot,
            _size;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        public Vector3 Scale { get; }

        public RectState(RectTransform rect)
        {
            _rect = rect;
            _parent = rect.parent;
            _sibling = rect.GetSiblingIndex();
            _min = rect.anchorMin;
            _max = rect.anchorMax;
            _pivot = rect.pivot;
            _size = rect.sizeDelta;
            _position = rect.anchoredPosition3D;
            _rotation = rect.localRotation;
            Scale = rect.localScale;
        }

        public void Restore()
        {
            if (_rect == null)
                return;
            _rect.SetParent(_parent, false);
            _rect.SetSiblingIndex(_sibling);
            _rect.anchorMin = _min;
            _rect.anchorMax = _max;
            _rect.pivot = _pivot;
            _rect.sizeDelta = _size;
            _rect.anchoredPosition3D = _position;
            _rect.localScale = Scale;
            _rect.localRotation = _rotation;
        }
    }
}
