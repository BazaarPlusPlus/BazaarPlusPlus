#nullable enable
#pragma warning disable CS0436
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
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
                new BppLogEvent(
                    BppLogFeatureScope.Settings,
                    "settings.patch.degraded",
                    storm: ["operation", "reason_code"]
                ),
                ex,
                ("operation", SettingsPatchOperation.LanguageRefresh),
                ("reason_code", SettingsLogReasonCode.PatchException)
            );
        }
    }
}
