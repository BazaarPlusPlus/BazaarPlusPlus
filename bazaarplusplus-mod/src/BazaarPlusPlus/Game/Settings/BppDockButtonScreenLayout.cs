#nullable enable
using BazaarPlusPlus.GameInterop.Scenes;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal sealed class BppDockButtonScreenLayout
{
    private const string BppObjectPrefix = "BPP_";

    // Transforms seen in one scene; a guard against unbounded growth in a long-lived scene.
    private const int MaximumCachedBppNameVerdicts = 8192;

    // Whether a Transform's own name carries the BPP prefix, keyed by instance ID. Only the name is
    // cached, never the ancestor verdict, so a native subtree reparented under a BPP container is
    // still seen on the next walk. BPP names its objects as it creates them, so a cached name does
    // not go stale.
    private static readonly Dictionary<int, bool> BppNameVerdicts = [];
    private static string? _bppNameVerdictsScene;

    private readonly List<BppDockButtonObstacle> _blockerScratch = [];
    private readonly List<(Transform Button, Transform Canvas)> _blockerSourceScratch = [];
    private readonly List<Graphic> _graphicScratch = [];
    private readonly List<CanvasGroup> _canvasGroupScratch = [];
    private readonly Vector3[] _cornerScratch = new Vector3[4];

    /// <summary>
    /// The scene-wide scan one layout probe shares across every
    /// <see cref="TryResolveAndApplyCollection"/> call it makes.
    /// </summary>
    internal static Button[] FindSceneButtons() =>
        UnityEngine.Object.FindObjectsOfType<Button>(includeInactive: false);

    /// <summary>
    /// Places <paramref name="collectionRect"/> beside the anchor clear of every visible native
    /// button in <paramref name="sceneButtons"/>. <paramref name="blockerName"/> names what stopped
    /// the placement and is set only when this returns false.
    /// </summary>
    internal bool TryResolveAndApplyCollection(
        Button[] sceneButtons,
        Button anchorButton,
        RectTransform collectionRect,
        float localGap,
        out string? blockerName,
        RectTransform? occupiedRect = null,
        bool beside = false
    )
    {
        blockerName = null;
        var collectionButton = collectionRect.GetComponent<Button>();
        if (collectionButton == null)
        {
            blockerName = "missing-collection-button";
            return false;
        }

        if (
            !TryCalculateButtonFootprint(
                anchorButton,
                (RectTransform)anchorButton.transform,
                out var gearBounds
            )
        )
        {
            blockerName = "gear-footprint-unavailable";
            return false;
        }

        if (
            !TryCalculateButtonFootprint(collectionButton, collectionRect, out var collectionBounds)
        )
        {
            blockerName = "collection-footprint-unavailable";
            return false;
        }

        var viewportBounds = new BppDockButtonBounds(0f, Screen.width, 0f, Screen.height);
        var anchorCanvas = ResolveRootCanvas(anchorButton.transform);
        if (anchorCanvas == null || !anchorCanvas.isActiveAndEnabled)
        {
            blockerName = "anchor-canvas-unavailable";
            return false;
        }

        var gap = ResolveScreenGap(collectionRect.parent as RectTransform, localGap);
        var blockers = CollectVisibleNativeBlockers(
            sceneButtons,
            viewportBounds,
            anchorButton,
            collectionButton,
            anchorCanvas.targetDisplay
        );
        if (
            occupiedRect != null
            && occupiedRect.GetComponent<Button>() is { } occupiedButton
            && TryCalculateButtonFootprint(occupiedButton, occupiedRect, out var occupiedBounds)
        )
            _blockerScratch.Add(
                new BppDockButtonObstacle(occupiedRect.name, occupiedBounds, isActive: true)
            );
        var plan = beside
            ? BppCollectionDockButtonLayoutPlanner.ResolveBeside(
                viewportBounds,
                gearBounds,
                collectionBounds.Width,
                collectionBounds.Height,
                gap,
                blockers
            )
            : BppCollectionDockButtonLayoutPlanner.Resolve(
                viewportBounds,
                gearBounds,
                collectionBounds.Width,
                collectionBounds.Height,
                gap,
                blockers
            );
        if (!plan.CanApply)
        {
            // Names are log labels only, so a scanned blocker's path is built here, before the
            // next call clears the source scratch, and only when it actually blocks.
            blockerName = DescribeBlocker(plan.Blocker);
            return false;
        }

        if (
            !TryCalculateTargetLocalPosition(
                collectionRect,
                collectionButton,
                collectionBounds,
                plan.Bounds,
                out var collectionLocalPosition
            )
        )
        {
            blockerName = "target-local-position-unavailable";
            return false;
        }

        collectionRect.localPosition = collectionLocalPosition;
        collectionRect.localRotation = Quaternion.identity;
        collectionRect.SetAsLastSibling();
        return true;
    }

    private IReadOnlyList<BppDockButtonObstacle> CollectVisibleNativeBlockers(
        Button[] sceneButtons,
        BppDockButtonBounds viewportBounds,
        Button anchorButton,
        Button collectionButton,
        int targetDisplay
    )
    {
        _blockerScratch.Clear();
        _blockerSourceScratch.Clear();
        ResetBppNameVerdictsOnSceneChange();
        foreach (var button in sceneButtons)
        {
            if (
                button == null
                || button == anchorButton
                || button == collectionButton
                || !button.gameObject.scene.IsValid()
                || !button.isActiveAndEnabled
                || !button.gameObject.activeInHierarchy
                || HasBppAncestor(button.transform)
                || IsRelated(button.transform, anchorButton.transform)
            )
                continue;

            var canvas = ResolveRootCanvas(button.transform);
            if (
                canvas == null
                || !canvas.isActiveAndEnabled
                || canvas.targetDisplay != targetDisplay
            )
                continue;

            if (!TryCalculateVisibleButtonFootprint(button, out var bounds))
                continue;

            if (!viewportBounds.Overlaps(bounds))
                continue;

            _blockerScratch.Add(
                new BppDockButtonObstacle(
                    name: null,
                    bounds,
                    isActive: true,
                    sourceIndex: _blockerSourceScratch.Count
                )
            );
            _blockerSourceScratch.Add((button.transform, canvas.transform));
        }

        return _blockerScratch;
    }

    private string? DescribeBlocker(BppDockButtonObstacle? blocker)
    {
        if (
            blocker is not { SourceIndex: var index }
            || index < 0
            || index >= _blockerSourceScratch.Count
        )
            return blocker?.Name;

        var (button, canvas) = _blockerSourceScratch[index];
        return $"{BuildHierarchyPath(button)} [canvas={BuildHierarchyPath(canvas)}]";
    }

    private bool TryCalculateButtonFootprint(
        Button button,
        RectTransform fallbackRect,
        out BppDockButtonBounds bounds
    )
    {
        var visualRect = button.targetGraphic?.rectTransform ?? fallbackRect;
        if (!TryCalculateScreenBounds(visualRect, out var targetGraphicBounds))
        {
            bounds = default;
            return false;
        }

        _graphicScratch.Clear();
        button.GetComponentsInChildren(includeInactive: true, _graphicScratch);
        foreach (var graphic in _graphicScratch)
        {
            if (!IsFootprintGraphic(button, graphic))
                continue;

            if (TryCalculateScreenBounds(graphic.rectTransform, out var graphicBounds))
                targetGraphicBounds = targetGraphicBounds.Union(graphicBounds);
        }

        bounds = targetGraphicBounds;
        return bounds.IsValid && bounds.Width > 0.0001f && bounds.Height > 0.0001f;
    }

    private bool TryCalculateVisibleButtonFootprint(Button button, out BppDockButtonBounds bounds)
    {
        bounds = default;
        var hasVisibleGraphic = false;
        _graphicScratch.Clear();
        button.GetComponentsInChildren(includeInactive: false, _graphicScratch);
        foreach (var graphic in _graphicScratch)
        {
            if (!IsOwnedVisual(button, graphic) || !IsVisible(graphic))
                continue;

            if (!TryCalculateScreenBounds(graphic.rectTransform, out var graphicBounds))
                continue;

            bounds = hasVisibleGraphic ? bounds.Union(graphicBounds) : graphicBounds;
            hasVisibleGraphic = true;
        }

        if (!hasVisibleGraphic)
            return false;

        return bounds.IsValid && bounds.Width > 0.0001f && bounds.Height > 0.0001f;
    }

    private bool TryCalculateScreenBounds(RectTransform rect, out BppDockButtonBounds bounds)
    {
        rect.GetWorldCorners(_cornerScratch);
        if (!TryResolveEventCamera(rect, out var camera))
        {
            bounds = default;
            return false;
        }

        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var minY = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;
        for (var index = 0; index < _cornerScratch.Length; index++)
        {
            var screen = RectTransformUtility.WorldToScreenPoint(camera, _cornerScratch[index]);
            minX = Math.Min(minX, screen.x);
            maxX = Math.Max(maxX, screen.x);
            minY = Math.Min(minY, screen.y);
            maxY = Math.Max(maxY, screen.y);
        }

        bounds = new BppDockButtonBounds(minX, maxX, minY, maxY);
        return bounds.IsValid && bounds.Width > 0.0001f && bounds.Height > 0.0001f;
    }

    private bool TryCalculateTargetLocalPosition(
        RectTransform buttonRect,
        Button button,
        BppDockButtonBounds currentFootprintBounds,
        BppDockButtonBounds targetBounds,
        out Vector3 targetLocalPosition
    )
    {
        targetLocalPosition = default;
        var parentRect = buttonRect.parent as RectTransform;
        var visualRect = button.targetGraphic?.rectTransform ?? buttonRect;
        if (parentRect == null)
            return false;

        if (!currentFootprintBounds.IsValid || !TryResolveEventCamera(visualRect, out var camera))
            return false;

        var currentScreenCenter = new Vector2(
            currentFootprintBounds.CenterX,
            currentFootprintBounds.CenterY
        );
        var targetScreenCenter = new Vector2(targetBounds.CenterX, targetBounds.CenterY);
        if (
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                visualRect,
                currentScreenCenter,
                camera,
                out var currentVisualLocalPoint
            )
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                visualRect,
                targetScreenCenter,
                camera,
                out var targetVisualLocalPoint
            )
        )
            return false;

        var currentWorldCenter = visualRect.TransformPoint(
            new Vector3(currentVisualLocalPoint.x, currentVisualLocalPoint.y, 0f)
        );
        var targetWorldCenter = visualRect.TransformPoint(
            new Vector3(targetVisualLocalPoint.x, targetVisualLocalPoint.y, 0f)
        );
        var targetRootWorldPosition = buttonRect.position + targetWorldCenter - currentWorldCenter;
        targetLocalPosition = parentRect.InverseTransformPoint(targetRootWorldPosition);
        return true;
    }

    private bool IsVisible(Graphic graphic)
    {
        if (
            graphic == null
            || !graphic.isActiveAndEnabled
            || !graphic.gameObject.activeInHierarchy
            || graphic.canvasRenderer.cull
            || ResolveRootCanvas(graphic.transform) is not { isActiveAndEnabled: true }
        )
            return false;

        var alpha = graphic.color.a * graphic.canvasRenderer.GetAlpha();
        var stopAtCurrentTransform = false;
        for (
            var current = graphic.transform;
            current != null && !stopAtCurrentTransform;
            current = current.parent
        )
        {
            var canvas = current.GetComponent<Canvas>();
            if (canvas != null && !canvas.isActiveAndEnabled)
                return false;

            current.GetComponents(_canvasGroupScratch);
            foreach (var group in _canvasGroupScratch)
            {
                alpha *= group.alpha;
                if (group.ignoreParentGroups)
                {
                    stopAtCurrentTransform = true;
                    break;
                }
            }
        }

        return alpha > BppDockButtonVisualFootprint.VisibleAlphaThreshold;
    }

    private bool IsFootprintGraphic(Button owner, Graphic graphic) =>
        graphic != null
        && BppDockButtonVisualFootprint.ShouldIncludeGraphic(
            graphic.enabled,
            IsActiveBelowOwner(graphic.transform, owner.transform),
            ResolveAuthoredAlpha(graphic, owner.transform),
            IsOwnedVisual(owner, graphic)
        );

    private static bool IsOwnedVisual(Button owner, Graphic graphic)
    {
        for (var current = graphic.transform; current != null; current = current.parent)
        {
            if (current == owner.transform)
                return true;

            if (current.GetComponent<Button>() != null)
                return false;
        }

        return false;
    }

    private static bool IsActiveBelowOwner(Transform candidate, Transform buttonRoot)
    {
        // The clone root can be hidden while layout is retried. Only authored child states matter.
        for (
            var current = candidate;
            current != null && current != buttonRoot;
            current = current.parent
        )
        {
            if (!current.gameObject.activeSelf)
                return false;
        }

        return true;
    }

    private float ResolveAuthoredAlpha(Graphic graphic, Transform buttonRoot)
    {
        var alpha = graphic.color.a;
        var stopAtCurrentTransform = false;
        for (
            var current = graphic.transform;
            current != null && current != buttonRoot && !stopAtCurrentTransform;
            current = current.parent
        )
        {
            current.GetComponents(_canvasGroupScratch);
            foreach (var group in _canvasGroupScratch)
            {
                alpha *= group.alpha;
                if (group.ignoreParentGroups)
                {
                    stopAtCurrentTransform = true;
                    break;
                }
            }
        }

        return alpha;
    }

    private static float ResolveScreenGap(RectTransform? referenceRect, float localGap)
    {
        if (referenceRect == null || localGap <= 0f)
            return Math.Max(0f, localGap);

        if (!TryResolveEventCamera(referenceRect, out var camera))
            return Math.Max(0f, localGap);

        var origin = RectTransformUtility.WorldToScreenPoint(
            camera,
            referenceRect.TransformPoint(Vector3.zero)
        );
        var horizontal = RectTransformUtility.WorldToScreenPoint(
            camera,
            referenceRect.TransformPoint(new Vector3(localGap, 0f, 0f))
        );
        var vertical = RectTransformUtility.WorldToScreenPoint(
            camera,
            referenceRect.TransformPoint(new Vector3(0f, localGap, 0f))
        );
        return (Vector2.Distance(origin, horizontal) + Vector2.Distance(origin, vertical)) * 0.5f;
    }

    private static bool TryResolveEventCamera(Transform transform, out Camera? camera)
    {
        var canvas = ResolveRootCanvas(transform);
        if (canvas == null || !canvas.isActiveAndEnabled)
        {
            camera = null;
            return false;
        }

        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            camera = null;
            return true;
        }

        camera = canvas.worldCamera;
        return camera != null;
    }

    private static Canvas? ResolveRootCanvas(Transform transform)
    {
        Canvas? rootCanvas = null;
        for (var current = transform; current != null; current = current.parent)
        {
            var canvas = current.GetComponent<Canvas>();
            if (canvas != null)
                rootCanvas = canvas;
        }

        return rootCanvas;
    }

    private static bool HasBppAncestor(Transform transform)
    {
        for (var current = transform; current != null; current = current.parent)
        {
            if (HasBppName(current))
                return true;
        }

        return false;
    }

    // Transform.name marshals a fresh string on every read; read it once per Transform per scene.
    private static bool HasBppName(Transform transform)
    {
        var instanceId = transform.GetInstanceID();
        if (BppNameVerdicts.TryGetValue(instanceId, out var hasBppName))
            return hasBppName;

        hasBppName = transform.name.StartsWith(BppObjectPrefix, StringComparison.Ordinal);
        BppNameVerdicts[instanceId] = hasBppName;
        return hasBppName;
    }

    private static void ResetBppNameVerdictsOnSceneChange()
    {
        var sceneName = ActiveSceneNameCache.Current;
        if (
            string.Equals(sceneName, _bppNameVerdictsScene, StringComparison.Ordinal)
            && BppNameVerdicts.Count < MaximumCachedBppNameVerdicts
        )
            return;

        _bppNameVerdictsScene = sceneName;
        BppNameVerdicts.Clear();
    }

    private static bool IsRelated(Transform candidate, Transform anchor) =>
        candidate.IsChildOf(anchor) || anchor.IsChildOf(candidate);

    private static string BuildHierarchyPath(Transform transform)
    {
        var path = transform.name;
        for (var current = transform.parent; current != null; current = current.parent)
            path = $"{current.name}/{path}";

        return path;
    }
}
