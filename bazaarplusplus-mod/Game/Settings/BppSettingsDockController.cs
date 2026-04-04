#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.Input;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal sealed class BppSettingsDockController : MonoBehaviour
{
    private const string LogCategory = "BppSettingsDock";
    private const string DockButtonObjectName = "BPP_SettingsDockButton";
    private const string DockButtonLabelObjectName = "BPP_SettingsDockButtonLabel";
    private const string PanelObjectName = "BPP_SettingsDockPanel";
    private const string HeaderObjectName = "BPP_SettingsDockHeader";
    private const float DockButtonWidth = 124f;
    private const float DockButtonHeight = 44f;
    private const float DockButtonOffsetX = -20f;
    private const float DockButtonOffsetY = 100f;
    private const float PanelWidth = 456f;
    private const float PanelPadding = 18f;
    private const float PanelTopPadding = 16f;
    private const float PanelBottomPadding = 28f;
    private const float HeaderHeight = 24f;
    private const float HeaderSpacing = 16f;
    private const float RowHeight = 48f;
    private const float RowSpacing = 12f;
    private const float RowInnerPadding = 16f;
    private const float StatusWidth = 80f;

    private readonly List<DockSettingRowView> _rows = [];

    private Button? _anchorButton;
    private Button? _dockButton;
    private RectTransform? _dockButtonRect;
    private RectTransform? _panelRoot;
    private TextMeshProUGUI? _headerLabel;
    private TMP_FontAsset? _uiFont;
    private Material? _uiFontMaterial;
    private bool _isExpanded;

    internal static void Attach(Button anchorButton)
    {
        if (anchorButton == null)
            return;

        var controller =
            anchorButton.GetComponent<BppSettingsDockController>()
            ?? anchorButton.gameObject.AddComponent<BppSettingsDockController>();
        controller.Initialize(anchorButton);
    }

    internal static void RefreshAll()
    {
        foreach (
            var controller in UnityEngine.Object.FindObjectsOfType<BppSettingsDockController>(
                includeInactive: true
            )
        )
            controller.RefreshView();
    }

    private void Initialize(Button anchorButton)
    {
        _anchorButton = anchorButton;
        ResolveTextStyle();
        if (!TryEnsureDockButton())
            return;

        if (!TryEnsurePanel())
            return;

        RefreshView();
        SetExpanded(false);
    }

    private void OnEnable()
    {
        RefreshView();
    }

    private void OnDisable()
    {
        SetExpanded(false);
    }

    private bool TryEnsureDockButton()
    {
        if (_anchorButton == null)
            return false;

        var hostRect = _anchorButton.transform.parent as RectTransform;
        if (hostRect == null)
            return false;

        var existingRect = hostRect.Find(DockButtonObjectName) as RectTransform;
        if (existingRect != null)
        {
            _dockButtonRect = existingRect;
            _dockButton = existingRect.GetComponent<Button>();
            if (_dockButton == null)
                return false;

            var existingLabel = existingRect.Find(DockButtonLabelObjectName);
            if (existingLabel == null)
                CreateDockButtonLabel(existingRect);

            _dockButton.onClick.RemoveListener(OnDockButtonClicked);
            _dockButton.onClick.AddListener(OnDockButtonClicked);
            ConfigureDockButtonRect(existingRect);
            ConfigureDockButtonVisual(existingRect.gameObject);
            SyncDockButtonPlacement();
            return true;
        }

        var dockButtonObject = new GameObject(
            DockButtonObjectName,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button),
            typeof(Outline)
        );
        var dockRect = dockButtonObject.GetComponent<RectTransform>();
        dockRect.SetParent(hostRect, worldPositionStays: false);
        ConfigureDockButtonRect(dockRect);
        ConfigureDockButtonVisual(dockButtonObject);

        _dockButtonRect = dockRect;
        _dockButton = dockButtonObject.GetComponent<Button>();
        _dockButton.transition = Selectable.Transition.ColorTint;
        _dockButton.navigation = new Navigation { mode = Navigation.Mode.None };
        _dockButton.targetGraphic = dockButtonObject.GetComponent<Image>();
        _dockButton.onClick.AddListener(OnDockButtonClicked);

        CreateDockButtonLabel(dockRect);
        SyncDockButtonPlacement();
        return true;
    }

    private bool TryEnsurePanel()
    {
        if (_dockButtonRect == null)
            return false;

        var existingPanel = _dockButtonRect.Find(PanelObjectName) as RectTransform;
        if (existingPanel != null)
        {
            _panelRoot = existingPanel;
            ConfigurePanelRect(existingPanel);
            ConfigurePanelVisual(existingPanel.gameObject);
            _headerLabel = existingPanel.Find(HeaderObjectName)?.GetComponent<TextMeshProUGUI>();
            if (_headerLabel == null)
            {
                BppLog.Warn(LogCategory, "BPP settings panel header was missing.");
                return false;
            }

            EnsureRows();
            ConfigureHeaderRect(_headerLabel.rectTransform);
            RefreshRowLayouts();
            return true;
        }

        var panelObject = new GameObject(
            PanelObjectName,
            typeof(RectTransform),
            typeof(Image),
            typeof(Outline)
        );
        var panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.SetParent(_dockButtonRect, worldPositionStays: false);
        panelRect.anchorMin = new Vector2(0f, 0.5f);
        panelRect.anchorMax = new Vector2(0f, 0.5f);
        panelRect.pivot = new Vector2(1f, 0.5f);
        panelRect.localScale = Vector3.one;
        panelRect.localRotation = Quaternion.identity;
        panelRect.anchoredPosition = new Vector2(-8f, 0f);
        ConfigurePanelRect(panelRect);
        ConfigurePanelVisual(panelObject);

        _headerLabel = CreateText(
            HeaderObjectName,
            panelRect,
            21f,
            TextAlignmentOptions.Left,
            new Color(0.97f, 0.83f, 0.49f, 1f)
        );
        if (_headerLabel == null)
        {
            BppLog.Warn(LogCategory, "Failed to create BPP settings dock header.");
            return false;
        }

        _panelRoot = panelRect;
        EnsureRows();
        ConfigureHeaderRect(_headerLabel.rectTransform);
        RefreshRowLayouts();
        return true;
    }

    private void EnsureRows()
    {
        if (_panelRoot == null)
            return;

        while (_rows.Count < BppSettingsDockCatalog.Definitions.Count)
        {
            var rowIndex = _rows.Count;
            _rows.Add(CreateRow(BppSettingsDockCatalog.Definitions[rowIndex], rowIndex));
        }
    }

    private void RefreshRowLayouts()
    {
        for (var index = 0; index < _rows.Count; index++)
            ConfigureRowRect(_rows[index].RectTransform, index);
    }

    private DockSettingRowView CreateRow(BppSettingsDockDefinition definition, int index)
    {
        if (_panelRoot == null)
            throw new InvalidOperationException("Panel root was unavailable.");

        var rowObject = new GameObject(
            $"BPP_SettingsDockRow_{definition.Key}",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button),
            typeof(Outline)
        );
        var rowRect = rowObject.GetComponent<RectTransform>();
        rowRect.SetParent(_panelRoot, worldPositionStays: false);
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        ConfigureRowRect(rowRect, index);

        var background = rowObject.GetComponent<Image>();
        background.raycastTarget = true;

        var outline = rowObject.GetComponent<Outline>();
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        var button = rowObject.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.targetGraphic = background;
        button.onClick.AddListener(() => ActivateDefinition(definition));

        var label = CreateText(
            "Label",
            rowRect,
            19f,
            TextAlignmentOptions.Left,
            new Color(0.93f, 0.93f, 0.95f, 1f)
        );
        if (label == null)
            throw new InvalidOperationException(
                $"Failed to create row label for {definition.Key}."
            );

        var labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.pivot = new Vector2(0f, 0.5f);
        labelRect.offsetMin = new Vector2(RowInnerPadding, 0f);
        labelRect.offsetMax = new Vector2(-(StatusWidth + RowInnerPadding + 8f), 0f);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;

        var status = CreateText("Status", rowRect, 17f, TextAlignmentOptions.Center, Color.white);
        if (status == null)
            throw new InvalidOperationException(
                $"Failed to create row status label for {definition.Key}."
            );

        var statusRect = status.rectTransform;
        statusRect.anchorMin = new Vector2(1f, 0.5f);
        statusRect.anchorMax = new Vector2(1f, 0.5f);
        statusRect.pivot = new Vector2(1f, 0.5f);
        statusRect.sizeDelta = new Vector2(StatusWidth, RowHeight);
        statusRect.anchoredPosition = new Vector2(-RowInnerPadding, 0f);
        status.textWrappingMode = TextWrappingModes.NoWrap;

        return new DockSettingRowView(definition, rowRect, background, outline, label, status);
    }

    private void OnDockButtonClicked()
    {
        SetExpanded(!_isExpanded);
    }

    private void OnRectTransformDimensionsChange()
    {
        SyncDockButtonPlacement();
    }

    private void SetExpanded(bool expanded)
    {
        _isExpanded = expanded;
        if (_panelRoot != null)
        {
            if (expanded)
                RefreshView();

            _panelRoot.gameObject.SetActive(expanded);
        }

        UpdateDockButtonAccent();
    }

    private void ActivateDefinition(BppSettingsDockDefinition definition)
    {
        if (definition.RequiresCtrlToActivate && !IsCtrlHeld())
            return;

        definition.Activate();
        if (definition.CollapseAfterActivate)
            SetExpanded(false);

        RefreshAll();
    }

    private void RefreshView()
    {
        if (_headerLabel != null)
            _headerLabel.text = ResolveHeader(PlayerPreferences.Data.LanguageCode);

        foreach (var row in _rows)
            ApplyRowState(row);

        UpdateDockButtonAccent();
    }

    private void ApplyRowState(DockSettingRowView row)
    {
        var enabled = row.Definition.IsActive();
        row.Label.text = row.Definition.ResolveLabel(PlayerPreferences.Data.LanguageCode);
        row.Status.text = row.Definition.ResolveStatus(PlayerPreferences.Data.LanguageCode);
        row.Background.color = enabled
            ? new Color(0.23f, 0.35f, 0.22f, 0.94f)
            : new Color(0.19f, 0.19f, 0.22f, 0.92f);
        row.Outline.effectColor = enabled
            ? new Color(0.78f, 0.86f, 0.46f, 0.70f)
            : new Color(0f, 0f, 0f, 0.45f);
        row.Status.color = enabled
            ? new Color(0.90f, 0.97f, 0.78f, 1f)
            : new Color(0.75f, 0.78f, 0.82f, 0.98f);
    }

    private void UpdateDockButtonAccent()
    {
        if (_dockButtonRect == null)
            return;

        var image = _dockButtonRect.GetComponent<Image>();
        var outline = _dockButtonRect.GetComponent<Outline>();
        if (image == null || outline == null)
            return;

        var enabledCount = 0;
        foreach (var definition in BppSettingsDockCatalog.Definitions)
        {
            if (definition.IsActive())
                enabledCount++;
        }

        image.color =
            _isExpanded ? new Color(0.72f, 0.34f, 0.15f, 0.98f)
            : enabledCount > 0 ? new Color(0.40f, 0.21f, 0.13f, 0.96f)
            : new Color(0.16f, 0.16f, 0.18f, 0.95f);
        outline.effectColor = _isExpanded
            ? new Color(0.98f, 0.87f, 0.55f, 0.82f)
            : new Color(0f, 0f, 0f, 0.50f);
    }

    private void SyncDockButtonPlacement()
    {
        if (_dockButtonRect == null || _anchorButton == null)
            return;

        var parentRect = _dockButtonRect.parent as RectTransform;
        var anchorRect = _anchorButton.transform as RectTransform;
        if (parentRect == null || anchorRect == null)
            return;

        _dockButtonRect.anchorMin = new Vector2(0.5f, 0.5f);
        _dockButtonRect.anchorMax = new Vector2(0.5f, 0.5f);
        _dockButtonRect.pivot = new Vector2(0.5f, 0.5f);
        _dockButtonRect.localScale = Vector3.one;
        _dockButtonRect.localRotation = Quaternion.identity;

        var corners = new Vector3[4];
        anchorRect.GetWorldCorners(corners);
        var anchorCenterWorld = (corners[0] + corners[2]) * 0.5f;
        var anchorCenterLocal = parentRect.InverseTransformPoint(anchorCenterWorld);
        _dockButtonRect.localPosition = new Vector3(
            anchorCenterLocal.x + DockButtonOffsetX,
            anchorCenterLocal.y + DockButtonOffsetY,
            _dockButtonRect.localPosition.z
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

        var template = FindTemplateText(_anchorButton.transform);
        if (template == null)
        {
            var hostRect = _anchorButton.transform.parent;
            if (hostRect != null)
                template = FindTemplateText(hostRect);
        }

        if (template == null)
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                if (candidate != null && candidate.font != null)
                {
                    template = candidate;
                    break;
                }
            }
        }

        if (template == null)
            return;

        _uiFont = template.font;
        _uiFontMaterial = template.fontSharedMaterial;
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

    private sealed class DockSettingRowView
    {
        internal DockSettingRowView(
            BppSettingsDockDefinition definition,
            RectTransform rectTransform,
            Image background,
            Outline outline,
            TextMeshProUGUI label,
            TextMeshProUGUI status
        )
        {
            Definition = definition;
            RectTransform = rectTransform;
            Background = background;
            Outline = outline;
            Label = label;
            Status = status;
        }

        internal BppSettingsDockDefinition Definition { get; }

        internal RectTransform RectTransform { get; }

        internal Image Background { get; }

        internal Outline Outline { get; }

        internal TextMeshProUGUI Label { get; }

        internal TextMeshProUGUI Status { get; }
    }
}
