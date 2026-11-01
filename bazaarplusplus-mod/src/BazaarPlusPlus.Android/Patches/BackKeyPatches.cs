using BazaarPlusPlus.Android.Host;
using HarmonyLib;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.Patches;

/// <summary>
/// The game's escape handling, where the Android back key arrives: <c>HandleEscape</c> closes the
/// top popup or opens the escape menu, and <c>OnOpenSettings</c> is the menu input's own path to
/// it. While a BPP panel is open, <see cref="AndroidOverlayPanels"/> closes it
/// instead and the game's handler is skipped for that press. Outside Enabled mode every press goes to the game.
/// </summary>
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
