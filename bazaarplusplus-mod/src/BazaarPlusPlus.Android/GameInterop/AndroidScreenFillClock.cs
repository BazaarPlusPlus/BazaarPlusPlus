using Il2CppTheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The day clock on a screen <see cref="AndroidScreenFill"/> draws the board nearer on. The clock
/// (the prefab <see cref="ClockViewController"/> sits on) is board meshes left of the board, in
/// the scenery the nearer view trims, so it would fall off the screen's left edge. Instead it
/// shrinks into the band above the board, its left edge level with the board's. It is placed again after
/// every camera update; a position or scale the game writes in between is taken as the new
/// original, so its own animations still play. Main thread only.
/// </summary>
internal sealed class AndroidScreenFillClock
{
    // Of the band's height, what is kept clear above and below the clock.
    private const float BandMargin = 0.12f;

    // Below this the clock is too small to read; it is left where the game put it.
    private const float MinScale = 0.4f;

    // Of its distance from the camera, how far the clock is drawn.
    private const float Nearer = 0.5f;

    private readonly AndroidVaultBounds _vault = new();
    private ClockViewController? _view;
    private int _searchCooldown;
    private Transform? _anchor;
    private Vector3 _originalPosition;
    private Vector3 _originalScale;
    private Vector3 _writtenPosition;
    private Vector3 _writtenScale;
    private MeshRenderer[] _clock = [];

    public void Place(Camera camera, bool nearer)
    {
        var anchor = Find();
        if (anchor == null)
            return;
        if (anchor != _anchor)
        {
            _anchor = anchor;
            _originalPosition = _writtenPosition = anchor.localPosition;
            _originalScale = _writtenScale = anchor.localScale;
            _clock = anchor.GetComponentsInChildren<MeshRenderer>(false);
        }
        else if (anchor.localPosition != _writtenPosition || anchor.localScale != _writtenScale)
        {
            _originalPosition = anchor.localPosition;
            _originalScale = anchor.localScale;
        }
        anchor.localPosition = _originalPosition;
        anchor.localScale = _originalScale;
        _writtenPosition = _originalPosition;
        _writtenScale = _originalScale;
        if (!nearer || !anchor.gameObject.activeInHierarchy)
            return;

        // The board's top-left corner mirrors the vault, its bottom-right, through its centre.
        var center = Il2Cpp.BoardManager.Instance?.boardCenter;
        if (
            center == null
            || !TryScreenRect(_clock, camera, out var clock)
            || !_vault.TryRead(Screen.height, out var vault)
        )
            return;
        var middle = camera.WorldToScreenPoint(center.position);
        if (middle.z <= 0f)
            return;
        // Screen pixels run up from the bottom; the vault is read top-down.
        var boardTop = 2f * middle.y - (Screen.height - vault.Bottom);
        var boardLeft = 2f * middle.x - vault.Right;
        var bandTop = Screen.safeArea.yMax;
        var band = bandTop - boardTop;
        var height = Math.Min(clock.height, band * (1f - 2f * BandMargin));
        var scale = height / clock.height;
        if (scale < MinScale)
            return;
        var target = new Vector2(
            Math.Max(Screen.safeArea.xMin, boardLeft) + clock.width * scale / 2f,
            boardTop + band / 2f
        );
        var pivot = camera.WorldToScreenPoint(anchor.position);
        if (pivot.z <= 0f)
            return;
        // Scaling about the pivot draws the clock's centre that much nearer to it.
        var pivotTarget = target - ((Vector2)clock.center - (Vector2)pivot) * scale;
        // The scenery over the band (palm fronds) stands above the board; drawn nearer the
        // camera, and smaller by as much, the clock stays in front of it.
        var depth = pivot.z * Nearer;
        anchor.position = camera.ScreenToWorldPoint(
            new Vector3(pivotTarget.x, pivotTarget.y, depth)
        );
        anchor.localScale = _originalScale * (scale * Nearer);
        _writtenPosition = anchor.localPosition;
        _writtenScale = anchor.localScale;
    }

    // The clock is the encounter prefab ClockViewController sits on, not the board's empty
    // playerClockAnchor. Searched for at most every half second while missing.
    private Transform? Find()
    {
        if (_view != null)
            return _view.transform;
        _anchor = null;
        if (_searchCooldown-- > 0)
            return null;
        _searchCooldown = 30;
        _view = UnityEngine.Object.FindObjectOfType<ClockViewController>();
        return _view != null ? _view.transform : null;
    }

    private static bool TryScreenRect(MeshRenderer[] renderers, Camera camera, out Rect rect)
    {
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;
            var bounds = renderer.bounds;
            for (var i = 0; i < 8; i++)
            {
                var corner =
                    bounds.center
                    + Vector3.Scale(
                        bounds.extents,
                        new Vector3(
                            (i & 1) == 0 ? -1f : 1f,
                            (i & 2) == 0 ? -1f : 1f,
                            (i & 4) == 0 ? -1f : 1f
                        )
                    );
                var point = camera.WorldToScreenPoint(corner);
                if (point.z <= 0f)
                {
                    rect = default;
                    return false;
                }
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
        }
        rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return float.IsFinite(min.x) && max.x > min.x && max.y > min.y;
    }
}
