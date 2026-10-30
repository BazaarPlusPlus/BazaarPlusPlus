#nullable enable
using BazaarPlusPlus.GameInterop.Touch;
using UnityEngine;
using Fill = BazaarPlusPlus.Core.Ui.ScreenFill;

namespace BazaarPlusPlus.GameInterop.ScreenFill;

/// <summary>
/// The board camera on a screen Screen Fill lets the game fill: its field of view and position,
/// written after Cinemachine's brain wrote them for the frame. The brain writes the lens and the
/// position again on most frames; a frame it skipped still holds the value written here, which is
/// left alone so the change never compounds. Main thread only.
/// </summary>
internal sealed class ScreenFillCamera
{
    private readonly TouchVaultBounds _vault = new();

    // The field of view last written to each camera, by instance.
    private readonly Dictionary<int, float> _widened = new();

    // The position last written to each camera, by instance.
    private readonly Dictionary<int, Vector3> _raised = new();

    /// <summary>
    /// Widens a perspective camera's field of view on a screen narrower than 16:10
    /// (<see cref="Fill.WidenedFieldOfView"/>); true when the HUD gathers above the board there.
    /// </summary>
    public bool Widen(Camera camera)
    {
        if (camera.orthographic || camera.usePhysicalProperties)
            return false;
        var aspect = camera.aspect;
        if (!Fill.Fills(aspect))
            return false;
        var key = camera.GetInstanceID();
        var fieldOfView = camera.fieldOfView;
        var above = Fill.GathersHudAbove(aspect);
        if (_widened.TryGetValue(key, out var written) && written == fieldOfView)
            return above;
        camera.fieldOfView = Fill.WidenedFieldOfView(fieldOfView, aspect);
        // Read back: the camera clamps the value it stores.
        _widened[key] = camera.fieldOfView;
        return above;
    }

    /// <summary>
    /// Raises the camera by <see cref="Fill.BoardDrop"/>, so the room left above and below the
    /// nearer board is one band above it, and gives the board's top-left corner in screen pixels
    /// down from the top (<see cref="Fill.BoardTopLeft"/>: the vault's bottom-right mirrored
    /// through <c>BoardManager.boardCenter</c>).
    /// </summary>
    public bool TryLowerBoard(Camera camera, out Vector2 topLeft)
    {
        topLeft = default;
        var center = global::BoardManager.Instance?.BoardCenter;
        if (center == null || !_vault.TryRead(Screen.height, out var vault))
            return false;
        var transform = camera.transform;
        var key = camera.GetInstanceID();
        if (!_raised.TryGetValue(key, out var written) || written != transform.position)
        {
            var depth = camera.WorldToScreenPoint(center.position).z;
            if (depth <= 0f)
                return false;
            var drop = Fill.BoardDrop(Screen.height, Screen.safeArea.yMin, vault.Bottom);
            if (drop > 0f)
            {
                transform.position +=
                    transform.up
                    * (drop * Fill.WorldUnitsPerPixel(depth, camera.fieldOfView, Screen.height));
                if (!_vault.TryRead(Screen.height, out vault))
                    return false;
            }
            _raised[key] = transform.position;
        }
        var middle = camera.WorldToScreenPoint(center.position);
        if (middle.z <= 0f)
            return false;
        var (x, y) = Fill.BoardTopLeft(
            middle.x,
            Screen.height - middle.y,
            vault.Right,
            vault.Bottom
        );
        topLeft = new Vector2(x, y);
        return true;
    }
}
