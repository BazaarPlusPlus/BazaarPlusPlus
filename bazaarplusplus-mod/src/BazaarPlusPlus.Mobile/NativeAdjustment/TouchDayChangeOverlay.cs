using UnityEngine;

namespace BazaarPlusPlus.Mobile.NativeAdjustment;

// NewDayTransition uses world-space meshes sized for the game's camera. Reapply
// the adjustment after camera updates while preserving transforms written by its animation.
internal sealed class TouchDayChangeOverlay
{
    private const int SearchEvery = 30;

    private readonly IScreenFillHost _host;
    private Transform? _found;
    private int _searchCooldown;
    private Transform? _overlay;
    private Vector3 _originalPosition;
    private Vector3 _originalScale;
    private Vector3 _writtenPosition;
    private Vector3 _writtenScale;
    private bool _adjusted;

    public TouchDayChangeOverlay(IScreenFillHost host) => _host = host;

    public void Place(Camera camera, Vector3 lift, float stretch)
    {
        if (lift == Vector3.zero && stretch == 1f)
        {
            Restore();
            return;
        }
        var overlay = Find();
        if (overlay == null)
            return;
        if (overlay != _overlay)
        {
            _overlay = overlay;
            _originalPosition = _writtenPosition = overlay.localPosition;
            _originalScale = _writtenScale = overlay.localScale;
        }
        else
        {
            if (overlay.localPosition != _writtenPosition)
                _originalPosition = overlay.localPosition;
            if (overlay.localScale != _writtenScale)
                _originalScale = overlay.localScale;
        }
        if (overlay.localPosition != _originalPosition)
            overlay.localPosition = _originalPosition;
        if (overlay.localScale != _originalScale)
            overlay.localScale = _originalScale;
        _adjusted = false;
        _writtenPosition = _originalPosition;
        _writtenScale = _originalScale;
        if (!overlay.gameObject.activeInHierarchy)
            return;
        // Scaled about the camera's axis, not its eye: a uniform scale about the eye draws the
        // same picture.
        var view = camera.transform;
        var relative = view.InverseTransformPoint(overlay.position + lift);
        relative.x *= stretch;
        relative.y *= stretch;
        overlay.position = view.TransformPoint(relative);
        overlay.localScale = _originalScale * stretch;
        _writtenPosition = overlay.localPosition;
        _writtenScale = overlay.localScale;
        _adjusted = true;
    }

    private void Restore()
    {
        if (!_adjusted)
            return;
        if (Il2CppUi.IsAlive(_overlay))
        {
            if (_overlay.localPosition == _writtenPosition && _writtenPosition != _originalPosition)
                _overlay.localPosition = _originalPosition;
            if (_overlay.localScale == _writtenScale && _writtenScale != _originalScale)
                _overlay.localScale = _originalScale;
        }
        _adjusted = false;
    }

    private Transform? Find()
    {
        if (Il2CppUi.IsAlive(_found))
            return _found;
        _found = null;
        _overlay = null;
        if (_searchCooldown-- > 0)
            return null;
        _searchCooldown = SearchEvery;
        _found = _host.FindDayChangeOverlay();
        return Il2CppUi.IsAlive(_found) ? _found : null;
    }
}
