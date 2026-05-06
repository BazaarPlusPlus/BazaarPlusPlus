#nullable enable
using BazaarGameShared.TempoNet.Enums;
using BazaarPlusPlus.Core.Runtime;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.EndOfRun;
using UnityEngine;

namespace BazaarPlusPlus.Game.LegendaryPosition;

internal static class LegendaryPositionUiRefresh
{
    internal static void TryRefreshVisibleDisplays()
    {
        TryRefreshVisibleHeroBanners();
        TryRefreshVisibleEndOfRunRanks();
    }

    private static void TryRefreshVisibleHeroBanners()
    {
        BppClientCacheBridge.TryGetPlayerLeaderboardPosition(out var leaderboardPosition);

        var banners = Object.FindObjectsOfType<HeroBannerController>(includeInactive: true);
        if (banners == null || banners.Length == 0)
            return;

        var setLeaderboardPosition = AccessTools.Method(
            typeof(HeroBannerController),
            "SetLeaderboardPosition",
            [typeof(int?)]
        );
        if (setLeaderboardPosition == null)
            return;

        foreach (var banner in banners)
        {
            if (banner == null || !banner.isActiveAndEnabled)
                continue;

            setLeaderboardPosition.Invoke(banner, [leaderboardPosition]);
        }
    }

    private static void TryRefreshVisibleEndOfRunRanks()
    {
        var controllers = Object.FindObjectsOfType<EndOfRunRankController>(includeInactive: true);
        if (controllers == null || controllers.Length == 0)
            return;

        foreach (var controller in controllers)
        {
            if (controller == null || !controller.isActiveAndEnabled)
                continue;

            var traverse = Traverse.Create(controller);
            var currentRank = traverse.Field("currentRank").GetValue<ERank>();
            var postRunRank = traverse.Field("postRunRank").GetValue<ERank>();
            var currentPosition = traverse.Field("currentLeaderboardPosition").GetValue<int>();
            var postRunPosition = traverse.Field("postRunLeaderboardPosition").GetValue<int>();

            int? visiblePosition =
                currentRank == ERank.Legendary ? currentPosition
                : postRunRank == ERank.Legendary ? postRunPosition
                : null;

            if (!visiblePosition.HasValue)
                continue;

            Patches.LegendaryPosition.EndOfRunSetLeaderboardPositionPatch.ApplyRankLabelOverrides(
                controller,
                visiblePosition
            );
        }
    }
}
