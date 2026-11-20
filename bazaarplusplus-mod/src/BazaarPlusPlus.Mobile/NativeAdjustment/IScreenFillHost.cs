using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.NativeAdjustment;

// Main-thread adapter for game types the Mobile UI cannot reference.
internal interface IScreenFillHost
{
    // The native letterbox recomputes its crop during its next update.
    bool LowerLetterBox(Vector2Int floor, out Vector2Int previous);

    IEnumerable<CanvasScaler> CanvasScalers();

    Transform? BoardCenter();

    Transform? FindDayClock();

    // NewDayTransition's root is active only while its animation plays.
    Transform? FindDayChangeOverlay();
}
