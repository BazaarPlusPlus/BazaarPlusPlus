using BazaarPlusPlus.Core.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The game's own canvases on a screen <see cref="AndroidScreenFill"/> lets it fill. They scale
/// with the screen's height alone (a 3840×2160 reference, <c>matchWidthOrHeight</c> 1; tooltips
/// aside), so a nearly square screen gives them far less width than any layout was made for: on
/// 2480×2248 the main canvas is 2382 units wide where its panels expect 3456 (16:10) or more, and
/// the settings menu, the hero picker and the like run off both sides. Each canvas Core's
/// <see cref="ScreenFill.TakesCanvas"/> accepts gets <see cref="ScreenFill.GameCanvasMatch"/>:
/// as wide as 16:10 makes it, and at 16:10 or wider (the fold closed) back as the game set it.
/// Looked at every <see cref="Every"/> frames, as canvases come with scenes and popups. Main
/// thread only.
/// </summary>
internal static class AndroidScreenFillCanvases
{
    private const int Every = 30;

    // The match each canvas had before it was moved, by instance.
    private static readonly Dictionary<int, float> Original = new();

    private static int _frame;

    public static void Tick()
    {
        if (++_frame % Every != 0)
            return;
        var aspect = ScreenFill.Aspect(Screen.width, Screen.height);
        foreach (var scaler in UnityEngine.Object.FindObjectsOfType<CanvasScaler>())
        {
            if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
                continue;
            var id = scaler.GetInstanceID();
            if (!Original.TryGetValue(id, out var original))
            {
                var reference = scaler.referenceResolution;
                original = scaler.matchWidthOrHeight;
                if (
                    !ScreenFill.TakesCanvas(
                        reference.x,
                        reference.y,
                        original,
                        scaler.gameObject.name
                    )
                )
                    continue;
                Original[id] = original;
            }
            var match = ScreenFill.GameCanvasMatch(aspect, original);
            if (Math.Abs(scaler.matchWidthOrHeight - match) > 0.001f)
                scaler.matchWidthOrHeight = match;
        }
    }
}
