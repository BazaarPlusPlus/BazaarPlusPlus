#pragma warning disable CS0436
#nullable enable
using System;
using System.Linq;
using BazaarPlusPlus.Game.Settings;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus;

internal static class SettingsMenuToggleInstaller
{
    internal static void ArrangeRows(
        OptionsDialogController instance,
        params string[] toggleObjectNames
    )
    {
        if (instance == null || toggleObjectNames == null || toggleObjectNames.Length == 0)
            return;

        var baseAnchorToggle = GetBaseAnchorToggle(instance, "SettingsMenu");
        var baseAnchorRow = baseAnchorToggle?.transform.parent;
        if (baseAnchorRow == null)
            return;

        var currentAnchor = baseAnchorRow;
        foreach (var toggleObjectName in toggleObjectNames)
        {
            if (string.IsNullOrWhiteSpace(toggleObjectName))
                continue;

            var row = GetRowByName(instance, toggleObjectName);
            if (row == null)
                continue;

            SettingsMenuLayoutUtility.ArrangeRow(currentAnchor, row);
            currentAnchor = row;
        }
    }

    internal static void EnsureToggleExists(
        OptionsDialogController instance,
        SettingsMenuToggleDefinition definition
    )
    {
        var anchorRow = GetAnchorRow(instance, definition);
        if (anchorRow == null)
        {
            BppLog.Warn(definition.LogCategory, "Could not find gameplay settings anchor toggle");
            return;
        }

        var container = anchorRow.parent;
        if (container == null)
            return;

        var existing = container.Find(definition.ToggleObjectName);
        if (existing != null)
        {
            var existingToggle = existing.GetComponentInChildren<Toggle>(includeInactive: true);
            if (existingToggle == null)
                return;

            ConfigureToggle(existing.gameObject, existingToggle, definition);
            SettingsMenuLayoutUtility.ArrangeRow(anchorRow, existing);
            return;
        }

        var cloneObject = UnityEngine.Object.Instantiate(anchorRow.gameObject, container);
        cloneObject.name = definition.ToggleObjectName;

        var cloneTransform = cloneObject.transform;
        var cloneToggle = cloneObject.GetComponentInChildren<Toggle>(includeInactive: true);
        if (cloneToggle == null)
            return;

        ConfigureToggle(cloneObject, cloneToggle, definition);
        SettingsMenuLayoutUtility.ArrangeRow(anchorRow, cloneTransform);
    }

    private static void ConfigureToggle(
        GameObject toggleObject,
        Toggle toggle,
        SettingsMenuToggleDefinition definition
    )
    {
        SetToggleLabel(toggleObject, definition);
        toggle.SetIsOnWithoutNotify(definition.Bridge.GetInitialValue());
        toggle.onValueChanged.RemoveAllListeners();
        toggle.onValueChanged.AddListener(definition.Bridge.ApplyValue);
    }

    private static Transform? GetAnchorRow(
        OptionsDialogController instance,
        SettingsMenuToggleDefinition definition
    )
    {
        var preferredAnchor = GetPreferredAnchorRow(instance, definition.PreferredAnchorObjectName);
        if (preferredAnchor != null)
            return preferredAnchor;

        var anchorToggle = GetBaseAnchorToggle(instance, definition.LogCategory);
        return anchorToggle?.transform.parent;
    }

    private static Transform? GetPreferredAnchorRow(
        OptionsDialogController instance,
        string? preferredAnchorObjectName
    )
    {
        if (string.IsNullOrWhiteSpace(preferredAnchorObjectName))
            return null;

        return instance.transform.Find($"**/{preferredAnchorObjectName}")
            ?? instance
                .GetComponentsInChildren<Transform>(includeInactive: true)
                .FirstOrDefault(candidate =>
                    candidate != null && candidate.name == preferredAnchorObjectName
                );
    }

    private static Transform? GetRowByName(OptionsDialogController instance, string objectName)
    {
        return instance
            .GetComponentsInChildren<Transform>(includeInactive: true)
            .FirstOrDefault(candidate => candidate != null && candidate.name == objectName);
    }

    private static Toggle? GetBaseAnchorToggle(OptionsDialogController instance, string logCategory)
    {
        var field = AccessTools.Field(typeof(OptionsDialogController), "_fastForwardFirstFight");
        var toggle = field?.GetValue(instance) as Toggle;
        if (toggle != null)
            return toggle;

        BppLog.Warn(
            logCategory,
            "Field _fastForwardFirstFight was unavailable; falling back to toggle scan"
        );
        return instance
            .GetComponentsInChildren<Toggle>(includeInactive: true)
            .FirstOrDefault(candidate =>
                candidate != null
                && !string.IsNullOrWhiteSpace(candidate.name)
                && candidate.name.IndexOf("FastForward", StringComparison.OrdinalIgnoreCase) >= 0
            );
    }

    private static void SetToggleLabel(
        GameObject toggleObject,
        SettingsMenuToggleDefinition definition
    )
    {
        var labelText = definition.ResolveLabel(PlayerPreferences.Data.LanguageCode);
        var label = toggleObject
            .GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text.text));
        if (label != null)
            label.text = labelText;
    }
}

internal static class SettingsMenuLayoutUtility
{
    private const float FallbackSpacing = 8f;

    internal static void Rebuild(RectTransform rectTransform)
    {
        var current = rectTransform;
        while (current != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(current);
            current = current.parent as RectTransform;
        }
    }

    internal static void ArrangeRow(Transform anchorRow, Transform cloneRow)
    {
        if (anchorRow == null || cloneRow == null)
            return;

        cloneRow.SetSiblingIndex(anchorRow.GetSiblingIndex() + 1);

        var parentRect = anchorRow.parent as RectTransform;
        if (parentRect == null)
            return;

        if (HasAutomaticLayout(parentRect))
        {
            BppLog.Debug("SettingsMenu", $"Using automatic layout for {cloneRow.name}");
            Rebuild(parentRect);
            return;
        }

        var anchorRect = anchorRow as RectTransform;
        var cloneRect = cloneRow as RectTransform;
        if (anchorRect == null || cloneRect == null)
        {
            Rebuild(parentRect);
            return;
        }

        var additionalIndex = parentRect
            .Cast<Transform>()
            .Where(child => child != null && child != anchorRow && child.name.StartsWith("BPP_"))
            .OrderBy(child => child.GetSiblingIndex())
            .ToList()
            .FindIndex(child => child == cloneRow);

        if (additionalIndex < 0)
            additionalIndex = 0;

        var step = GetVerticalStep(anchorRect, cloneRect);
        cloneRect.anchorMin = anchorRect.anchorMin;
        cloneRect.anchorMax = anchorRect.anchorMax;
        cloneRect.pivot = anchorRect.pivot;
        cloneRect.sizeDelta = anchorRect.sizeDelta;
        cloneRect.anchoredPosition =
            anchorRect.anchoredPosition + new Vector2(0f, -step * (additionalIndex + 1));
        cloneRect.localScale = anchorRect.localScale;
        cloneRect.localRotation = anchorRect.localRotation;

        BppLog.Info(
            "SettingsMenu",
            $"Positioned {cloneRow.name} below {anchorRow.name}: index={additionalIndex + 1}, step={step:F1}, position={cloneRect.anchoredPosition}"
        );
        ExpandParentIfNeeded(parentRect, anchorRect, step, additionalIndex + 1);
    }

    private static bool HasAutomaticLayout(RectTransform rectTransform)
    {
        return rectTransform.GetComponent<VerticalLayoutGroup>() != null
            || rectTransform.GetComponent<HorizontalOrVerticalLayoutGroup>() != null
            || rectTransform.GetComponent<GridLayoutGroup>() != null;
    }

    private static float GetVerticalStep(RectTransform anchorRect, RectTransform cloneRect)
    {
        var preferredHeight = LayoutUtility.GetPreferredHeight(anchorRect);
        if (preferredHeight <= 0f)
            preferredHeight = anchorRect.rect.height;
        if (preferredHeight <= 0f)
        {
            preferredHeight = LayoutUtility.GetPreferredHeight(cloneRect);
            if (preferredHeight <= 0f)
                preferredHeight = cloneRect.rect.height;
        }

        if (preferredHeight <= 0f)
            preferredHeight = FallbackSpacing;

        var spacing = GetSpacing(anchorRect);
        return preferredHeight + spacing;
    }

    private static float GetSpacing(RectTransform anchorRect)
    {
        var layoutElement = anchorRect.GetComponent<LayoutElement>();
        if (layoutElement != null && layoutElement.minHeight > 0f)
            return Math.Max(layoutElement.minHeight - anchorRect.rect.height, 0f);

        return FallbackSpacing;
    }

    private static void ExpandParentIfNeeded(
        RectTransform parentRect,
        RectTransform anchorRect,
        float step,
        int additionalRows
    )
    {
        var bottomY =
            anchorRect.anchoredPosition.y - step * additionalRows - anchorRect.rect.height;
        var requiredHeight = Math.Abs(Math.Min(0f, bottomY)) + anchorRect.rect.height;
        if (requiredHeight <= parentRect.rect.height)
            return;

        parentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, requiredHeight);
        Rebuild(parentRect);
    }
}
