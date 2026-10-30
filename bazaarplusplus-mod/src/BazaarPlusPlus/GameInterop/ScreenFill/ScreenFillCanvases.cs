#nullable enable
using UnityEngine;
using UnityEngine.UI;
using Fill = BazaarPlusPlus.Core.Ui.ScreenFill;

namespace BazaarPlusPlus.GameInterop.ScreenFill;

/// <summary>
/// The game's own canvases on a screen Screen Fill lets it fill. They scale with the screen's
/// height alone (a 3840×2160 reference, <c>matchWidthOrHeight</c> 1; tooltips aside), so a screen
/// narrower than 16:10 gives them less width than any layout was made for, and the settings menu,
/// the hero picker and the like run off both sides. Each canvas Core's <see cref="Fill.TakesCanvas"/>
/// accepts gets <see cref="Fill.GameCanvasMatch"/>: as wide as 16:10 makes it, and at 16:10 or
/// wider back as the game set it. Main thread only.
/// </summary>
internal static class ScreenFillCanvases
{
    // The match each canvas had before it was moved, by instance.
    private static readonly Dictionary<int, float> Original = new();

    /// <summary>Sets every game canvas's match for a screen of <paramref name="aspect"/>.</summary>
    public static void Apply(float aspect)
    {
        foreach (
            var scaler in UnityEngine.Object.FindObjectsByType<CanvasScaler>(
                FindObjectsSortMode.None
            )
        )
        {
            if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
                continue;
            var id = scaler.GetInstanceID();
            if (!Original.TryGetValue(id, out var original))
            {
                var reference = scaler.referenceResolution;
                original = scaler.matchWidthOrHeight;
                if (!Fill.TakesCanvas(reference.x, reference.y, original, scaler.gameObject.name))
                    continue;
                Original[id] = original;
            }
            var match = Fill.GameCanvasMatch(aspect, original);
            if (Math.Abs(scaler.matchWidthOrHeight - match) > 0.001f)
                scaler.matchWidthOrHeight = match;
        }
    }
}
