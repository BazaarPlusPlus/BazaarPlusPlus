#nullable enable
using BazaarPlusPlus.Mobile.NativeAdjustment;
using TheBazaar;
using TheBazaar.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.GameInterop.Touch;

/// <summary>
/// The Mono host's game objects for the Mobile UI's Screen Fill (<see cref="TouchScreenFill"/>) on
/// a mobile platform. <c>LetterBoxManager</c> clamps the aspect ratio it draws to
/// <c>[_minResolution, _maxResolution]</c> (16:10 to 21:9) and crops everything outside it: the
/// camera rects, the root canvases' children and two black bars, which on a 4:3 iPad drop a sixth
/// of the height. The game re-reads the floor only when the screen size changes, so a write also
/// clears <c>_lastScreenSize</c>: the next <c>LetterBoxManager.Update</c> recomputes the crop and
/// raises <c>ScreenSizeChanged</c> to every letterbox listener. Main thread only.
/// </summary>
internal sealed class TouchScreenFillHost : IScreenFillHost
{
    public bool LowerLetterBox(Vector2Int floor, out Vector2Int previous)
    {
        previous = LetterBoxManager._minResolution;
        if (previous == floor)
            return false;
        LetterBoxManager._minResolution = floor;
        LetterBoxManager._lastScreenSize = Vector2Int.zero;
        return true;
    }

    public IEnumerable<CanvasScaler> CanvasScalers() =>
        UnityEngine.Object.FindObjectsByType<CanvasScaler>(FindObjectsSortMode.None);

    public Transform? BoardCenter() => global::BoardManager.Instance?.BoardCenter;

    public Transform? FindDayClock()
    {
        var view = UnityEngine.Object.FindFirstObjectByType<ClockViewController>();
        return view != null ? view.transform : null;
    }
}
