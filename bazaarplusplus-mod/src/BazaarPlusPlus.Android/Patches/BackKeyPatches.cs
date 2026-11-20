using HarmonyLib;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.Patches;

// Unity sends the Android back key through the game's escape handlers.
internal static class BackKeyPatches
{
    private static bool Consume(string source) =>
        AndroidPatchHost.Mode == AndroidPatchMode.Enabled
        && AndroidPatchHost.OverlayPanels?.TryConsumeBack(source) == true;

    [HarmonyPatch(typeof(PopupManager), nameof(PopupManager.HandleEscape))]
    internal static class HandleEscapePatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => !Consume("handle_escape");
    }

    [HarmonyPatch(typeof(PopupManager), nameof(PopupManager.OnOpenSettings))]
    internal static class OpenSettingsPatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => !Consume("open_settings");
    }
}
