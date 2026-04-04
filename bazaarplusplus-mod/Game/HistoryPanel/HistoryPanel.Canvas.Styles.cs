#nullable enable
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel
{
    private RectTransform CreateSectionPanel(Transform parent, string name)
    {
        var panel = CreateRect(name, parent);
        AddImage(panel.gameObject, new Color(0.11f, 0.13f, 0.18f, 0.98f));

        var border = CreateRect("Border", panel);
        StretchToParent(border, 0f, 0f, 0f, 0f);
        AddImage(border.gameObject, new Color(0.77f, 0.83f, 0.91f, 0.08f));
        return panel;
    }

    private void BuildSectionHeader(
        Transform parent,
        string titleText,
        string subtitleText,
        out TextMeshProUGUI title,
        out TextMeshProUGUI subtitle
    )
    {
        title = CreateText("SectionTitle", parent, 20, FontStyle.Bold, TextAnchor.UpperLeft);
        title.text = titleText.ToUpperInvariant();
        title.color = new Color(0.76f, 0.91f, 1f, 1f);
        ConfigureLayoutElement(title.gameObject, preferredHeight: 24f, minHeight: 24f);

        subtitle = CreateText(
            "SectionSubtitle",
            parent,
            12,
            FontStyle.Normal,
            TextAnchor.UpperLeft
        );
        subtitle.text = subtitleText;
        subtitle.color = new Color(0.72f, 0.77f, 0.84f, 0.92f);
        subtitle.textWrappingMode = TextWrappingModes.Normal;
        subtitle.overflowMode = TextOverflowModes.Ellipsis;
        ConfigureLayoutElement(subtitle.gameObject, preferredHeight: 30f, minHeight: 18f);
    }

    private RectTransform CreateScrollSection(Transform parent, string name)
    {
        var root = CreateRect(name, parent);
        ConfigureLayoutElement(root.gameObject, flexibleHeight: 1f, flexibleWidth: 1f);

        var viewport = CreateRect("Viewport", root);
        StretchToParent(viewport, 0f, 14f, 0f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var group = content.gameObject.AddComponent<VerticalLayoutGroup>();
        group.spacing = 10f;
        group.childControlWidth = true;
        group.childControlHeight = false;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 20f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.verticalScrollbar = CreateScrollbar(root);
        scroll.verticalScrollbarVisibility = ScrollRect
            .ScrollbarVisibility
            .AutoHideAndExpandViewport;
        return content;
    }

    private void BuildPreviewSection(Transform parent)
    {
        var preview = CreateRect("PreviewPanel", parent);
        ConfigureLayoutElement(
            preview.gameObject,
            preferredHeight: PreviewSectionHeight,
            minHeight: PreviewSectionHeight
        );
        AddImage(preview.gameObject, new Color(0.07f, 0.09f, 0.12f, 0.99f));

        var surfaceFrame = CreateRect("PreviewSurfaceFrame", preview);
        StretchToParent(surfaceFrame, 10f, 10f, 10f, 10f);
        AddImage(surfaceFrame.gameObject, new Color(0.03f, 0.04f, 0.06f, 0.98f));

        var rawImageRect = CreateRect("PreviewRawImage", surfaceFrame);
        StretchToParent(rawImageRect, 1f, 1f, 1f, 1f);
        _previewSurface = rawImageRect.gameObject.AddComponent<RawImage>();
        _previewSurface.color = new Color(1f, 1f, 1f, 0.10f);
        _previewSurface.raycastTarget = false;

        _previewStatusText = CreateText(
            "PreviewStatus",
            surfaceFrame,
            13,
            FontStyle.Normal,
            TextAnchor.MiddleCenter
        );
        _previewStatusText.text = "Select a battle to preview its recorded cards.";
        _previewStatusText.color = new Color(0.82f, 0.87f, 0.93f, 0.96f);
        _previewStatusText.textWrappingMode = TextWrappingModes.Normal;
        _previewStatusText.overflowMode = TextOverflowModes.Ellipsis;
        StretchToParent(_previewStatusText.rectTransform, 28f, 28f, 18f, 18f);

        _previewDebugText = CreateText(
            "PreviewDebug",
            surfaceFrame,
            11,
            FontStyle.Bold,
            TextAnchor.UpperRight
        );
        _previewDebugText.color = new Color(0.97f, 0.85f, 0.57f, 0.96f);
        _previewDebugText.gameObject.SetActive(false);
        _previewDebugText.textWrappingMode = TextWrappingModes.NoWrap;
        _previewDebugText.overflowMode = TextOverflowModes.Overflow;
        _previewDebugText.rectTransform.anchorMin = new Vector2(1f, 1f);
        _previewDebugText.rectTransform.anchorMax = new Vector2(1f, 1f);
        _previewDebugText.rectTransform.pivot = new Vector2(1f, 1f);
        _previewDebugText.rectTransform.anchoredPosition = new Vector2(-14f, -12f);
        _previewDebugText.rectTransform.sizeDelta = new Vector2(560f, 36f);
    }

    private Scrollbar CreateScrollbar(Transform parent)
    {
        var root = CreateRect("Scrollbar", parent);
        root.anchorMin = new Vector2(1f, 0f);
        root.anchorMax = new Vector2(1f, 1f);
        root.pivot = new Vector2(1f, 0.5f);
        root.sizeDelta = new Vector2(10f, 0f);

        var track = AddImage(root.gameObject, new Color(0.16f, 0.18f, 0.22f, 0.88f));
        track.raycastTarget = true;
        var area = CreateRect("Area", root);
        StretchToParent(area, 0f, 0f, 0f, 0f);
        var handle = CreateRect("Handle", area);
        handle.anchorMin = new Vector2(0f, 1f);
        handle.anchorMax = new Vector2(1f, 1f);
        handle.pivot = new Vector2(0.5f, 1f);
        handle.sizeDelta = new Vector2(0f, 56f);
        var handleImage = AddImage(handle.gameObject, new Color(0.74f, 0.62f, 0.31f, 0.96f));
        handleImage.raycastTarget = true;
        var scrollbar = root.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        scrollbar.colors = BuildColorBlock(
            new Color(0.74f, 0.62f, 0.31f, 0.96f),
            new Color(0.86f, 0.73f, 0.38f, 1f),
            new Color(0.24f, 0.26f, 0.30f, 0.45f)
        );
        return scrollbar;
    }

    private TextMeshProUGUI CreateChip(Transform parent, float width)
    {
        var chip = CreateRect("Chip", parent);
        ConfigureLayoutElement(
            chip.gameObject,
            preferredWidth: width,
            minWidth: width,
            preferredHeight: 32f,
            minHeight: 32f
        );
        AddImage(chip.gameObject, new Color(0.14f, 0.18f, 0.23f, 0.96f));
        var text = CreateText("Label", chip, 12, FontStyle.Bold, TextAnchor.MiddleCenter);
        text.color = new Color(0.95f, 0.96f, 0.98f, 1f);
        StretchToParent(text.rectTransform, 8f, 8f, 0f, 0f);
        return text;
    }

    private RectTransform CreatePill(
        Transform parent,
        string name,
        string labelText,
        Color backgroundColor,
        Color textColor
    )
    {
        var pill = CreateRect(name, parent);
        AddImage(pill.gameObject, backgroundColor);
        var text = CreateText("Label", pill, 12, FontStyle.Bold, TextAnchor.MiddleCenter);
        text.text = labelText;
        text.color = textColor;
        text.enableAutoSizing = true;
        text.fontSizeMin = 10f;
        text.fontSizeMax = 12f;
        text.maxVisibleCharacters = PillMaxVisibleCharacters;
        text.overflowMode = TextOverflowModes.Ellipsis;
        StretchToParent(text.rectTransform, 10f, 10f, 0f, 0f);
        return pill;
    }

    private void CreateActionButton(
        string name,
        Transform parent,
        string label,
        float width,
        UnityEngine.Events.UnityAction onClick
    )
    {
        var (button, background, text) = CreateStyledButton(name, parent, label, width, 38f);
        button.onClick.AddListener(onClick);
        RefreshActionButton(
            button,
            background,
            text,
            true,
            new Color(0.23f, 0.27f, 0.32f, 0.98f),
            new Color(0.35f, 0.39f, 0.44f, 1f),
            new Color(0.24f, 0.26f, 0.30f, 0.50f),
            Color.white
        );
    }

    private (Button button, Image background, TextMeshProUGUI label) CreateStyledButton(
        string name,
        Transform parent,
        string labelText,
        float width,
        float preferredHeight = 36f
    )
    {
        var rect = CreateRect(name, parent);
        if (width > 0f)
        {
            ConfigureLayoutElement(
                rect.gameObject,
                preferredWidth: width,
                minWidth: width,
                preferredHeight: preferredHeight,
                minHeight: preferredHeight
            );
        }
        else
        {
            ConfigureLayoutElement(
                rect.gameObject,
                flexibleWidth: 1f,
                preferredHeight: preferredHeight,
                minHeight: preferredHeight
            );
        }

        var background = AddImage(rect.gameObject, Color.white);
        background.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = BuildColorBlock(Color.white, Color.white, Color.white);

        var label = CreateText("Label", rect, 13, FontStyle.Bold, TextAnchor.MiddleCenter);
        label.text = labelText;
        label.color = Color.white;
        label.enableAutoSizing = true;
        label.fontSizeMin = 10f;
        label.fontSizeMax = 13f;
        label.overflowMode = TextOverflowModes.Ellipsis;
        StretchToParent(label.rectTransform, 10f, 10f, 0f, 0f);
        return (button, background, label);
    }

    private static void ConfigureCompactGhostFilterLabel(TextMeshProUGUI? label)
    {
        if (label == null)
            return;

        label.enableAutoSizing = false;
        label.fontSize = 14f;
        label.margin = Vector4.zero;
        label.extraPadding = false;
    }

    private (Button button, Image background) CreateCardButtonShell(
        string name,
        Transform parent,
        float preferredHeight
    )
    {
        var rect = CreateRect(name, parent);
        ConfigureLayoutElement(
            rect.gameObject,
            flexibleWidth: 1f,
            preferredHeight: preferredHeight,
            minHeight: preferredHeight
        );
        var background = AddImage(rect.gameObject, Color.white);
        background.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = BuildColorBlock(Color.white, Color.white, Color.white);
        return (button, background);
    }

    private static void RefreshActionButton(
        Button? button,
        Image? background,
        TextMeshProUGUI? label,
        bool interactable,
        Color normalColor,
        Color pressedColor,
        Color disabledColor,
        Color textColor
    )
    {
        if (button == null || background == null || label == null)
            return;

        button.interactable = interactable;
        button.colors = BuildColorBlock(normalColor, pressedColor, disabledColor);
        background.color = interactable ? normalColor : disabledColor;
        label.color = interactable
            ? textColor
            : new Color(textColor.r, textColor.g, textColor.b, 0.55f);
    }

    private void RefreshGhostFilterButton(
        Button? button,
        Image? background,
        TextMeshProUGUI? label,
        GhostBattleFilter filter
    )
    {
        var isGhostMode = _sectionMode == HistorySectionMode.Ghost;
        var selected = _ghostBattleFilter == filter;
        RefreshActionButton(
            button,
            background,
            label,
            isGhostMode,
            selected
                ? new Color(0.78f, 0.60f, 0.24f, 0.98f)
                : new Color(0.19f, 0.22f, 0.27f, 0.98f),
            new Color(0.92f, 0.72f, 0.30f, 1f),
            new Color(0.24f, 0.26f, 0.30f, 0.50f),
            selected ? new Color(0.10f, 0.07f, 0.03f, 1f) : Color.white
        );
    }

    private static string GetGhostFilterLabel(GhostBattleFilter filter)
    {
        return filter switch
        {
            GhostBattleFilter.IWon => "I Won",
            GhostBattleFilter.ILost => "I Lost",
            _ => "All",
        };
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static RectTransform CreateVerticalGroup(
        string name,
        Transform parent,
        float spacing,
        RectOffset? padding,
        TextAnchor alignment,
        bool controlWidth,
        bool controlHeight,
        bool forceExpandWidth,
        bool forceExpandHeight
    )
    {
        var rect = CreateRect(name, parent);
        var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        group.spacing = spacing;
        group.padding = padding ?? new RectOffset();
        group.childAlignment = alignment;
        group.childControlWidth = controlWidth;
        group.childControlHeight = controlHeight;
        group.childForceExpandWidth = forceExpandWidth;
        group.childForceExpandHeight = forceExpandHeight;
        return rect;
    }

    private static RectTransform CreateHorizontalGroup(
        string name,
        Transform parent,
        float spacing,
        RectOffset? padding,
        TextAnchor alignment,
        bool controlWidth,
        bool controlHeight,
        bool forceExpandWidth,
        bool forceExpandHeight
    )
    {
        var rect = CreateRect(name, parent);
        var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.spacing = spacing;
        group.padding = padding ?? new RectOffset();
        group.childAlignment = alignment;
        group.childControlWidth = controlWidth;
        group.childControlHeight = controlHeight;
        group.childForceExpandWidth = forceExpandWidth;
        group.childForceExpandHeight = forceExpandHeight;
        return rect;
    }

    private static void CreateFlexibleSpacer(string name, Transform parent)
    {
        var spacer = CreateRect(name, parent);
        ConfigureLayoutElement(spacer.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);
    }

    private static RectOffset CreatePadding(float left, float right, float top, float bottom)
    {
        return new RectOffset(
            Mathf.RoundToInt(left),
            Mathf.RoundToInt(right),
            Mathf.RoundToInt(top),
            Mathf.RoundToInt(bottom)
        );
    }

    private static void ConfigureLayoutElement(
        GameObject gameObject,
        float preferredWidth = -1f,
        float minWidth = -1f,
        float flexibleWidth = -1f,
        float preferredHeight = -1f,
        float minHeight = -1f,
        float flexibleHeight = -1f
    )
    {
        var element =
            gameObject.GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
        if (preferredWidth >= 0f)
            element.preferredWidth = preferredWidth;
        if (minWidth >= 0f)
            element.minWidth = minWidth;
        if (flexibleWidth >= 0f)
            element.flexibleWidth = flexibleWidth;
        if (preferredHeight >= 0f)
            element.preferredHeight = preferredHeight;
        if (minHeight >= 0f)
            element.minHeight = minHeight;
        if (flexibleHeight >= 0f)
            element.flexibleHeight = flexibleHeight;
    }

    private static void StretchToParent(
        RectTransform rect,
        float left,
        float right,
        float top,
        float bottom
    )
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static Image AddImage(GameObject gameObject, Color color)
    {
        var image = gameObject.AddComponent<Image>();
        image.sprite = GetRoundedSprite();
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        int fontSize,
        FontStyle fontStyle,
        TextAnchor alignment
    )
    {
        var rect = CreateRect(name, parent);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = GetUiFont();
        text.fontSize = fontSize;
        text.fontStyle = MapFontStyle(fontStyle);
        text.alignment = MapAlignment(alignment);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Truncate;
        text.richText = false;
        text.raycastTarget = false;
        return text;
    }

    private static ColorBlock BuildColorBlock(Color normal, Color pressed, Color disabled)
    {
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor = normal;
        colors.highlightedColor = normal;
        colors.selectedColor = normal;
        colors.pressedColor = pressed;
        colors.disabledColor = disabled;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.05f;
        return colors;
    }

    private static TMP_FontAsset GetUiFont()
    {
        _uiFont ??= TMP_Settings.defaultFontAsset;
        if (_uiFont == null)
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                if (candidate != null && candidate.font != null)
                {
                    _uiFont = candidate.font;
                    break;
                }
            }
        }

        _uiFont ??= TMP_FontAsset.CreateFontAsset(
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
        );
        return _uiFont;
    }

    private static Sprite GetRoundedSprite()
    {
        if (_roundedSprite != null)
            return _roundedSprite;

        const int size = 32;
        const int radius = 12;
        var texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                texture.SetPixel(
                    x,
                    y,
                    new Color(1f, 1f, 1f, IsInsideRoundedRect(x, y, size, radius) ? 1f : 0f)
                );
            }
        }

        texture.Apply();
        _roundedSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius)
        );
        return _roundedSprite;
    }

    private static bool IsInsideRoundedRect(int x, int y, int size, int radius)
    {
        var clampedX = Mathf.Clamp(x, radius, size - radius - 1);
        var clampedY = Mathf.Clamp(y, radius, size - radius - 1);
        var dx = x - clampedX;
        var dy = y - clampedY;
        return (dx * dx) + (dy * dy) <= radius * radius;
    }

    private static FontStyles MapFontStyle(FontStyle fontStyle) =>
        fontStyle switch
        {
            FontStyle.Bold => FontStyles.Bold,
            FontStyle.Italic => FontStyles.Italic,
            FontStyle.BoldAndItalic => FontStyles.Bold | FontStyles.Italic,
            _ => FontStyles.Normal,
        };

    private static TextAlignmentOptions MapAlignment(TextAnchor alignment) =>
        alignment switch
        {
            TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter => TextAlignmentOptions.Top,
            TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
            TextAnchor.MiddleLeft => TextAlignmentOptions.MidlineLeft,
            TextAnchor.MiddleCenter => TextAlignmentOptions.Midline,
            TextAnchor.MiddleRight => TextAlignmentOptions.MidlineRight,
            TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
            TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
            TextAnchor.LowerRight => TextAlignmentOptions.BottomRight,
            _ => TextAlignmentOptions.TopLeft,
        };

    private static float MeasurePillWidth(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? 64f : Mathf.Max(64f, (text.Length * 7f) + 24f);
    }

    private static string FormatPillLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return label;

        var trimmed = label.Trim();
        return trimmed.ToUpperInvariant() switch
        {
            "PERFECT" => "PERFCT",
            "MISFORTUNE" => "MISFRT",
            _ => trimmed,
        };
    }

    private static string GetDynamicPreviewButtonLabel(bool enabled)
    {
        return enabled ? "Live" : "Still";
    }

    private static string GetDeleteRunButtonLabel(bool confirming)
    {
        return confirming ? "Sure?" : "Delete";
    }

    private static string ShortenBattleId(string battleId)
    {
        return string.IsNullOrWhiteSpace(battleId) ? "-"
            : battleId.Length <= 12 ? battleId
            : battleId[..12];
    }

    private static BattlePalette GetBattlePalette(HistoryBattleRecord battle)
    {
        var result = HistoryPanelFormatter.FormatBattleResult(battle);
        if (string.Equals(result, "Win", StringComparison.OrdinalIgnoreCase))
        {
            return new BattlePalette(
                new Color(0.10f, 0.15f, 0.16f, 0.98f),
                new Color(0.13f, 0.23f, 0.22f, 0.99f),
                new Color(0.23f, 0.54f, 0.47f, 0.95f),
                new Color(0.13f, 0.28f, 0.23f, 0.98f),
                new Color(0.80f, 0.98f, 0.91f, 1f)
            );
        }

        if (string.Equals(result, "Loss", StringComparison.OrdinalIgnoreCase))
        {
            return new BattlePalette(
                new Color(0.15f, 0.13f, 0.15f, 0.98f),
                new Color(0.24f, 0.18f, 0.16f, 0.99f),
                new Color(0.63f, 0.36f, 0.24f, 0.95f),
                new Color(0.33f, 0.20f, 0.15f, 0.98f),
                new Color(0.99f, 0.90f, 0.85f, 1f)
            );
        }

        return new BattlePalette(
            new Color(0.13f, 0.15f, 0.18f, 0.98f),
            new Color(0.18f, 0.24f, 0.31f, 0.99f),
            new Color(0.34f, 0.47f, 0.64f, 0.95f),
            new Color(0.18f, 0.23f, 0.31f, 0.98f),
            new Color(0.89f, 0.94f, 1f, 1f)
        );
    }

    private static Color GetRunAchievementBackground(string achievement)
    {
        return achievement switch
        {
            "PERFECT" => new Color(0.36f, 0.28f, 0.10f, 0.98f),
            "GOLD" => new Color(0.41f, 0.31f, 0.12f, 0.98f),
            "SILVER" => new Color(0.36f, 0.38f, 0.44f, 0.98f),
            "BRONZE" => new Color(0.38f, 0.25f, 0.18f, 0.98f),
            _ => new Color(0.24f, 0.18f, 0.18f, 0.98f),
        };
    }

    private static Color GetRunAchievementText(string achievement)
    {
        return achievement switch
        {
            "PERFECT" => new Color(1f, 0.94f, 0.71f, 1f),
            "GOLD" => new Color(0.99f, 0.90f, 0.66f, 1f),
            "SILVER" => new Color(0.94f, 0.97f, 1f, 1f),
            "BRONZE" => new Color(0.98f, 0.88f, 0.80f, 1f),
            _ => new Color(0.96f, 0.84f, 0.84f, 1f),
        };
    }

    private static string? FormatRank(string? rawRank)
    {
        if (string.IsNullOrWhiteSpace(rawRank))
            return null;

        var trimmed = rawRank.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        return firstSpace > 0 ? trimmed[..firstSpace] : trimmed;
    }

    private static (Color Background, Color Text) GetRankBadgePalette(string rank)
    {
        return rank switch
        {
            "Bronze" => (new Color(0.39f, 0.24f, 0.17f, 0.98f), new Color(0.98f, 0.88f, 0.80f, 1f)),
            "Silver" => (new Color(0.34f, 0.37f, 0.43f, 0.98f), new Color(0.94f, 0.97f, 1f, 1f)),
            "Gold" => (new Color(0.41f, 0.31f, 0.12f, 0.98f), new Color(0.99f, 0.90f, 0.66f, 1f)),
            "Diamond" => (new Color(0.18f, 0.35f, 0.47f, 0.98f), new Color(0.84f, 0.97f, 1f, 1f)),
            _ => (new Color(0.24f, 0.28f, 0.36f, 0.98f), new Color(0.89f, 0.94f, 1f, 1f)),
        };
    }

    private static void CreateRunStatChip(
        Transform parent,
        string labelText,
        int? value,
        Color valueColor
    )
    {
        var chip = CreateRect(labelText, parent);
        ConfigureLayoutElement(
            chip.gameObject,
            flexibleWidth: 1f,
            preferredHeight: 40f,
            minHeight: 40f
        );
        AddImage(chip.gameObject, BuildRunStatChipBackground(valueColor));

        var accent = CreateRect("Accent", chip);
        accent.anchorMin = new Vector2(0f, 0f);
        accent.anchorMax = new Vector2(0f, 1f);
        accent.pivot = new Vector2(0f, 0.5f);
        accent.sizeDelta = new Vector2(3f, 0f);
        AddImage(accent.gameObject, new Color(valueColor.r, valueColor.g, valueColor.b, 0.95f));

        var label = CreateText("Label", chip, 9, FontStyle.Bold, TextAnchor.UpperLeft);
        label.text = labelText;
        label.color = new Color(0.86f, 0.90f, 0.96f, 0.86f);
        label.rectTransform.anchorMin = new Vector2(0f, 1f);
        label.rectTransform.anchorMax = new Vector2(1f, 1f);
        label.rectTransform.pivot = new Vector2(0f, 1f);
        label.rectTransform.offsetMin = new Vector2(9f, -16f);
        label.rectTransform.offsetMax = new Vector2(-6f, -5f);

        var valueText = CreateText("Value", chip, 14, FontStyle.Bold, TextAnchor.UpperLeft);
        valueText.text = value?.ToString() ?? "--";
        valueText.color = valueColor;
        valueText.rectTransform.anchorMin = new Vector2(0f, 0f);
        valueText.rectTransform.anchorMax = new Vector2(1f, 0f);
        valueText.rectTransform.pivot = new Vector2(0f, 0f);
        valueText.rectTransform.offsetMin = new Vector2(9f, 5f);
        valueText.rectTransform.offsetMax = new Vector2(-6f, 21f);
    }

    private static Color BuildRunStatChipBackground(Color accent)
    {
        return new Color(
            Mathf.Lerp(0.16f, accent.r, 0.12f),
            Mathf.Lerp(0.15f, accent.g, 0.12f),
            Mathf.Lerp(0.14f, accent.b, 0.12f),
            0.98f
        );
    }

    private static HeroBadgeStyle GetHeroBadgeStyle(string? heroName)
    {
        if (string.IsNullOrWhiteSpace(heroName))
            return new HeroBadgeStyle("UNK", new Color(0.20f, 0.29f, 0.38f, 0.95f), Color.white);

        return heroName.Trim() switch
        {
            "Vanessa" => BuildHeroBadgeStyle("VAN", 192, 33, 33),
            "Pygmalien" => BuildHeroBadgeStyle("PYG", 39, 103, 192),
            "Dooley" => BuildHeroBadgeStyle("DOO", 225, 154, 8),
            "Mak" => BuildHeroBadgeStyle("MAK", 190, 230, 91),
            "Jules" => BuildHeroBadgeStyle("JUL", 180, 52, 236),
            "Karnok" => BuildHeroBadgeStyle("KAR", 59, 136, 156),
            "Stelle" => BuildHeroBadgeStyle("STE", 255, 235, 24),
            _ => BuildHeroBadgeStyle(
                heroName.Length <= 3
                    ? heroName.ToUpperInvariant()
                    : heroName[..3].ToUpperInvariant(),
                57,
                73,
                97
            ),
        };
    }

    private static HeroBadgeStyle BuildHeroBadgeStyle(string shortCode, int r, int g, int b)
    {
        var background = ColorFromRgb(r, g, b);
        var luminance = (0.299f * background.r) + (0.587f * background.g) + (0.114f * background.b);
        var text = luminance > 0.62f ? new Color(0.10f, 0.12f, 0.15f, 1f) : Color.white;
        return new HeroBadgeStyle(shortCode, background, text);
    }

    private static Color ColorFromRgb(int r, int g, int b)
    {
        return new Color(r / 255f, g / 255f, b / 255f, 0.98f);
    }
}
