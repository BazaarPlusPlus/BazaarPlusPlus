#nullable enable
#pragma warning disable CS0436
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar.UI.Components;

namespace BazaarPlusPlus.Patches.Settings;

[HarmonyPatch(typeof(ScrollSpyController), "Awake")]
internal static class BppNativeSettingsScrollSpyAwakePatch
{
    [HarmonyPrefix]
    private static void Prefix(ScrollSpyController __instance)
    {
        try
        {
            BppNativeSettingsSectionController.TryInstall(__instance);
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                SettingsLogEvents.PatchDegraded,
                ex,
                SettingsLogEvents.PatchDegradedOperation.Bind(
                    SettingsPatchOperation.NativeSectionInstall
                ),
                SettingsLogEvents.PatchDegradedReasonCode.Bind(SettingsLogReasonCode.PatchException)
            );
        }
    }
}

[HarmonyPatch(typeof(OptionsDialogController), "OnEnable")]
internal static class BppNativeSettingsOnEnablePatch
{
    [HarmonyPostfix]
    private static void Postfix() => BppNativeSettingsSectionController.RefreshAll();
}
