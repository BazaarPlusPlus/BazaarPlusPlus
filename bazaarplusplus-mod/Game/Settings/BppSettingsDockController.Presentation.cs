#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.Input;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal sealed partial class BppSettingsDockController
{
    private static void SyncFloatingButton(
        RectTransform? rectTransform,
        Vector3 anchorCenterLocal,
        float offsetX,
        float offsetY
    )
    {
        if (rectTransform == null)
            return;

        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
        rectTransform.localPosition = new Vector3(
            anchorCenterLocal.x + offsetX,
            anchorCenterLocal.y + offsetY,
            rectTransform.localPosition.z
        );
    }

    private static void ConfigureDockButtonRect(RectTransform rectTransform)
    {
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
        rectTransform.sizeDelta = new Vector2(DockButtonWidth, DockButtonHeight);
        rectTransform.anchoredPosition = new Vector2(DockButtonOffsetX, DockButtonOffsetY);
    }

    private static void ConfigurePanelRect(RectTransform rectTransform)
    {
        rectTransform.anchorMin = new Vector2(0f, 0.5f);
        rectTransform.anchorMax = new Vector2(0f, 0.5f);
        rectTransform.pivot = new Vector2(1f, 0.5f);
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
        rectTransform.anchoredPosition = new Vector2(-8f, 0f);
        rectTransform.sizeDelta = new Vector2(
            PanelWidth,
            CalculatePanelHeight(BppSettingsDockCatalog.Definitions.Count)
        );
    }

    private static void ConfigurePanelVisual(GameObject panelObject)
    {
        var background = panelObject.GetComponent<Image>();
        if (background != null)
        {
            background.color = new Color(0.09f, 0.09f, 0.11f, 0.96f);
            background.raycastTarget = true;
        }

        var outline = panelObject.GetComponent<Outline>();
        if (outline != null)
        {
            outline.effectColor = new Color(0.76f, 0.45f, 0.14f, 0.75f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            outline.useGraphicAlpha = true;
        }
    }

    private static void ConfigureHeaderRect(RectTransform headerRect)
    {
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0f, 1f);
        headerRect.offsetMin = new Vector2(PanelPadding, -PanelTopPadding - HeaderHeight);
        headerRect.offsetMax = new Vector2(-PanelPadding, -PanelTopPadding);
    }

    private static void ConfigureRowRect(RectTransform rowRect, int index)
    {
        var rowTop =
            PanelTopPadding + HeaderHeight + HeaderSpacing + (index * (RowHeight + RowSpacing));
        rowRect.offsetMin = new Vector2(PanelPadding, -(rowTop + RowHeight));
        rowRect.offsetMax = new Vector2(-PanelPadding, -rowTop);
    }

    private static void ConfigureDockButtonVisual(GameObject dockButtonObject)
    {
        var background = dockButtonObject.GetComponent<Image>();
        background.color = new Color(0.16f, 0.16f, 0.18f, 0.95f);
        background.raycastTarget = true;

        var outline = dockButtonObject.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.50f);
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;
    }

    private void CreateDockButtonLabel(Transform parent)
    {
        var label = CreateText(
            DockButtonLabelObjectName,
            parent,
            13f,
            TextAlignmentOptions.Center,
            new Color(0.98f, 0.94f, 0.82f, 1f)
        );
        if (label == null)
            return;

        var labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        label.text = "BazaarPlusPlus";
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
    }

    private TextMeshProUGUI? CreateText(
        string objectName,
        Transform parent,
        float fontSize,
        TextAlignmentOptions alignment,
        Color color
    )
    {
        var textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(parent, worldPositionStays: false);

        var text = textObject.GetComponent<TextMeshProUGUI>();
        ApplyTextStyle(text);
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private void ResolveTextStyle()
    {
        if (_anchorButton == null)
            return;

        TextMeshProUGUI? templateSource = null;
        var template = FindTemplateText(_anchorButton.transform);
        templateSource = template;
        if (template == null)
        {
            var hostRect = _anchorButton.transform.parent;
            if (hostRect != null)
            {
                template = FindTemplateText(hostRect);
                templateSource = template;
            }
        }

        if (template == null)
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                if (candidate != null && candidate.font != null)
                {
                    template = candidate;
                    templateSource = candidate;
                    break;
                }
            }
        }

        if (template == null)
            return;

        _uiFont = template.font;
        _uiFontMaterial = template.fontSharedMaterial;

        if (!_fontResolutionLogged && _uiFont != null)
        {
            _fontResolutionLogged = true;
            BppLog.Info(
                LogCategory,
                $"Resolved TMP font '{_uiFont.name}' material '{_uiFontMaterial?.name ?? "<null>"}' from '{BuildTransformPath(templateSource?.transform)}' text='{templateSource?.text ?? string.Empty}'."
            );
        }
    }

    private static TextMeshProUGUI? FindTemplateText(Transform root)
    {
        foreach (var candidate in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (candidate != null && candidate.font != null)
                return candidate;
        }

        return null;
    }

    private static string BuildTransformPath(Transform? transform)
    {
        if (transform == null)
            return "<unknown>";

        var segments = new Stack<string>();
        var current = transform;
        while (current != null)
        {
            segments.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", segments);
    }

    private void ApplyTextStyle(TextMeshProUGUI text)
    {
        _uiFont ??= TMP_Settings.defaultFontAsset;
        if (_uiFont != null)
            text.font = _uiFont;

        if (_uiFontMaterial != null)
            text.fontSharedMaterial = _uiFontMaterial;

        text.richText = false;
    }

    private static float CalculatePanelHeight(int rowCount)
    {
        var rowsHeight = rowCount > 0 ? (rowCount * RowHeight) + ((rowCount - 1) * RowSpacing) : 0f;
        return PanelTopPadding + HeaderHeight + HeaderSpacing + rowsHeight + PanelBottomPadding;
    }

    private static string ResolveHeader(string languageCode)
    {
        return "BazaarPlusPlus";
    }

    private static bool IsCtrlHeld()
    {
        return KeyBindings.Modifiers.IsCtrlPressed(Keyboard.current);
    }
}
