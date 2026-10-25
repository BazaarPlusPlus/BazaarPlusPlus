using HarmonyLib;
using Il2CppTheBazaar;
using Il2CppTheBazaar.Tooltips;
using Il2CppTheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Android.Patches;

[HarmonyPatch(typeof(TooltipQuestEntry), nameof(TooltipQuestEntry.SetData))]
internal static class QuestPreviewEntryPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        TooltipQuestEntry __instance,
        CardQuestEntryData entry,
        CardTooltipData currentTooltipData
    )
    {
        try
        {
            AndroidPatchHost.QuestPreview?.Render(__instance, entry, currentTooltipData);
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("quest_preview", exception);
        }
    }
}

[HarmonyPatch(typeof(TooltipQuestGroup), nameof(TooltipQuestGroup.SetData))]
internal static class QuestPreviewLayoutPatch
{
    [HarmonyPostfix]
    private static void Postfix(TooltipQuestGroup __instance)
    {
        try
        {
            __instance.ForceRebuildLayout();
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("quest_preview_layout", exception);
        }
    }
}
