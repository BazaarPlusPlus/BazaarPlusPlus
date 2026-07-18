#pragma warning disable CS0436
#nullable enable
using System.Collections;
using System.Reflection;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar.UI;
using UnityEngine;

namespace BazaarPlusPlus.Patches.Settings;

[HarmonyPatch(typeof(OptionsDialogController), "Awake")]
internal static class BppKeybindSettingsAwakePatch
{
    internal const string EnchantPreviewObjectName = "BPP_Keybind_EnchantPreview";
    internal const string UpgradePreviewObjectName = "BPP_Keybind_UpgradePreview";
    internal const string ToggleCollectionPanelObjectName = "BPP_Keybind_ToggleCollectionPanel";
    internal const string ToggleLiveBuildPanelObjectName = "BPP_Keybind_ToggleLiveBuildPanel";
    internal const string ToggleHistoryPanelObjectName = "BPP_Keybind_ToggleHistoryPanel";

    private static readonly BppKeybindDefinition[] Definitions =
    [
        new(EnchantPreviewObjectName, BppHotkeyActionId.HoldEnchantPreview),
        new(UpgradePreviewObjectName, BppHotkeyActionId.HoldUpgradePreview),
        new(ToggleCollectionPanelObjectName, BppHotkeyActionId.ToggleCollectionPanel),
        new(ToggleLiveBuildPanelObjectName, BppHotkeyActionId.ToggleLiveBuildPanel),
        new(ToggleHistoryPanelObjectName, BppHotkeyActionId.ToggleHistoryPanel),
    ];
    internal static readonly string[] DefinitionObjectNames =
    [
        EnchantPreviewObjectName,
        UpgradePreviewObjectName,
        ToggleCollectionPanelObjectName,
        ToggleLiveBuildPanelObjectName,
        ToggleHistoryPanelObjectName,
    ];
    private static readonly FieldInfo? KeybindObjectsField = AccessTools.Field(
        typeof(OptionsDialogController),
        "_keybindObjects"
    );

    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        BppKeybindSettingsPatchSupport.RunRefresh(__instance);
    }

    internal static bool EnsureKeybindRows(OptionsDialogController instance)
    {
        var templateRow = GetTemplateRow(instance);
        if (templateRow == null)
            return false;

        Transform anchorRow = templateRow;
        foreach (var definition in Definitions)
            anchorRow = EnsureKeybindRow(definition, templateRow, anchorRow) ?? anchorRow;

        ArrangeRows(templateRow, Definitions);
        return true;
    }

    internal static void RefreshLanguage(OptionsDialogController instance)
    {
        if (instance == null)
            return;

        foreach (var controller in instance.GetComponentsInChildren<BppKeyBindRowController>(true))
            controller.RefreshLanguage();
    }

    private static Transform? EnsureKeybindRow(
        BppKeybindDefinition definition,
        Transform templateRow,
        Transform anchorRow
    )
    {
        var container = templateRow.parent;
        if (container == null)
            return null;

        var existing = container.Find(definition.ObjectName);
        if (existing != null)
        {
            ConfigureRow(existing.gameObject, definition);
            SettingsMenuLayoutUtility.ArrangeRow(
                anchorRow,
                existing,
                ToRowId(definition.ActionId),
                emitObservation: false
            );
            return existing;
        }

        var cloneObject = UnityEngine.Object.Instantiate(templateRow.gameObject, container);
        cloneObject.name = definition.ObjectName;

        ConfigureRow(cloneObject, definition);
        SettingsMenuLayoutUtility.ArrangeRow(
            anchorRow,
            cloneObject.transform,
            ToRowId(definition.ActionId),
            emitObservation: false
        );
        return cloneObject.transform;
    }

    private static void ConfigureRow(GameObject rowObject, BppKeybindDefinition definition)
    {
        var nativeController = rowObject.GetComponent<KeyBindController>();
        var controller =
            rowObject.GetComponent<BppKeyBindRowController>()
            ?? rowObject.AddComponent<BppKeyBindRowController>();
        controller.Initialize(definition.ActionId, nativeController);
    }

    internal static Transform? FindRowContainer(OptionsDialogController instance)
    {
        return GetTemplateRow(instance)?.parent;
    }

    private static Transform? GetTemplateRow(OptionsDialogController instance)
    {
        var keybindRows = KeybindObjectsField?.GetValue(instance) as RectTransform[];
        if (keybindRows == null)
            return null;

        Transform? templateRow = null;
        foreach (var candidate in keybindRows)
        {
            if (candidate != null && candidate.GetComponent<KeyBindController>() != null)
                templateRow = candidate;
        }

        return templateRow;
    }

    private static void ArrangeRows(
        Transform templateRow,
        params BppKeybindDefinition[] definitions
    )
    {
        if (templateRow == null || definitions == null || definitions.Length == 0)
            return;

        var parent = templateRow.parent;
        if (parent == null)
            return;

        var currentAnchor = templateRow;
        foreach (var definition in definitions)
        {
            var row = parent.Find(definition.ObjectName);
            if (row == null)
                continue;

            SettingsMenuLayoutUtility.ArrangeRow(
                currentAnchor,
                row,
                ToRowId(definition.ActionId),
                emitObservation: true
            );
            currentAnchor = row;
        }
    }

    private static SettingsRowId ToRowId(BppHotkeyActionId actionId) =>
        actionId switch
        {
            BppHotkeyActionId.HoldEnchantPreview => SettingsRowId.HoldEnchantPreview,
            BppHotkeyActionId.HoldUpgradePreview => SettingsRowId.HoldUpgradePreview,
            BppHotkeyActionId.ToggleCollectionPanel => SettingsRowId.CollectionPanel,
            BppHotkeyActionId.ToggleLiveBuildPanel => SettingsRowId.LiveBuildPanel,
            BppHotkeyActionId.ToggleHistoryPanel => SettingsRowId.HistoryPanel,
            _ => SettingsRowId.Unknown,
        };

    private sealed class BppKeybindDefinition
    {
        internal BppKeybindDefinition(string objectName, BppHotkeyActionId actionId)
        {
            ObjectName = objectName;
            ActionId = actionId;
        }

        internal string ObjectName { get; }
        internal BppHotkeyActionId ActionId { get; }
    }
}

[HarmonyPatch(typeof(OptionsDialogController), "OnEnable")]
internal static class BppKeybindSettingsOnEnablePatch
{
    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        BppKeybindSettingsPatchSupport.RunRefresh(__instance);
    }
}

[HarmonyPatch(typeof(OptionsDialogController), "OnGameplayButtonClick")]
internal static class BppKeybindSettingsGameplayOpenPatch
{
    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        BppKeybindSettingsPatchSupport.RunRefresh(__instance);
    }
}

internal static class BppKeybindSettingsPatchSupport
{
    internal static void RunRefresh(OptionsDialogController instance) =>
        BppKeybindSettingsRefreshDriver.Attach(instance).RequestRefresh();
}

internal sealed class BppKeybindSettingsRefreshDriver : MonoBehaviour
{
    private const int RetryFrames = 120;

    private OptionsDialogController? _controller;
    private Coroutine? _refreshCoroutine;

    internal static BppKeybindSettingsRefreshDriver Attach(OptionsDialogController controller)
    {
        var driver =
            controller.GetComponent<BppKeybindSettingsRefreshDriver>()
            ?? controller.gameObject.AddComponent<BppKeybindSettingsRefreshDriver>();
        driver._controller = controller;
        return driver;
    }

    internal void RequestRefresh()
    {
        if (_refreshCoroutine != null)
            StopCoroutine(_refreshCoroutine);

        _refreshCoroutine = StartCoroutine(RefreshRoutine());
    }

    private void OnDisable()
    {
        if (_refreshCoroutine == null)
            return;

        StopCoroutine(_refreshCoroutine);
        _refreshCoroutine = null;
    }

    private IEnumerator RefreshRoutine()
    {
        Exception? lastException = null;
        var lastStage = SettingsKeybindStage.TemplateDiscovery;
        for (var frame = 0; frame < RetryFrames; frame++)
        {
            if (_controller == null)
                yield break;

            var completed = false;
            try
            {
                lastStage = SettingsKeybindStage.TemplateDiscovery;
                if (BppKeybindSettingsAwakePatch.EnsureKeybindRows(_controller))
                {
                    lastStage = SettingsKeybindStage.Refresh;
                    BppKeybindSettingsAwakePatch.RefreshLanguage(_controller);
                    completed = HasInstalledRows(_controller);
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            if (completed)
            {
                _refreshCoroutine = null;
                yield break;
            }

            yield return null;
        }

        _refreshCoroutine = null;
        var fields = new[]
        {
            SettingsLogEvents.KeybindRowsDegradedStage.Bind(lastStage),
            SettingsLogEvents.KeybindRowsDegradedReasonCode.Bind(
                SettingsLogReasonCode.RetryExhausted
            ),
        };
        if (lastException == null)
            BppLog.WarnEvent(SettingsLogEvents.KeybindRowsDegraded, fields);
        else
            BppLog.WarnEvent(SettingsLogEvents.KeybindRowsDegraded, lastException, fields);
    }

    private static bool HasInstalledRows(OptionsDialogController controller)
    {
        var container = BppKeybindSettingsAwakePatch.FindRowContainer(controller);
        if (container == null)
            return false;

        foreach (var objectName in BppKeybindSettingsAwakePatch.DefinitionObjectNames)
        {
            if (container.Find(objectName) == null)
                return false;
        }

        return true;
    }
}
