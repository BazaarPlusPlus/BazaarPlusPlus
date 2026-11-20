#nullable enable
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI;
using TheBazaar.UI.Settings;
using UnityEngine;

namespace BazaarPlusPlus.Patches.Settings;

[HarmonyPatch(typeof(OptionsDialogController), "ApplyDisplayFromPrefs")]
internal static class FullscreenResolutionPatch
{
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions
    )
    {
        var codes = instructions.ToList();
        var setResolution = AccessTools.Method(
            typeof(Screen),
            nameof(Screen.SetResolution),
            [typeof(int), typeof(int), typeof(FullScreenMode)]
        );
        var calls = codes.Where(code => code.Calls(setResolution)).ToArray();
        if (calls.Length != 1)
            throw new InvalidOperationException("Native display resolution call changed.");

        calls[0].operand = AccessTools.Method(
            typeof(FullscreenResolutionPatch),
            nameof(SetResolution)
        );
        return codes;
    }

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

[HarmonyPatch(
    typeof(OptionsDialogSettingsAsset),
    nameof(OptionsDialogSettingsAsset.FindCurrentResolutionIndex)
)]
internal static class FullscreenResolutionIndexPatch
{
    [HarmonyPrefix]
    private static bool Prefix(OptionsDialogSettingsAsset __instance, ref int __result)
    {
        if (Screen.fullScreenMode != FullScreenMode.FullScreenWindow)
            return true;

        var index = PlayerPreferences.Data.WindowResolutionIndex;
        var resolutions = __instance.SupportedResolutions;
        if (index < 0 || index >= resolutions.Count)
            return true;

        var selected = resolutions[index];
        if (selected.x <= 0 || selected.y <= 0)
            return true;

        // Awake writes this index back to the preferences shared with windowed mode.
        if (
            FullscreenResolutionPatch.FitGameAspect(selected.x, selected.y)
            != (Screen.width, Screen.height)
        )
            return true;

        __result = index;
        return false;
    }
}
