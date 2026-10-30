#nullable enable
using TheBazaar.UI;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.ScreenFill;

/// <summary>
/// The game's letterbox floor (ADR-0025 decision 5, the host's read and write). <c>LetterBoxManager</c>
/// clamps the aspect ratio it draws to <c>[_minResolution, _maxResolution]</c> (16:10 to 21:9) and
/// crops everything outside it: the camera rects, the root canvases' children and two black bars,
/// which on a 4:3 iPad drop a sixth of the height. The game re-reads the floor only when the screen
/// size changes, so a write also clears <c>_lastScreenSize</c>: the next
/// <c>LetterBoxManager.Update</c> recomputes the crop and raises <c>ScreenSizeChanged</c> to every
/// letterbox listener. Main thread only.
/// </summary>
internal static class ScreenFillLetterBox
{
    /// <summary>
    /// Writes <paramref name="floor"/> unless the game already holds it; true with the floor it
    /// replaced when it wrote.
    /// </summary>
    public static bool Lower(Vector2Int floor, out Vector2Int previous)
    {
        previous = LetterBoxManager._minResolution;
        if (previous == floor)
            return false;
        LetterBoxManager._minResolution = floor;
        LetterBoxManager._lastScreenSize = Vector2Int.zero;
        return true;
    }
}
