using BazaarPlusPlus.Core.Ui;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.NativeAdjustment;

/// <summary>
/// The day clock on a screen <see cref="TouchScreenFill"/> draws the board nearer on. The clock
/// (the encounter prefab <c>ClockViewController</c> sits on, not the board's empty
/// <c>playerClockAnchor</c>) is board meshes left of the board, in the scenery the nearer view
/// trims, so it would fall off the screen's left edge. Instead it shrinks into the band above the
/// board, its left edge level with the board's (<see cref="ScreenFill.PlaceDayClock"/>). It is
/// placed again after every camera update; a position or scale the game writes in between is taken
/// as the new original, so its own animations still play. Main thread only.
/// </summary>
internal sealed class TouchDayClock
{
    // While the clock is missing, it is searched for at most every half second.
    private const int SearchEvery = 30;

    private readonly IScreenFillHost _host;
    private Transform? _found;
    private int _searchCooldown;
    private Transform? _anchor;
    private Vector3 _originalPosition;
    private Vector3 _originalScale;
    private Vector3 _writtenPosition;
    private Vector3 _writtenScale;
    private MeshRenderer[] _clock = [];

    public TouchDayClock(IScreenFillHost host) => _host = host;

    /// <summary>
    /// Places the clock in the band above <paramref name="board"/>, the board's top-left corner in
    /// screen pixels down from the top, or back where the game put it when that is null.
    /// </summary>
    public void Place(Camera camera, Vector2? board)
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
        if (
            board is not { } corner
            || !anchor.gameObject.activeInHierarchy
            || !TryScreenBox(_clock, camera, out var clock)
        )
            return;
        var pivot = camera.WorldToScreenPoint(anchor.position);
        if (pivot.z <= 0f)
            return;
        var safe = Screen.safeArea;
        if (
            ScreenFill.PlaceDayClock(
                Screen.height,
                safe.xMin,
                safe.yMax,
                corner.x,
                corner.y,
                clock,
                pivot.x,
                pivot.y
            )
            is not { } placement
        )
            return;
        anchor.position = camera.ScreenToWorldPoint(
            new Vector3(placement.PivotX, placement.PivotY, pivot.z * ScreenFill.ClockNearer)
        );
        anchor.localScale = _originalScale * (placement.Scale * ScreenFill.ClockNearer);
        _writtenPosition = anchor.localPosition;
        _writtenScale = anchor.localScale;
    }

    private Transform? Find()
    {
        if (MobilePlatform.Port.IsAlive(_found))
            return _found;
        _found = null;
        _anchor = null;
        if (_searchCooldown-- > 0)
            return null;
        _searchCooldown = SearchEvery;
        _found = _host.FindDayClock();
        return MobilePlatform.Port.IsAlive(_found) ? _found : null;
    }

    private static bool TryScreenBox(MeshRenderer[] renderers, Camera camera, out ScreenBox box)
    {
        box = default;
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        var port = MobilePlatform.Port;
        foreach (var renderer in renderers)
        {
            if (
                !port.IsAlive(renderer)
                || !renderer.enabled
                || !renderer.gameObject.activeInHierarchy
            )
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
                    return false;
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
        }
        if (!float.IsFinite(min.x) || max.x <= min.x || max.y <= min.y)
            return false;
        box = new ScreenBox(min.x, min.y, max.x - min.x, max.y - min.y);
        return true;
    }
}
