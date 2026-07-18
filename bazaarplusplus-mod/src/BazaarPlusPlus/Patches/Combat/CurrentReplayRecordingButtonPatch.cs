#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar;
using UnityEngine.UI;

namespace BazaarPlusPlus.Patches.Combat;

[HarmonyPatch(
    typeof(BoardRecapReplayButtonsController),
    nameof(BoardRecapReplayButtonsController.Show)
)]
internal static class CurrentReplayRecordingButtonPatch
{
    private static readonly FieldInfo? ReplayButtonField = AccessTools.Field(
        typeof(BoardRecapReplayButtonsController),
        "ReplayButton"
    );
    private static readonly FieldInfo? RecapButtonField = AccessTools.Field(
        typeof(BoardRecapReplayButtonsController),
        "RecapButton"
    );
    private static readonly FieldInfo? BackButtonField = AccessTools.Field(
        typeof(BoardRecapReplayButtonsController),
        "BackButton"
    );

    [HarmonyPostfix]
    private static void Postfix(BoardRecapReplayButtonsController __instance)
    {
        if (__instance == null)
            return;
        var replayButton = ReplayButtonField?.GetValue(__instance) as Button;
        var recapButton = RecapButtonField?.GetValue(__instance) as Button;
        var backButton = BackButtonField?.GetValue(__instance) as Button;
        if (replayButton != null && recapButton != null && backButton != null)
        {
            CurrentReplayRecordingButtonController.BindNativeActions(
                replayButton,
                recapButton,
                backButton
            );
        }
    }
}

[HarmonyPatch(typeof(FightMenuDialog), "Start")]
internal static class CurrentReplayRecordingDockButtonPatch
{
    private static readonly FieldInfo? SettingButtonField = AccessTools.Field(
        typeof(FightMenuDialog),
        "SettingButton"
    );

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    private static void Postfix(FightMenuDialog __instance)
    {
        try
        {
            var settingButtonCustom = SettingButtonField?.GetValue(__instance) as ButtonCustom;
            var settingsButton = settingButtonCustom?.GetButton();
            if (settingsButton != null)
                CurrentReplayRecordingButtonController.Attach(settingsButton);
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                SettingsLogEvents.PatchDegraded,
                ex,
                SettingsLogEvents.PatchDegradedOperation.Bind(SettingsPatchOperation.DockOpen),
                SettingsLogEvents.PatchDegradedReasonCode.Bind(SettingsLogReasonCode.PatchException)
            );
        }
    }
}
