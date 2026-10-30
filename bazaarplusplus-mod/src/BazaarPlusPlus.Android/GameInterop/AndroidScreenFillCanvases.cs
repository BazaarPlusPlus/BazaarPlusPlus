using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The game's own canvases on a screen <see cref="AndroidScreenFill"/> lets it fill. They scale
/// with the screen's height alone (a 3840×2160 reference, <c>matchWidthOrHeight</c> 1; tooltips aside), so a
/// nearly square screen gives them far less width than any layout was made for: on 2480×2248 the
/// main canvas is 2382 units wide where its panels expect 3456 (16:10) or more, and the settings
/// menu, the hero picker and the like run off both sides. Narrower than
/// <see cref="AndroidScreenFill.ReferenceAspect"/>, each such canvas's match moves toward its width
/// just far enough that it is as wide as 16:10 makes it: everything on it shrinks by as much as the
/// width falls short, and nothing is laid out narrower than the game knows. At 16:10 or wider (the
/// fold closed) each is put back as the game set it. Looked at every <see cref="Every"/> frames,
/// as canvases come with scenes and popups. Main thread only.
/// </summary>
internal static class AndroidScreenFillCanvases
{
    private const int Every = 30;

    // The game's canvases are made at this size; BPP's own and the letterbox's bars are not touched.
    private const float ReferenceWidth = 3840f;
    private const float ReferenceHeight = 2160f;

    // The match each canvas had before it was moved, by instance.
    private static readonly Dictionary<int, float> Original = new();

    private static int _frame;

    public static void Tick()
    {
        if (++_frame % Every != 0)
            return;
        var aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 0f;
        var narrow = aspect > 0f && aspect < AndroidScreenFill.ReferenceAspect;
        foreach (var scaler in UnityEngine.Object.FindObjectsOfType<CanvasScaler>())
        {
            if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
                continue;
            var reference = scaler.referenceResolution;
            if (reference.x != ReferenceWidth || reference.y != ReferenceHeight)
                continue;
            // Tooltips keep the size they are read at: they are laid out to fit by their own code.
            if (scaler.gameObject.name.Contains("Tooltip", StringComparison.Ordinal))
                continue;
            var id = scaler.GetInstanceID();
            if (!Original.TryGetValue(id, out var original))
            {
                // Only the canvases that follow the height: one that already weighs the width keeps
                // what the game chose.
                if (scaler.matchWidthOrHeight < 0.99f)
                    continue;
                original = scaler.matchWidthOrHeight;
                Original[id] = original;
            }
            var match = narrow ? MatchFor(aspect) : original;
            if (Math.Abs(scaler.matchWidthOrHeight - match) > 0.001f)
                scaler.matchWidthOrHeight = match;
        }
    }

    /// <summary>
    /// The match at which a 3840×2160 canvas on a screen of
    /// <paramref name="aspect"/> is as wide as on a 16:10 one. CanvasScaler blends the width's and
    /// the height's scales in log space, log s = (1 − m)·log(W/Rw) + m·log(H/Rh); with H = 1 and
    /// the scale wanted s = W/(Rh·16/10), m follows. Between 0 and 1.
    /// </summary>
    public static float MatchFor(float aspect)
    {
        var byWidth = Math.Log(aspect / ReferenceWidth);
        var byHeight = Math.Log(1.0 / ReferenceHeight);
        var wanted = Math.Log(aspect / (ReferenceHeight * AndroidScreenFill.ReferenceAspect));
        return (float)Math.Clamp((wanted - byWidth) / (byHeight - byWidth), 0.0, 1.0);
    }
}
