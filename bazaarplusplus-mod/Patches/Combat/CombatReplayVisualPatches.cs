#pragma warning disable CS0436
#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.CombatReplay;
using HarmonyLib;
using TheBazaar.UI.EncounterPicker;
using UnityEngine;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(EncounterPickerMapController), nameof(EncounterPickerMapController.Show))]
internal static class CombatReplayEncounterPickerMapShowPatch
{
    [HarmonyPrefix]
    private static bool Prefix(EncounterPickerMapController __instance)
    {
        if (CombatReplayRuntime.Instance?.IsSavedReplayPlaybackActive != true)
            return true;

        CombatReplayRuntime.HideEncounterPickerOverlays();
        if (__instance != null)
            __instance.gameObject.SetActive(false);
        return false;
    }
}

[HarmonyPatch]
internal static class CombatReplayInjectedEncounterPickerMapShowPatch
{
    private static MethodBase? TargetMethod()
    {
        var type =
            AccessTools.TypeByName("InjectedEncounterPickerMapController")
            ?? AccessTools.TypeByName("TheBazaar_InjectedEncounterPickerMapController")
            ?? AccessTools.TypeByName("TheBazaar.InjectedEncounterPickerMapController");
        return type == null ? null : AccessTools.Method(type, "PlayEnter");
    }

    [HarmonyPrefix]
    private static bool Prefix(MonoBehaviour __instance)
    {
        if (CombatReplayRuntime.Instance?.IsSavedReplayPlaybackActive != true)
            return true;

        CombatReplayRuntime.HideEncounterPickerOverlays();
        if (__instance != null)
            __instance.gameObject.SetActive(false);
        return false;
    }
}

[HarmonyPatch]
internal static class CombatReplayClockInjectedEncounterPickerPatch
{
    private static MethodBase? TargetMethod()
    {
        var type =
            AccessTools.TypeByName("ClockInjectedEncounterController")
            ?? AccessTools.TypeByName("TheBazaar_ClockInjectedEncounterController")
            ?? AccessTools.TypeByName("TheBazaar.ClockInjectedEncounterController");
        return type == null ? null : AccessTools.Method(type, "SpawnEncounterPickerVFX");
    }

    [HarmonyPrefix]
    private static bool Prefix()
    {
        if (CombatReplayRuntime.Instance?.IsSavedReplayPlaybackActive != true)
            return true;

        CombatReplayRuntime.HideEncounterPickerOverlays();
        return false;
    }
}
