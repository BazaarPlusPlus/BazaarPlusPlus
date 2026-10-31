using BazaarPlusPlus.Core.Ui;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.NativeAdjustment;

/// <summary>
/// The game's own canvases on a screen <see cref="TouchScreenFill"/> lets it fill. They scale with
/// the screen's height alone (a 3840×2160 reference, <c>matchWidthOrHeight</c> 1; tooltips aside),
/// so a screen narrower than 16:10 gives them less width than any layout was made for: on a
/// foldable's 2480×2248 inner display the main canvas is 2382 units wide where its panels expect
/// 3456 (16:10) or more, and the settings menu, the hero picker and the like run off both sides.
/// Each canvas Core's <see cref="ScreenFill.TakesCanvas"/> accepts gets
/// <see cref="ScreenFill.GameCanvasMatch"/>: as wide as 16:10 makes it, and at 16:10 or wider
/// back as the game set it. Main thread only.
/// </summary>
internal sealed class TouchScreenFillCanvases
{
    private readonly IScreenFillHost _host;

    // The match each canvas had before it was moved, by instance.
    private readonly Dictionary<int, float> _original = new();

    public TouchScreenFillCanvases(IScreenFillHost host) => _host = host;

    /// <summary>Sets every game canvas's match for a screen of <paramref name="aspect"/>.</summary>
    public void Apply(float aspect)
    {
        foreach (var scaler in _host.CanvasScalers())
        {
            if (
                !MobilePlatform.Port.IsAlive(scaler)
                || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize
            )
                continue;
            var id = scaler.GetInstanceID();
            if (!_original.TryGetValue(id, out var original))
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
                _original[id] = original;
            }
            var match = ScreenFill.GameCanvasMatch(aspect, original);
            if (Math.Abs(scaler.matchWidthOrHeight - match) > 0.001f)
                scaler.matchWidthOrHeight = match;
        }
    }
}
