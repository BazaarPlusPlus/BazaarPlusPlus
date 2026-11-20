using BazaarPlusPlus.Android.Settings;
using HarmonyLib;
using Il2Cpp;
using Il2CppTheBazaar.UI.Components;

namespace BazaarPlusPlus.Android.Patches;

[HarmonyPatch(typeof(ScrollSpyController), nameof(ScrollSpyController.Awake))]
internal static class NativeSettingsScrollSpyAwakePatch
{
    [HarmonyPrefix]
    private static void Prefix(ScrollSpyController __instance)
    {
        if (AndroidPatchHost.Mode != AndroidPatchMode.Enabled)
            return;
        try
        {
            AndroidNativeSettingsSection.TryInstall(__instance);
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("settings_section_install", exception);
        }
    }
}

[HarmonyPatch(typeof(OptionsDialogController), nameof(OptionsDialogController.OnEnable))]
internal static class NativeSettingsDialogEnablePatch
{
    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        if (AndroidPatchHost.Mode != AndroidPatchMode.Enabled)
            return;
        try
        {
            AndroidNativeSettingsSection.Opened(__instance);
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("settings_section_refresh", exception);
        }
    }
}

[HarmonyPatch(
    typeof(OptionsDialogController),
    nameof(OptionsDialogController.OnLanguageOptionChanged)
)]
internal static class NativeSettingsLanguagePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (AndroidPatchHost.Mode != AndroidPatchMode.Enabled)
            return;
        try
        {
            AndroidNativeSettingsSection.RefreshAll();
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("settings_section_language", exception);
        }
    }
}
