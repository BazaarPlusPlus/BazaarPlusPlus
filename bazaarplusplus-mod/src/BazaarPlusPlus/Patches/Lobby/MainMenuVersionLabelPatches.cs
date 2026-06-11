#pragma warning disable CS0436
#nullable enable
using System;
using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar;
using TMPro;

namespace BazaarPlusPlus.Patches.Lobby;

[HarmonyPatch(typeof(VersionShow), "BuildVersionLabel")]
internal static class MainMenuVersionLabelBuildPatch
{
    [HarmonyPostfix]
    private static void Postfix(VersionShow __instance)
    {
        try
        {
            var versionLabel = Traverse
                .Create(__instance)
                .Field("versionLabel")
                .GetValue<TextMeshProUGUI>();
            if (versionLabel == null)
                return;

            MainMenuVersionLabelUpdater.Refresh(versionLabel);
        }
        catch (Exception ex)
        {
            BppLog.Warn("MainMenuVersionLabel", $"Failed to refresh version label: {ex.Message}");
        }
    }
}
