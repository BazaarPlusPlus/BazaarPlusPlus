#pragma warning disable CS0436
#nullable enable
using System;
using System.Reflection;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;
using HarmonyLib;
using TheBazaar.UI;
using TMPro;
using UnityEngine;

namespace BazaarPlusPlus.Patches.Settings;

[HarmonyPatch(typeof(OptionsDialogController), "Awake")]
internal static class NativeKeybindLabelAwakePatch
{
    private const BindingFlags KeybindFieldFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo? KeybindActionField = typeof(KeyBindController).GetField(
        "_keybindAction",
        KeybindFieldFlags
    );
    private static readonly FieldInfo? KeybindButtonField = typeof(KeyBindController).GetField(
        "_keybindButton",
        KeybindFieldFlags
    );
    private static readonly FieldInfo? ResetToDefaultButtonField =
        typeof(KeyBindController).GetField("_resetToDefaultButton", KeybindFieldFlags);
    private static readonly FieldInfo? KeybindTextField = typeof(KeyBindController).GetField(
        "_keybindText",
        KeybindFieldFlags
    );
    private static readonly FieldInfo? WarningTextField = typeof(KeyBindController).GetField(
        "_warningText",
        KeybindFieldFlags
    );

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

    internal static bool TryUpdateLabels(OptionsDialogController instance)
    {
        if (instance == null)
            return false;
        if (KeybindActionField == null)
            return true;

        var updated = false;
        foreach (var controller in instance.GetComponentsInChildren<KeyBindController>(true))
        {
            if (!IsMonsterPreviewAction(controller))
                continue;

            var label = FindLabel(controller);
            if (label == null)
                continue;

            label.text = ResolveMonsterPreviewLabel();
            updated = true;
        }

        return updated;
    }

    private static bool IsMonsterPreviewAction(KeyBindController controller)
    {
        var action = KeybindActionField?.GetValue(controller)?.ToString();
        return string.Equals(action, "Lock", StringComparison.Ordinal);
    }

    private static TextMeshProUGUI? FindLabel(KeyBindController controller)
    {
        var keybindButton = KeybindButtonField?.GetValue(controller) as UnityEngine.UI.Button;
        var resetButton = ResetToDefaultButtonField?.GetValue(controller) as UnityEngine.UI.Button;
        var keybindText = KeybindTextField?.GetValue(controller) as TextMeshProUGUI;
        var warningText = WarningTextField?.GetValue(controller) as TextMeshProUGUI;

        foreach (var text in controller.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (
                text != null
                && text != keybindText
                && text != warningText
                && (keybindButton == null || !text.transform.IsChildOf(keybindButton.transform))
                && (resetButton == null || !text.transform.IsChildOf(resetButton.transform))
            )
            {
                return text;
            }
        }

        return null;
    }

    private static string ResolveMonsterPreviewLabel()
    {
        return L.Resolve(MonsterPreviewLabel);
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
