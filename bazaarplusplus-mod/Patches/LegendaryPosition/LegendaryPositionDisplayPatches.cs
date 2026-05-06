#pragma warning disable CS0436
#nullable enable
using BazaarGameShared.TempoNet.Enums;
using BazaarPlusPlus.Game.LegendaryPosition;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.EndOfRun;
using TMPro;
using UnityEngine.Playables;

namespace BazaarPlusPlus.Patches.LegendaryPosition;

[HarmonyPatch(typeof(HeroBannerController), "SetLeaderboardPosition")]
internal static class HeroBannerSetLeaderboardPositionPatch
{
    [HarmonyPostfix]
    private static void Postfix(HeroBannerController __instance, int? leaderboardPosition)
    {
        var label = Traverse
            .Create(__instance)
            .Field("_leaderboardPositionLabel")
            .GetValue<TMP_Text>();
        if (label == null)
            return;

        label.text = LegendaryPositionDisplayFormatter.Format(label.text, leaderboardPosition);
    }
}

[HarmonyPatch(typeof(EndOfRunRankController), "SetLeaderboardPosition")]
internal static class EndOfRunSetLeaderboardPositionPatch
{
    [HarmonyPostfix]
    private static void Postfix(EndOfRunRankController __instance, int position)
    {
        ApplyRankLabelOverrides(__instance, position);
    }

    internal static void ApplyRankLabelOverrides(EndOfRunRankController controller, int? position)
    {
        var formatted = LegendaryPositionDisplayFormatter.Format(position?.ToString(), position);
        SetRankDisplayLabel(controller, "bigDisplay", formatted);
        SetRankDisplayLabel(controller, "currentDisplay", formatted);
        SetRankDisplayLabel(controller, "nextDisplay", formatted);
        SetRankDisplayLabel(controller, "bigDisplaySwap", formatted);
    }

    private static void SetRankDisplayLabel(
        EndOfRunRankController controller,
        string fieldName,
        string text
    )
    {
        var display = Traverse.Create(controller).Field(fieldName).GetValue();
        if (display == null)
            return;

        var label = Traverse.Create(display).Field("rankLabel").GetValue<TMP_Text>();
        if (label == null)
            return;

        label.text = text;
    }
}

[HarmonyPatch(typeof(EndOfRunRankController), "RankUpCompleted")]
internal static class EndOfRunRankUpCompletedPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        EndOfRunRankController __instance,
        PlayableDirector playableDirector
    )
    {
        _ = playableDirector;

        var postRunRank = Traverse.Create(__instance).Field("postRunRank").GetValue<ERank>();
        if (postRunRank != ERank.Legendary)
            return;

        var position = Traverse
            .Create(__instance)
            .Field("postRunLeaderboardPosition")
            .GetValue<int>();
        EndOfRunSetLeaderboardPositionPatch.ApplyRankLabelOverrides(__instance, position);
    }
}
