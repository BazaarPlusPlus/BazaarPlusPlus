#pragma warning disable CS0436
using System;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.Settings;
using HarmonyLib;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(OptionsDialogController), "OnLanguageOptionChanged")]
internal static class OptionsDialogLanguageRefreshPatch
{
    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        try
        {
            BppSettingsDockController.RefreshAll();
            BppKeybindSettingsAwakePatch.RefreshLanguage(__instance);
            NativeKeybindLabelAwakePatch.TryUpdateLabels(__instance);
            HistoryPanel.RefreshLocalization();
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "SettingsMenu",
                "Failed to refresh custom settings after language changed",
                ex
            );
        }
    }
}
