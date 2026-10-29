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
/// sides; <see cref="WidenFieldOfView"/> keeps the 16:10 horizontal view and shows more above and
/// below instead. Enabled mode only; main thread only.
/// </summary>
internal static class AndroidScreenFill
{
    // The game's own floor: every layout was made for 16:10 or wider.
    private const float ReferenceAspect = 16f / 10f;
    private static readonly Vector2Int Floor = new(1, 1);
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
    /// horizontal one matches what 16:10 shows. The brain writes the lens again on most frames;
    /// a frame it skipped still holds the value written here, which is left alone so the change
    /// never compounds.
    /// </summary>
    public static void WidenFieldOfView(Camera? camera)
    {
        if (AndroidPatchHost.Mode != AndroidPatchMode.Enabled || camera == null)
            return;
        if (camera.orthographic || camera.usePhysicalProperties)
            return;
        var aspect = camera.aspect;
        if (aspect <= 0f || aspect >= ReferenceAspect)
            return;
        var key = camera.Pointer;
        var fieldOfView = camera.fieldOfView;
        if (Widened.TryGetValue(key, out var written) && written == fieldOfView)
            return;
        var halfTan = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
        var widened = 2f * Mathf.Atan(halfTan * ReferenceAspect / aspect) * Mathf.Rad2Deg;
        camera.fieldOfView = widened;
        // Read back: the camera clamps the value it stores.
        Widened[key] = camera.fieldOfView;
    }
}
