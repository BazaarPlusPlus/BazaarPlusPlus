using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.Ui;

/// <summary>
/// One drag-scrolled list's gestures, from the first moving frame to the frame it settles: each
/// finished gesture logs <paramref name="scrolled"/> with how far it went in dp, its peak speed,
/// and how many frames it coasted after the finger lifted (inertia). History's lists and
/// Collection's grid and rail log through it; tick it every frame the list is visible.
/// </summary>
internal sealed class ScrollTracker(BppLogEvent scrolled, string name, ScrollRect scroll)
{
    private Vector2 _last = scroll.content.anchoredPosition;
    private Vector2 _start;
    private bool _moving;
    private int _still;
    private int _coast;
    private float _peak;

    /// <summary>Whether a finger or the mouse is down this frame.</summary>
    public static bool PointerPressed()
    {
        try
        {
            return UnityEngine.InputSystem.Pointer.current?.press.isPressed == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The list moved programmatically (a page, a reset): that is no gesture.</summary>
    public void Sync()
    {
        _last = scroll.content.anchoredPosition;
        _moving = false;
    }

    public void Tick(bool pressed, float unitsPerDp)
    {
        if (!scroll.gameObject.activeInHierarchy)
            return;
        var position = scroll.content.anchoredPosition;
        if (position != _last)
        {
            if (!_moving)
            {
                _moving = true;
                _start = _last;
                _coast = 0;
                _peak = 0f;
            }
            _peak = Math.Max(_peak, scroll.velocity.magnitude);
            if (!pressed)
                _coast++;
            _last = position;
            _still = 0;
            return;
        }
        if (!_moving || pressed || ++_still < 3)
            return;
        _moving = false;
        BppLog.InfoEvent(
            scrolled,
            ("list", name),
            ("distance_dp", Math.Round((position - _start).magnitude / unitsPerDp, 1)),
            ("peak_velocity_dp", Math.Round(_peak / unitsPerDp)),
            ("coast_frames", _coast),
            ("inertia", scroll.inertia)
        );
    }
}
