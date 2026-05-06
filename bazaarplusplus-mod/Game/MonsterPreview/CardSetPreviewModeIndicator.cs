#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.Settings;
using TheBazaar;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class CardSetPreviewModeIndicator : IDisposable
{
    private const string RootObjectName = "BppCardSetPreviewModeIndicator";
    private const string TextObjectName = "BppCardSetPreviewModeIndicatorText";
    private static TMP_FontAsset? _resolvedFont;
    private static Material? _resolvedMaterial;

    private RectTransform? _rootRect;
    private TextMeshProUGUI? _label;
    private Image? _background;
    private Outline? _outline;
    private Transform? _hostRoot;

    public void Show(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Hide();
            return;
        }

        var rootCanvas = Data.TooltipParentComponent?.GetComponentInParent<Canvas>();
        if (rootCanvas == null)
            return;

        Ensure(rootCanvas.transform);
        if (_rootRect == null || _label == null || _background == null || _outline == null)
            return;

        _label.text = text;
        _rootRect.gameObject.SetActive(true);
        _rootRect.SetAsLastSibling();
    }

    public void Hide()
    {
        if (_rootRect != null)
            _rootRect.gameObject.SetActive(false);
    }

    public void Dispose()
    {
        if (_rootRect != null)
            UnityEngine.Object.Destroy(_rootRect.gameObject);

        _rootRect = null;
        _label = null;
        _background = null;
        _outline = null;
        _hostRoot = null;
    }

    private void Ensure(Transform hostRoot)
    {
        if (_rootRect != null && ReferenceEquals(_hostRoot, hostRoot))
            return;

        Dispose();
        _hostRoot = hostRoot;

        var rootObject = new GameObject(
            RootObjectName,
            typeof(RectTransform),
            typeof(Image),
            typeof(Outline)
        );
        _rootRect = rootObject.GetComponent<RectTransform>();
        _rootRect.SetParent(hostRoot, false);
        _rootRect.anchorMin = new Vector2(0.5f, 1f);
        _rootRect.anchorMax = new Vector2(0.5f, 1f);
        _rootRect.pivot = new Vector2(0.5f, 1f);
        _rootRect.anchoredPosition = new Vector2(0f, -120f);
        _rootRect.sizeDelta = new Vector2(360f, 42f);
        _rootRect.localScale = Vector3.one;

        _background = rootObject.GetComponent<Image>();
        _background.color = new Color(0.11f, 0.12f, 0.16f, 0.92f);
        _background.raycastTarget = false;

        _outline = rootObject.GetComponent<Outline>();
        _outline.effectColor = new Color(0.98f, 0.79f, 0.42f, 0.70f);
        _outline.effectDistance = new Vector2(1.2f, -1.2f);
        _outline.useGraphicAlpha = true;

        var textObject = new GameObject(
            TextObjectName,
            typeof(RectTransform),
            typeof(TextMeshProUGUI)
        );
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(_rootRect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 6f);
        textRect.offsetMax = new Vector2(-12f, -6f);

        _label = textObject.GetComponent<TextMeshProUGUI>();
        ApplyTextStyle(_label);
        _label.alignment = TextAlignmentOptions.Center;
        _label.fontSize = 18f;
        _label.enableAutoSizing = true;
        _label.fontSizeMin = 12f;
        _label.fontSizeMax = 18f;
        _label.color = new Color(0.98f, 0.93f, 0.84f, 1f);
        _label.textWrappingMode = TextWrappingModes.NoWrap;
        _label.overflowMode = TextOverflowModes.Ellipsis;
        _label.raycastTarget = false;

        rootObject.SetActive(false);
    }

    private static void ApplyTextStyle(TextMeshProUGUI text)
    {
        ResolveTextStyle();
        text.font = _resolvedFont ?? TMP_Settings.defaultFontAsset;
        if (_resolvedMaterial != null)
            text.fontSharedMaterial = _resolvedMaterial;

        text.richText = false;
    }

    private static void ResolveTextStyle()
    {
        if (_resolvedFont != null)
            return;

        TextMeshProUGUI? template = null;
        foreach (var candidate in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
        {
            if (candidate == null || candidate.font == null)
                continue;

            template ??= candidate;
            var path = BuildTransformPath(candidate.transform);
            if (path.IndexOf("BPP_SettingsDock", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                template = candidate;
                break;
            }
        }

        if (template == null)
            return;

        _resolvedFont = template.font;
        _resolvedMaterial = template.fontSharedMaterial;
    }

    private static string BuildTransformPath(Transform? transform)
    {
        if (transform == null)
            return string.Empty;

        var segments = new Stack<string>();
        for (var current = transform; current != null; current = current.parent)
            segments.Push(current.name);

        return string.Join("/", segments);
    }
}
