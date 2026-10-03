#pragma warning disable CS0436
#nullable enable
using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
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
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Lobby,
                    "lobby.version_label.degraded",
                    storm: ["reason_code"]
                ),
                ex,
                ("reason_code", LobbyLogReasonCode.LabelRefreshException)
            );
        }
    }
}
