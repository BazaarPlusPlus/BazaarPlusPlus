using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.Ui;

internal sealed class ScrollTracker(BppLogEvent scrolled, string name, ScrollRect scroll)
{
    private Vector2 _last = scroll.content.anchoredPosition;
    private Vector2 _start;
    private bool _moving;
    private int _still;
    private int _coast;
    private float _peak;

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
