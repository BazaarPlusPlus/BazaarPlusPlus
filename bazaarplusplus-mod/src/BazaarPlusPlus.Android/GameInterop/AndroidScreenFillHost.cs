using BazaarPlusPlus.Mobile.NativeAdjustment;
using Il2CppTheBazaar;
using Il2CppTheBazaar.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The Android host's game objects for the Mobile UI's Screen Fill (<see cref="TouchScreenFill"/>).
/// <c>LetterBoxManager</c> clamps the target aspect ratio to <c>[_minResolution,
/// _maxResolution]</c> (16:10 to 21:9) and crops everything outside it: the camera rects, the root
/// canvases' children and two black bars. On a foldable's inner display (OPPO Find N6,
/// 2480×2248, about 1.1:1) that crop drops a third of the height. The game re-reads the floor only
/// when the screen size changes, so a write also clears <c>_lastScreenSize</c>: the next
/// <c>LetterBoxManager.Update</c> recomputes the crop and raises <c>ScreenSizeChanged</c> to every
/// letterbox listener. <c>BoardManager</c> is a global-namespace game type, so its proxy is
/// <c>Il2Cpp.BoardManager</c>. Main thread only.
/// </summary>
internal sealed class AndroidScreenFillHost : IScreenFillHost
{
    public bool LowerLetterBox(Vector2Int floor, out Vector2Int previous)
    {
        previous = LetterBoxManager._minResolution;
        if (previous.x == floor.x && previous.y == floor.y)
            return false;
        LetterBoxManager._minResolution = floor;
        LetterBoxManager._lastScreenSize = Vector2Int.zero;
        return true;
    }

    public IEnumerable<CanvasScaler> CanvasScalers() =>
        UnityEngine.Object.FindObjectsOfType<CanvasScaler>();

    public Transform? BoardCenter() => Il2Cpp.BoardManager.Instance?.boardCenter;

    public Transform? FindDayClock()
    {
        var view = UnityEngine.Object.FindObjectOfType<ClockViewController>();
        return view != null ? view.transform : null;
    }
}
