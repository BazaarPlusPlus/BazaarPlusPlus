using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTheBazaar.UI;
using UnityEngine;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// Lets the game fill a screen narrower than 16:10. <c>LetterBoxManager</c> clamps the target
/// aspect ratio to <c>[_minResolution, _maxResolution]</c> (16:10 to 21:9) and crops everything
/// outside it: the camera rects, the root canvases' children and two black bars. On a foldable's
/// inner display (OPPO Find N6, 2480×2248, about 1.1:1) that crop drops a third of the height.
/// Lowering the floor to 1:1 makes the game use the screen's own ratio for any landscape screen;
/// the 21:9 ceiling stays. The game re-reads the floor only when the screen size changes, so the
/// write also clears <c>_lastScreenSize</c> and the next <c>LetterBoxManager.Update</c> recomputes
/// the crop and raises <c>ScreenSizeChanged</c> to every letterbox listener. Checked every frame,
/// because the class's static initializer may run after the first write.
/// The 3D cameras keep a fixed vertical field of view, so a narrower screen would cut the board's
/// sides; <see cref="WidenFieldOfView"/> draws the board <see cref="BoardZoom"/> times nearer than
/// 16:10 does, showing more above and below, and <see cref="AndroidScreenFillClock"/> moves the day
/// clock the trimmed sides held. Enabled mode only; main thread only.
/// </summary>
internal static class AndroidScreenFill
{
    // The game's own floor: every layout was made for 16:10 or wider.
    public const float ReferenceAspect = 16f / 10f;
    private static readonly Vector2Int Floor = new(1, 1);

    // How much nearer than the 16:10 view the board is drawn. 16:10 frames the board with a band
    // of scenery on each side, about a quarter of its width; this trims most of it, leaving a
    // margin, and still shows the board's full height on a nearly square screen.
    private const float BoardZoom = 1.3f;

    // The least the vertical view grows: the board fills about 97% of the game's own view, and
    // this leaves about an eighth of the screen above it for the HUD band. On a screen between
    // 4:3 and 16:10 it sets the zoom rather than BoardZoom does.
    private const float MinStretch = 1.12f;

    // Of the screen's height, what is kept clear under the lowered board.
    private const float BottomMargin = 0.012f;

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
    /// Called after Cinemachine's brain updated <paramref name="camera"/> this frame: on a
    /// perspective camera narrower than 16:10, raises the vertical field of view until the
    /// horizontal one is <see cref="BoardZoom"/> times narrower than what 16:10 shows, but by at
    /// least <see cref="MinStretch"/>; then lowers the board to the screen's bottom edge and places the day clock in
    /// the band left above it. The brain writes the lens again on most frames;
    /// a frame it skipped still holds the value written here, which is left alone so the change
    /// never compounds.
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
    /// Raises the camera until the board's bottom edge (the vault's) sits a margin above the
    /// screen's, so the room left above and below the nearer board is one band above it, and
    /// gives the board's top-left corner in screen pixels down from the top. The board's
    /// top-left corner mirrors the vault, its bottom-right, through
    /// <c>BoardManager.boardCenter</c>. As with the lens, a frame on which the brain did not
    /// move the camera still holds the position written here and is not raised again.
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
            var floor = Screen.height - Screen.safeArea.yMin - Screen.height * BottomMargin;
            var drop = floor - vault.Bottom;
            if (drop > 0f)
            {
                var perPixel =
                    2f
                    * depth
                    * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad)
                    / Screen.height;
                transform.position += transform.up * (drop * perPixel);
                if (!Vault.TryRead(Screen.height, out vault))
                    return false;
            }
            Raised[key] = transform.position;
        }
        var middle = camera.WorldToScreenPoint(center.position);
        if (middle.z <= 0f)
            return false;
        topLeft = new(2f * middle.x - vault.Right, 2f * (Screen.height - middle.y) - vault.Bottom);
        return true;
    }

    // True when the camera draws the board nearer than 16:10 does.
    private static bool WidenFieldOfView(Camera camera)
    {
        if (AndroidPatchHost.Mode != AndroidPatchMode.Enabled)
            return false;
        if (camera.orthographic || camera.usePhysicalProperties)
            return false;
        var aspect = camera.aspect;
        if (aspect <= 0f || aspect >= ReferenceAspect)
            return false;
        var key = camera.Pointer;
        var fieldOfView = camera.fieldOfView;
        if (Widened.TryGetValue(key, out var written) && written == fieldOfView)
            return true;
        var halfTan = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
        var stretch = Math.Max(MinStretch, ReferenceAspect / (aspect * BoardZoom));
        var widened = 2f * Mathf.Atan(halfTan * stretch) * Mathf.Rad2Deg;
        camera.fieldOfView = widened;
        // Read back: the camera clamps the value it stores.
        Widened[key] = camera.fieldOfView;
        return true;
    }
}
