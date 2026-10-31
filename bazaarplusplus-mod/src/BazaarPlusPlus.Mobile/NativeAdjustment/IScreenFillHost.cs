using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.NativeAdjustment;

/// <summary>
/// A host's read and write of the game objects <see cref="TouchScreenFill"/> adjusts (ADR-0025
/// decision 5): the letterbox, the board and the day clock are game types each host names its own
/// way, so it adapts them here. Main thread.
/// </summary>
internal interface IScreenFillHost
{
    /// <summary>
    /// Writes <paramref name="floor"/> as the game's letterbox floor, unless the game already holds
    /// it, so that the next letterbox update recomputes the crop; true with the floor it replaced
    /// when it wrote.
    /// </summary>
    bool LowerLetterBox(Vector2Int floor, out Vector2Int previous);

    /// <summary>Every canvas scaler in the loaded scenes.</summary>
    IEnumerable<CanvasScaler> CanvasScalers();

    /// <summary>The board's centre (<c>BoardManager</c>'s), or null with no board.</summary>
    Transform? BoardCenter();

    /// <summary>The day clock, the encounter prefab <c>ClockViewController</c> sits on, or null.</summary>
    Transform? FindDayClock();
}
