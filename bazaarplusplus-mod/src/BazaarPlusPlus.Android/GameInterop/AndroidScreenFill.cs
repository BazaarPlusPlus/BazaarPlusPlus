using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTheBazaar.UI;
using UnityEngine;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// Screen Fill on the Android host: Core's <see cref="ScreenFill"/> decides, this reads and
/// writes the game. <c>LetterBoxManager</c> clamps the target aspect ratio to
/// <c>[_minResolution, _maxResolution]</c> (16:10 to 21:9) and crops everything outside it: the
/// camera rects, the root canvases' children and two black bars. On a foldable's inner display
/// (OPPO Find N6, 2480×2248, about 1.1:1) that crop drops a third of the height. Lowering the
/// floor to 1:1 makes the game use the screen's own ratio for any landscape screen; the 21:9
/// ceiling stays. The game re-reads the floor only when the screen size changes, so the write also
/// clears <c>_lastScreenSize</c> and the next <c>LetterBoxManager.Update</c> recomputes the crop
/// and raises <c>ScreenSizeChanged</c> to every letterbox listener. Checked every frame, because
/// the class's static initializer may run after the first write. After each camera update the
/// board is drawn nearer (<see cref="ScreenFill.WidenedFieldOfView"/>), lowered, and
/// <see cref="AndroidScreenFillClock"/> moves the day clock the trimmed sides held. Enabled mode
/// only; main thread only.
/// </summary>
internal static class AndroidScreenFill
{
    private static readonly Vector2Int Floor = new(ScreenFill.FloorWidth, ScreenFill.FloorHeight);

    private static readonly AndroidScreenFillClock Clock = new();
    private static readonly AndroidVaultBounds Vault = new();

    // The position last written to each camera, by native pointer.
    private static readonly Dictionary<IntPtr, Vector3> Raised = new();
    private static bool _logged;

    // The field of view last written to each camera, by native pointer.
    private static readonly Dictionary<IntPtr, float> Widened = new();

    public static void Tick()
    {
        if (AndroidPatchHost.Mode != AndroidPatchMode.Enabled)
            return;
        AndroidScreenFillCanvases.Tick();
        var floor = LetterBoxManager._minResolution;
        if (floor.x == Floor.x && floor.y == Floor.y)
            return;
        LetterBoxManager._minResolution = Floor;
        LetterBoxManager._lastScreenSize = Vector2Int.zero;
        if (_logged)
            return;
        _logged = true;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.screen_fill.applied"),
            ("previous_floor", $"{floor.x}:{floor.y}"),
            ("screen", $"{Screen.width}x{Screen.height}")
        );
    }

    /// <summary>
    /// Called after Cinemachine's brain updated <paramref name="camera"/> this frame: widens its
    /// field of view on a screen narrower than 16:10, then, where the HUD gathers above the
    /// board, lowers the board to the screen's bottom edge and places the day clock in the band
    /// left above it. The brain writes the lens again on most frames; a frame it skipped still
    /// holds the value written here, which is left alone so the change never compounds.
    /// </summary>
    public static void AfterCameraUpdate(Camera? camera)
    {
        if (camera == null)
            return;
        Vector2? corner =
            WidenFieldOfView(camera) && TryLowerBoard(camera, out var topLeft) ? topLeft : null;
        BoardTop = corner?.y;
        Clock.Place(camera, corner);
    }

    /// <summary>
    /// While the board is drawn nearer, its top edge in screen pixels down from the top: the HUD
    /// gathers in the band above it. Null otherwise.
    /// </summary>
    public static float? BoardTop { get; private set; }

    /// <summary>
    /// Raises the camera by <see cref="ScreenFill.BoardDrop"/>, so the room left above and below
    /// the nearer board is one band above it, and gives the board's top-left corner in screen
    /// pixels down from the top (<see cref="ScreenFill.BoardTopLeft"/>, the vault's bottom-right
    /// mirrored through <c>BoardManager.boardCenter</c>). As with the lens, a frame on which the
    /// brain did not move the camera still holds the position written here and is not raised again.
    /// </summary>
    private static bool TryLowerBoard(Camera camera, out Vector2 topLeft)
    {
        topLeft = default;
        var center = Il2Cpp.BoardManager.Instance?.boardCenter;
        if (center == null || !Vault.TryRead(Screen.height, out var vault))
            return false;
        var transform = camera.transform;
        var key = camera.Pointer;
        if (!Raised.TryGetValue(key, out var written) || written != transform.position)
        {
            var depth = camera.WorldToScreenPoint(center.position).z;
            if (depth <= 0f)
                return false;
            var drop = ScreenFill.BoardDrop(Screen.height, Screen.safeArea.yMin, vault.Bottom);
            if (drop > 0f)
            {
                transform.position +=
                    transform.up
                    * (
                        drop
                        * ScreenFill.WorldUnitsPerPixel(depth, camera.fieldOfView, Screen.height)
                    );
                if (!Vault.TryRead(Screen.height, out vault))
                    return false;
            }
            Raised[key] = transform.position;
        }
        var middle = camera.WorldToScreenPoint(center.position);
        if (middle.z <= 0f)
            return false;
        var (x, y) = ScreenFill.BoardTopLeft(
            middle.x,
            Screen.height - middle.y,
            vault.Right,
            vault.Bottom
        );
        topLeft = new(x, y);
        return true;
    }

    // True when the HUD gathers above the board (ScreenFill.GathersHudAbove).
    private static bool WidenFieldOfView(Camera camera)
    {
        if (AndroidPatchHost.Mode != AndroidPatchMode.Enabled)
            return false;
        if (camera.orthographic || camera.usePhysicalProperties)
            return false;
        var aspect = camera.aspect;
        if (!ScreenFill.Fills(aspect))
            return false;
        var key = camera.Pointer;
        var fieldOfView = camera.fieldOfView;
        var above = ScreenFill.GathersHudAbove(aspect);
        if (Widened.TryGetValue(key, out var written) && written == fieldOfView)
            return above;
        camera.fieldOfView = ScreenFill.WidenedFieldOfView(fieldOfView, aspect);
        // Read back: the camera clamps the value it stores.
        Widened[key] = camera.fieldOfView;
        return above;
    }
}
