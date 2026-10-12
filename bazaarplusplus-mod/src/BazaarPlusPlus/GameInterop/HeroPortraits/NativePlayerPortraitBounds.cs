#nullable enable
using BazaarGameShared.Domain.Core.Types;
using TheBazaar;
using TheBazaar.UI.Components;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.HeroPortraits;

internal sealed class NativePlayerPortraitBounds
{
    private readonly Vector3[] _corners = new Vector3[4];
    private readonly List<MeshFilter> _frameMeshes = new();
    private Transform? _frameRoot;

    internal bool TryRead(out Rect frame, out float bottom, out Rect viewport)
    {
        frame = default;
        bottom = 0f;
        viewport = default;
        var board = Singleton<BoardManager>.Instance;
        var camera = Camera.main;
        if (
            board == null
            || camera == null
            || !board.TryGetEncounterControllerForCombatant(ECombatantId.Player, out var portrait)
            || portrait == null
            || !portrait.gameObject.activeInHierarchy
            || portrait.backgroundRenderer == null
            || Data.PlayerExperienceBar is not ExperienceBarController experience
        )
            return false;

        // EncounterController places the background and tier-frame meshes under one parent.
        // Read those meshes without including the skin sprite or its animated replacement.
        var frameRoot = portrait.backgroundRenderer.transform.parent;
        if (frameRoot == null || !frameRoot.gameObject.activeInHierarchy)
            return false;
        if (_frameRoot != frameRoot)
        {
            _frameRoot = frameRoot;
            _frameMeshes.Clear();
            // Tier-frame meshes are direct siblings of the background. Descendant meshes
            // belong to skin animations, banners, or effects and must not enlarge the frame.
            foreach (Transform child in frameRoot)
                if (child.TryGetComponent<MeshFilter>(out var filter))
                    _frameMeshes.Add(filter);
        }

        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var filter in _frameMeshes)
        {
            if (
                filter == null
                || filter.sharedMesh == null
                || !filter.gameObject.activeInHierarchy
                || !filter.TryGetComponent<MeshRenderer>(out var renderer)
                || !renderer.enabled
            )
                continue;
            var bounds = filter.sharedMesh.bounds;
            for (var i = 0; i < 8; i++)
            {
                var local =
                    bounds.center
                    + Vector3.Scale(
                        bounds.extents,
                        new Vector3(
                            (i & 1) == 0 ? -1f : 1f,
                            (i & 2) == 0 ? -1f : 1f,
                            (i & 4) == 0 ? -1f : 1f
                        )
                    );
                var point = camera.WorldToScreenPoint(filter.transform.TransformPoint(local));
                if (point.z <= 0f)
                    return false;
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
        }
        if (!float.IsFinite(min.x) || max.x <= min.x || max.y <= min.y)
            return false;

        var bar = experience.GetBar();
        if (bar == null || bar.transform is not RectTransform barRect)
            return false;
        var canvas = bar.GetComponentInParent<Canvas>();
        if (canvas == null)
            return false;
        var uiCamera =
            canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.rootCanvas.worldCamera ?? camera;
        barRect.GetWorldCorners(_corners);
        bottom = min.y;
        foreach (var corner in _corners)
            bottom = Mathf.Min(bottom, RectTransformUtility.WorldToScreenPoint(uiCamera, corner).y);

        frame = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        var pixels = camera.pixelRect;
        var safe = Screen.safeArea;
        viewport = Rect.MinMaxRect(
            Mathf.Max(pixels.xMin, safe.xMin),
            Mathf.Max(pixels.yMin, safe.yMin),
            Mathf.Min(pixels.xMax, safe.xMax),
            Mathf.Min(pixels.yMax, safe.yMax)
        );
        return true;
    }
}
