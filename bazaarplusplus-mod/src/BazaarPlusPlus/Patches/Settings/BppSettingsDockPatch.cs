#pragma warning disable CS0436
#nullable enable
using System;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using UnityEngine.UI;

namespace BazaarPlusPlus.Patches.Settings;

[HarmonyPatch(typeof(SettingDialogsView), "Awake")]
internal static class BppSettingsDockAwakePatch
{
    private static readonly System.Reflection.FieldInfo? MainMenuSettingOptionButtonField =
        AccessTools.Field(typeof(SettingDialogsView), "MainMenuSettingOptionButton");

    private static readonly System.Reflection.FieldInfo? HeroSelectSettingOptionButtonField =
        AccessTools.Field(typeof(SettingDialogsView), "HeroSelectSettingOptionButton");

    [HarmonyPostfix]
    private static void Postfix(SettingDialogsView __instance)
    {
        try
        {
            AttachButtons(
                MainMenuSettingOptionButtonField?.GetValue(__instance) as Button,
                "MainMenu"
            );
            AttachButtons(
                HeroSelectSettingOptionButtonField?.GetValue(__instance) as Button,
                "HeroSelect"
            );
        }
        catch (Exception ex)
        {
            BppLog.Error("BppSettingsDock", "Failed to attach BPP settings dock", ex);
        }
    }

    private static void AttachButtons(Button? button, string key)
    {
        if (button == null)
            return;

        CollectionPanelDockButtonController.Attach(
            button,
            BppSettingsDockPlacement.AboveSettingButton(
                $"CollectionPanel_{key}",
                BppDockButtonIconKind.CollectionPanel
            )
        );
        BppSettingsDockController.Attach(
            button,
            BppSettingsDockPlacement.LeftOfSettingButton(key, BppDockButtonIconKind.SettingsDock)
        );
    }
}

[HarmonyPatch(typeof(FightMenuDialog), "Start")]
internal static class BppSettingsDockFightMenuPatch
{
    private static readonly System.Reflection.FieldInfo? SettingButtonField = AccessTools.Field(
        typeof(FightMenuDialog),
        "SettingButton"
    );

    [HarmonyPostfix]
    private static void Postfix(FightMenuDialog __instance)
    {
        try
        {
            var settingButtonCustom = SettingButtonField?.GetValue(__instance) as ButtonCustom;
            var button = settingButtonCustom?.GetButton();
            if (button != null)
            {
                CollectionPanelDockButtonController.Attach(
                    button,
                    BppSettingsDockPlacement.AboveSettingButton(
                        "CollectionPanel_FightMenu",
                        BppDockButtonIconKind.CollectionPanel
                    )
                );
                BppSettingsDockController.Attach(
                    button,
                    BppSettingsDockPlacement.LeftOfSettingButton(
                        "FightMenu",
                        BppDockButtonIconKind.SettingsDock
                    )
                );
            }
        }
        catch (Exception ex)
        {
            BppLog.Error("BppSettingsDock", "Failed to attach BPP settings dock in fight menu", ex);
        }
    }
}
