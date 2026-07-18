#nullable enable
#pragma warning disable CS0436
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;

namespace BazaarPlusPlus.Patches.Settings;

[HarmonyPatch(typeof(OptionsDialogController), "OnLanguageOptionChanged")]
internal static class OptionsDialogLanguageRefreshPatch
{
    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        try
        {
            BppNativeSettingsSectionController.RefreshAll();
            BppKeybindSettingsAwakePatch.RefreshLanguage(__instance);
            HistoryPanel.RefreshLocalization();
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                SettingsLogEvents.PatchDegraded,
                ex,
                SettingsLogEvents.PatchDegradedOperation.Bind(
                    SettingsPatchOperation.LanguageRefresh
                ),
                SettingsLogEvents.PatchDegradedReasonCode.Bind(SettingsLogReasonCode.PatchException)
            );
        }
    }
}
