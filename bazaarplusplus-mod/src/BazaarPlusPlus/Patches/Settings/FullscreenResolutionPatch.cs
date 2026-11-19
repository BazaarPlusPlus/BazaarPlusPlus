#nullable enable
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI;
using UnityEngine;

namespace BazaarPlusPlus.Patches.Settings;

[HarmonyPatch(typeof(OptionsDialogController), "ApplyDisplayFromPrefs")]
internal static class FullscreenResolutionPatch
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions
    ) =>
        instructions.MethodReplacer(
            AccessTools.Method(
                typeof(Screen),
                nameof(Screen.SetResolution),
                [typeof(int), typeof(int), typeof(FullScreenMode)]
            ),
            AccessTools.Method(typeof(FullscreenResolutionPatch), nameof(SetResolution))
        );

    private static void SetResolution(int width, int height, FullScreenMode mode)
    {
        if (mode == FullScreenMode.FullScreenWindow && width > 0 && height > 0)
        {
            (width, height) = FitGameAspect(width, height);
        }

        Screen.SetResolution(width, height, mode);
    }

    // Render only the dimensions the game would have used for its content. Unity can then
    // fit that content to the fullscreen area without scaling up the game's own bars.
    internal static (int Width, int Height) FitGameAspect(int width, int height)
    {
        var size = new Vector2Int(width, height);
        var target = AspectRatioCalculator.GetTargetAspectRatio(
            new Vector2(width, height),
            LetterBoxManager.MinResolution,
            LetterBoxManager.MaxResolution
        );
        if (target == AspectRatioCalculator.GetCurrentRatio(size))
        {
            return (width, height);
        }

        return (long)width * target.y < (long)height * target.x
            ? (width, Math.Max(1, (int)((long)width * target.y / target.x)))
            : (Math.Max(1, (int)((long)height * target.x / target.y)), height);
    }
}
