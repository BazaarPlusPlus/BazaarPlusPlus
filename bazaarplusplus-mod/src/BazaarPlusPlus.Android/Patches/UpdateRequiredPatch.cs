using HarmonyLib;
using Il2CppNetworking;

namespace BazaarPlusPlus.Android.Patches;

/// <summary>
/// The game's own outdated-client gate: <c>StartRunAppState.CheckClientVersion</c> asks
/// <c>IsUpdateRequired</c> as a run starts and, on true, returns to the main menu with a message
/// that sends the player to a store. The store updates the official game, but BPP runs it only
/// when the Android App starts it, so the answer tells <see cref="Lobby.AndroidUpdatePrompt"/>
/// to add that step once that popup closes.
/// </summary>
[HarmonyPatch(typeof(ServersHealthService), nameof(ServersHealthService.IsUpdateRequired))]
internal static class UpdateRequiredPatch
{
    [HarmonyPostfix]
    private static void Postfix(bool __result)
    {
        if (__result && AndroidPatchHost.Mode == AndroidPatchMode.Enabled)
            AndroidPatchHost.UpdatePrompt?.GameRequiresUpdate();
    }
}
