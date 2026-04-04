#pragma warning disable CS0436
#nullable enable
using System;
using System.Linq;
using BazaarPlusPlus.Game.Settings;
using HarmonyLib;
using TheBazaar.UI;
using TMPro;
using UnityEngine;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(OptionsDialogController), "Awake")]
internal static class NativeKeybindLabelAwakePatch
{
    private static readonly LocalizedTextSet MonsterPreviewLabel = new(
        "Show Monster Preview",
        "显示怪物预览",
        "Monstervorschau anzeigen",
        "Mostrar previa de monstro",
        "몬스터 미리보기 표시",
        "Mostra anteprima mostro"
    );

    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        TryUpdateLabels(__instance);
    }

    internal static void TryUpdateLabels(OptionsDialogController instance)
    {
        if (instance == null)
            return;

        foreach (var controller in instance.GetComponentsInChildren<KeyBindController>(true))
        {
            if (!IsMonsterPreviewAction(controller))
                continue;

            var label = FindLabel(controller);
            if (label == null)
                continue;

            label.text = ResolveMonsterPreviewLabel(PlayerPreferences.Data.LanguageCode);
        }
    }

    private static bool IsMonsterPreviewAction(KeyBindController controller)
    {
        var field = AccessTools.Field(typeof(KeyBindController), "_keybindAction");
        var action = field?.GetValue(controller)?.ToString();
        return string.Equals(action, "Lock", StringComparison.Ordinal);
    }

    private static TextMeshProUGUI? FindLabel(KeyBindController controller)
    {
        var keybindButton =
            AccessTools.Field(typeof(KeyBindController), "_keybindButton")?.GetValue(controller)
            as UnityEngine.UI.Button;
        var resetButton =
            AccessTools
                .Field(typeof(KeyBindController), "_resetToDefaultButton")
                ?.GetValue(controller) as UnityEngine.UI.Button;
        var keybindText =
            AccessTools.Field(typeof(KeyBindController), "_keybindText")?.GetValue(controller)
            as TextMeshProUGUI;
        var warningText =
            AccessTools.Field(typeof(KeyBindController), "_warningText")?.GetValue(controller)
            as TextMeshProUGUI;

        return controller
            .GetComponentsInChildren<TextMeshProUGUI>(true)
            .FirstOrDefault(text =>
                text != null
                && text != keybindText
                && text != warningText
                && (keybindButton == null || !text.transform.IsChildOf(keybindButton.transform))
                && (resetButton == null || !text.transform.IsChildOf(resetButton.transform))
            );
    }

    private static string ResolveMonsterPreviewLabel(string languageCode)
    {
        return MonsterPreviewLabel.Resolve(languageCode);
    }
}

[HarmonyPatch(typeof(OptionsDialogController), "OnEnable")]
internal static class NativeKeybindLabelOnEnablePatch
{
    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        NativeKeybindLabelAwakePatch.TryUpdateLabels(__instance);
    }
}
