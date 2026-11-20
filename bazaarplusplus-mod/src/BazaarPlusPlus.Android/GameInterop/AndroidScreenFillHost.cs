using BazaarPlusPlus.Mobile.NativeAdjustment;
using Il2CppTheBazaar;
using Il2CppTheBazaar.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop;

// LetterBoxManager rereads the aspect floor only on a resize. Clear its cached screen size to
// refresh every listener.
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

    public Transform? FindDayChangeOverlay()
    {
        var view = UnityEngine.Object.FindObjectOfType<Il2Cpp.NewDayTransition>(true);
        var root = view != null ? view.rootGameObject : null;
        return root != null ? root.transform : null;
    }
}
