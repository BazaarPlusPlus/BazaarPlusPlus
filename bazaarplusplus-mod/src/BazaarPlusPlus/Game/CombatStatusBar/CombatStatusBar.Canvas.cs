#nullable enable

using System;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed partial class CombatStatusBar
{
    private const float BarHeight = Sizes.CombatStatusBarHeight;
    private const float BarBottomMargin = 0f;
    private const float SegmentSpacing = 14f;
    private const float SegmentHorizontalInset = 10f;
    private const int CanvasSortingOrder = 10;

    private static readonly Color BarColorIdle = new(0.06f, 0.07f, 0.09f, 0.90f);
    private static readonly Color BarColorActive = new(0.16f, 0.12f, 0.08f, 0.96f);
    private static readonly Color GlowColorIdle = new(0.20f, 0.24f, 0.28f, 0.08f);
    private static readonly Color GlowColorActive = new(0.44f, 0.30f, 0.14f, 0.20f);
    private static readonly Color SegmentColorIdle = new(0.11f, 0.13f, 0.16f, 0.90f);
    private static readonly Color SegmentColorActive = new(0.23f, 0.18f, 0.11f, 0.92f);
    private static readonly Color LabelColorIdle = new(0.62f, 0.67f, 0.74f, 0.88f);
    private static readonly Color LabelColorActive = new(0.90f, 0.84f, 0.62f, 0.96f);
    private static readonly Color ValueColorIdle = new(0.84f, 0.87f, 0.93f, 0.96f);
    private static readonly Color ValueColorActive = new(1f, 0.96f, 0.90f, 1f);
    private static readonly Color DividerColorIdle = new(0.42f, 0.46f, 0.53f, 0.22f);
    private static readonly Color DividerColorActive = new(0.84f, 0.74f, 0.44f, 0.42f);
    private static readonly Color SpeedButtonColorIdle = new(0.26f, 0.30f, 0.36f, 0.92f);
    private static readonly Color SpeedButtonColorActive = new(0.48f, 0.33f, 0.13f, 0.95f);
    private static readonly Color SpeedButtonPressedColorIdle = new(0.35f, 0.39f, 0.46f, 1f);
    private static readonly Color SpeedButtonPressedColorActive = new(0.66f, 0.47f, 0.16f, 1f);
    private static readonly Color SpeedButtonUnavailableColorIdle = new(0.18f, 0.21f, 0.25f, 0.62f);
    private static readonly Color SpeedButtonUnavailableColorActive = new(
        0.34f,
        0.24f,
        0.12f,
        0.68f
    );
    private static readonly Color PausedBaseColorIdle = new(0.28f, 0.33f, 0.40f, 0.95f);
    private static readonly Color PausedBaseColorActive = new(0.54f, 0.40f, 0.16f, 0.96f);
    private static readonly Color UnpausedBaseColorIdle = new(0.24f, 0.27f, 0.33f, 0.90f);
    private static readonly Color UnpausedBaseColorActive = new(0.41f, 0.31f, 0.13f, 0.93f);
    private static readonly Color PausedPressedColorIdle = new(0.36f, 0.42f, 0.50f, 1f);
    private static readonly Color PausedPressedColorActive = new(0.68f, 0.50f, 0.18f, 1f);
    private static readonly Color UnpausedPressedColorIdle = new(0.32f, 0.36f, 0.43f, 1f);
    private static readonly Color UnpausedPressedColorActive = new(0.56f, 0.41f, 0.15f, 1f);
    private static readonly Color OutlineColorIdle = new(0.48f, 0.52f, 0.58f, 0.24f);
    private static readonly Color OutlineColorActive = new(0.96f, 0.72f, 0.34f, 0.40f);
    private static readonly Color SpeedDotHalfColor = new(0.46f, 0.30f, 0.16f, 0.98f);
    private static readonly Color SpeedDotTwoThirdsColor = new(0.58f, 0.44f, 0.24f, 0.98f);
    private static readonly Color SpeedDotFullColor = new(0.42f, 0.78f, 0.36f, 0.98f);

    private static Sprite? _roundedSprite;
    private static Font? _uiFont;

    private GameObject? _canvasObject;
    private Canvas? _canvas;
    private RectTransform? _barRoot;
    private Image? _barBackground;
    private Image? _barGlow;
    private Outline? _barOutline;

    private Text? _timeLabel;
    private Text? _timeValue;
    private Image? _timeBackground;

    private Text? _speedLabel;
    private Button? _decrementButton;
    private Text? _decrementButtonText;
    private Image? _decrementButtonBackground;
    private Button? _incrementButton;
    private Text? _incrementButtonText;
    private Image? _incrementButtonBackground;
    private Image? _speedDot;
    private Image? _speedBackground;

    private Text? _pauseLabel;
    private Button? _pauseButton;
    private Text? _pauseButtonText;
    private Image? _pauseButtonBackground;
    private Image? _pauseBackground;

    private Image? _timeDivider;
    private Image? _speedDivider;

    private string? _renderedTimeLabel;
    private string? _renderedTimeText;
    private string? _renderedPauseButtonText;

    private bool _hasAppliedVisualColors;
    private float _appliedVisualBlend;
    private float _appliedSpeedMultiplier;
    private bool _appliedPauseState;
    private bool _appliedPauseInteractable;
    private float _speedDotMultiplier = float.NaN;
    private int _speedDotPercent;

    private void EnsureUi()
    {
        if (_canvasObject != null)
            return;

        _canvasObject = new GameObject(
            "CombatStatusBarCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        _canvasObject.transform.SetParent(transform, false);

        _canvas = _canvasObject.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = CanvasSortingOrder;

        var scaler = _canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.55f;

        var canvasRect = (RectTransform)_canvasObject.transform;
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        var safeAreaRoot = CreateRect("SafeAreaRoot", _canvasObject.transform);
        safeAreaRoot.anchorMin = Vector2.zero;
        safeAreaRoot.anchorMax = Vector2.one;
        safeAreaRoot.offsetMin = Vector2.zero;
        safeAreaRoot.offsetMax = Vector2.zero;

        _barRoot = CreateRect("BarRoot", safeAreaRoot);
        _barRoot.anchorMin = new Vector2(0.5f, 0f);
        _barRoot.anchorMax = new Vector2(0.5f, 0f);
        _barRoot.pivot = new Vector2(0.5f, 0f);
        _barRoot.anchoredPosition = new Vector2(0f, BarBottomMargin);
        _barRoot.sizeDelta = new Vector2(0f, BarHeight);

        _barBackground = AddImage(_barRoot.gameObject, Colors.CombatBarBackground);
        _barOutline = _barRoot.gameObject.AddComponent<Outline>();
        _barOutline.effectDistance = new Vector2(1f, -1f);
        _barOutline.effectColor = OutlineColorIdle;
        _barOutline.useGraphicAlpha = false;

        _barGlow = AddChildImage("BarGlow", _barRoot, Colors.CombatBarGlow);
        _barGlow.rectTransform.offsetMin = new Vector2(3f, 3f);
        _barGlow.rectTransform.offsetMax = new Vector2(-3f, -3f);
        var glowLayout = _barGlow.gameObject.AddComponent<LayoutElement>();
        glowLayout.ignoreLayout = true;

        var layout = _barRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = SegmentSpacing;
        layout.padding = new RectOffset(18, 18, 8, 8);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = true;
        layout.childForceExpandWidth = false;

        var fitter = _barRoot.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        var speedContent = CreateInteractiveSegment(
            "SpeedSegment",
            _barRoot,
            92f,
            out _speedBackground,
            out _speedLabel
        );
        SetLabel(_speedLabel, "Speed");
        CreateSpeedContent(speedContent);

        _speedDivider = CreateDivider(_barRoot);

        CreateReadoutSegment(
            "TimeSegment",
            _barRoot,
            132f,
            out _timeBackground,
            out _timeLabel,
            out _timeValue
        );
        SetLabel(_timeLabel, "Time");

        _timeDivider = CreateDivider(_barRoot);

        var pauseContent = CreateInteractiveSegment(
            "PauseSegment",
            _barRoot,
            92f,
            out _pauseBackground,
            out _pauseLabel
        );
        SetLabel(_pauseLabel, "Pause");
        CreatePauseContent(pauseContent);
    }

    private void DisposeUi()
    {
        if (_canvasObject == null)
            return;

        Destroy(_canvasObject);
        _canvasObject = null;
        _canvas = null;
        _barRoot = null;
        _barBackground = null;
        _barGlow = null;
        _barOutline = null;
        _timeLabel = null;
        _timeValue = null;
        _timeBackground = null;
        _speedLabel = null;
        _decrementButton = null;
        _decrementButtonText = null;
        _decrementButtonBackground = null;
        _incrementButton = null;
        _incrementButtonText = null;
        _incrementButtonBackground = null;
        _speedDot = null;
        _speedBackground = null;
        _pauseLabel = null;
        _pauseButton = null;
        _pauseButtonText = null;
        _pauseButtonBackground = null;
        _pauseBackground = null;
        _timeDivider = null;
        _speedDivider = null;
        _renderedTimeLabel = null;
        _renderedTimeText = null;
        _renderedPauseButtonText = null;
        // EnsureUi rebuilds elements with placeholder colors, so force a full repaint.
        _hasAppliedVisualColors = false;
    }

    private void SetUiVisible(bool visible)
    {
        if (_canvasObject != null && _canvasObject.activeSelf != visible)
            _canvasObject.SetActive(visible);
    }

    private void RefreshUi()
    {
        if (_canvasObject == null)
            return;

        var shouldDraw = ShouldDraw();
        SetUiVisible(shouldDraw);
        if (!shouldDraw)
            return;

        var pauseInteractable = CanToggleCombatPause();
        if (
            !_hasAppliedVisualColors
            || _appliedVisualBlend != _visualBlend
            || _appliedSpeedMultiplier != CombatSpeedMultiplier
            || _appliedPauseState != IsCombatPaused
            || _appliedPauseInteractable != pauseInteractable
        )
        {
            ApplyVisualColors(pauseInteractable);
            _hasAppliedVisualColors = true;
            _appliedVisualBlend = _visualBlend;
            _appliedSpeedMultiplier = CombatSpeedMultiplier;
            _appliedPauseState = IsCombatPaused;
            _appliedPauseInteractable = pauseInteractable;
        }

        var timeLabel = GetDisplayedTimeLabel();
        if (!string.Equals(_renderedTimeLabel, timeLabel, StringComparison.Ordinal))
        {
            _renderedTimeLabel = timeLabel;
            SetLabel(_timeLabel, timeLabel);
        }

        var timeText = GetDisplayedTimeText();
        if (
            _timeValue != null
            && !string.Equals(_renderedTimeText, timeText, StringComparison.Ordinal)
        )
        {
            _renderedTimeText = timeText;
            _timeValue.text = timeText;
        }

        var pauseButtonText = IsCombatPaused ? ">" : "||";
        if (
            _pauseButtonText != null
            && !string.Equals(_renderedPauseButtonText, pauseButtonText, StringComparison.Ordinal)
        )
        {
            _renderedPauseButtonText = pauseButtonText;
            _pauseButtonText.text = pauseButtonText;
        }
    }

    private void ApplyVisualColors(bool pauseInteractable)
    {
        var barColor = Color.Lerp(BarColorIdle, BarColorActive, _visualBlend);
        var glowColor = Color.Lerp(GlowColorIdle, GlowColorActive, _visualBlend);
        var segmentColor = Color.Lerp(SegmentColorIdle, SegmentColorActive, _visualBlend);
        var labelColor = Color.Lerp(LabelColorIdle, LabelColorActive, _visualBlend);
        var valueColor = Color.Lerp(ValueColorIdle, ValueColorActive, _visualBlend);
        var dividerColor = Color.Lerp(DividerColorIdle, DividerColorActive, _visualBlend);

        SetImageColor(_barBackground, barColor);
        SetImageColor(_barGlow, glowColor);
        SetOutlineColor(
            _barOutline,
            Color.Lerp(OutlineColorIdle, OutlineColorActive, _visualBlend)
        );
        SetImageColor(_timeBackground, segmentColor);
        SetImageColor(_speedBackground, segmentColor);
        SetImageColor(_pauseBackground, segmentColor);
        SetImageColor(_timeDivider, dividerColor);
        SetImageColor(_speedDivider, dividerColor);

        SetTextColor(_timeLabel, labelColor);
        SetTextColor(_speedLabel, labelColor);
        SetTextColor(_pauseLabel, labelColor);
        SetTextColor(_timeValue, valueColor);

        var speedButtonColor = Color.Lerp(
            SpeedButtonColorIdle,
            SpeedButtonColorActive,
            _visualBlend
        );
        var speedButtonPressedColor = Color.Lerp(
            SpeedButtonPressedColorIdle,
            SpeedButtonPressedColorActive,
            _visualBlend
        );
        var speedButtonUnavailableColor = Color.Lerp(
            SpeedButtonUnavailableColorIdle,
            SpeedButtonUnavailableColorActive,
            _visualBlend
        );
        ApplyButtonColors(
            _decrementButton,
            _decrementButtonBackground,
            _decrementButtonText,
            CombatStatusBarButtonKind.Speed,
            CanStepCombatSpeed(-1),
            speedButtonColor,
            speedButtonPressedColor,
            speedButtonUnavailableColor,
            valueColor
        );
        ApplyButtonColors(
            _incrementButton,
            _incrementButtonBackground,
            _incrementButtonText,
            CombatStatusBarButtonKind.Speed,
            CanStepCombatSpeed(1),
            speedButtonColor,
            speedButtonPressedColor,
            speedButtonUnavailableColor,
            valueColor
        );
        RefreshSpeedDot();

        var pauseBaseColor = IsCombatPaused
            ? Color.Lerp(PausedBaseColorIdle, PausedBaseColorActive, _visualBlend)
            : Color.Lerp(UnpausedBaseColorIdle, UnpausedBaseColorActive, _visualBlend);
        var pausePressedColor = IsCombatPaused
            ? Color.Lerp(PausedPressedColorIdle, PausedPressedColorActive, _visualBlend)
            : Color.Lerp(UnpausedPressedColorIdle, UnpausedPressedColorActive, _visualBlend);
        ApplyButtonColors(
            _pauseButton,
            _pauseButtonBackground,
            _pauseButtonText,
            CombatStatusBarButtonKind.Pause,
            pauseInteractable,
            pauseBaseColor,
            pausePressedColor,
            pauseBaseColor,
            valueColor
        );
    }

    private void CreateSpeedContent(RectTransform parent)
    {
        var row = CreateRect("SpeedRow", parent);
        row.anchorMin = Vector2.zero;
        row.anchorMax = Vector2.one;
        row.offsetMin = Vector2.zero;
        row.offsetMax = Vector2.zero;

        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 5f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        (_decrementButton, _decrementButtonBackground, _decrementButtonText) = CreateButton(
            "DecrementButton",
            row,
            "<",
            22f
        );

        var dotContainer = CreateRect("SpeedDotContainer", row);
        var dotLayout = dotContainer.gameObject.AddComponent<LayoutElement>();
        dotLayout.minWidth = 14f;
        dotLayout.preferredWidth = 14f;
        dotLayout.minHeight = 14f;
        dotLayout.preferredHeight = 14f;
        dotLayout.flexibleWidth = 0f;
        dotLayout.flexibleHeight = 0f;

        _speedDot = AddChildImage("SpeedDot", dotContainer, Color.white);
        _speedDot.rectTransform.offsetMin = Vector2.zero;
        _speedDot.rectTransform.offsetMax = Vector2.zero;

        (_incrementButton, _incrementButtonBackground, _incrementButtonText) = CreateButton(
            "IncrementButton",
            row,
            ">",
            22f
        );

        _decrementButton.onClick.AddListener(() => StepCombatSpeed(-1));
        _incrementButton.onClick.AddListener(() => StepCombatSpeed(1));
    }

    private void RefreshSpeedDot()
    {
        if (_speedDot == null)
            return;

        var multiplier = CombatSpeedMultiplier;
        if (multiplier != _speedDotMultiplier)
        {
            _speedDotMultiplier = multiplier;
            _speedDotPercent = Mathf.RoundToInt(multiplier * 100f);
        }

        _speedDot.color = _speedDotPercent switch
        {
            50 => SpeedDotHalfColor,
            67 => SpeedDotTwoThirdsColor,
            _ => SpeedDotFullColor,
        };
    }

    private void CreatePauseContent(RectTransform parent)
    {
        var buttonArea = CreateRect("PauseButtonArea", parent);
        buttonArea.anchorMin = Vector2.zero;
        buttonArea.anchorMax = Vector2.one;
        buttonArea.offsetMin = Vector2.zero;
        buttonArea.offsetMax = Vector2.zero;

        (_pauseButton, _pauseButtonBackground, _pauseButtonText) = CreateButton(
            "PauseButton",
            buttonArea,
            "||",
            0f
        );
        StretchToParent((RectTransform)_pauseButton.transform, 0f, 0f, 0f, 0f);
        var pauseLayout = _pauseButton.gameObject.AddComponent<LayoutElement>();
        pauseLayout.preferredWidth = 0f;
        pauseLayout.flexibleWidth = 1f;
        _pauseButton.onClick.AddListener(() => ToggleCombatPause());
    }

    private RectTransform CreateReadoutSegment(
        string name,
        Transform parent,
        float width,
        out Image background,
        out Text label,
        out Text value
    )
    {
        var segment = CreateSegmentShell(name, parent, width, out background);
        var stack = CreateSegmentStack(segment);
        label = CreateText("Label", stack, 10, FontStyle.Normal, TextAnchor.MiddleCenter);
        AddFixedLayout(label.rectTransform, 12f);

        value = CreateText("Value", stack, 15, FontStyle.Bold, TextAnchor.MiddleCenter);
        value.verticalOverflow = VerticalWrapMode.Overflow;
        AddFixedLayout(value.rectTransform, 18f);
        return segment;
    }

    private RectTransform CreateInteractiveSegment(
        string name,
        Transform parent,
        float width,
        out Image background,
        out Text label
    )
    {
        var segment = CreateSegmentShell(name, parent, width, out background);
        var stack = CreateSegmentStack(segment);
        label = CreateText("Label", stack, 10, FontStyle.Normal, TextAnchor.MiddleCenter);
        AddFixedLayout(label.rectTransform, 12f);

        var content = CreateRect("Content", stack);
        var contentLayout = content.gameObject.AddComponent<LayoutElement>();
        contentLayout.minHeight = 24f;
        contentLayout.preferredHeight = 24f;
        contentLayout.flexibleWidth = 1f;
        contentLayout.flexibleHeight = 0f;
        return content;
    }

    private RectTransform CreateSegmentStack(Transform parent)
    {
        var stack = CreateRect("Stack", parent);
        StretchToParent(stack, SegmentHorizontalInset, SegmentHorizontalInset, 5f, 5f);

        var layout = stack.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 2f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return stack;
    }

    private RectTransform CreateSegmentShell(
        string name,
        Transform parent,
        float width,
        out Image background
    )
    {
        var segment = CreateRect(name, parent);
        var layoutElement = segment.gameObject.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = width;
        layoutElement.minWidth = width;
        layoutElement.flexibleHeight = 1f;
        background = AddImage(segment.gameObject, Color.white);
        return segment;
    }

    private Image CreateDivider(Transform parent)
    {
        var divider = CreateRect("Divider", parent);
        var layoutElement = divider.gameObject.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = 1f;
        layoutElement.minWidth = 1f;
        layoutElement.flexibleHeight = 1f;
        var image = AddChildImage("DividerLine", divider, Color.white);
        image.rectTransform.offsetMin = new Vector2(0f, 4f);
        image.rectTransform.offsetMax = new Vector2(0f, -4f);
        return image;
    }

    private (Button button, Image background, Text label) CreateButton(
        string name,
        Transform parent,
        string text,
        float preferredWidth
    )
    {
        var buttonRect = CreateRect(name, parent);
        if (preferredWidth > 0f)
        {
            var layoutElement = buttonRect.gameObject.AddComponent<LayoutElement>();
            layoutElement.minWidth = preferredWidth;
            layoutElement.preferredWidth = preferredWidth;
            layoutElement.flexibleWidth = 0f;
        }

        var background = AddImage(buttonRect.gameObject, Color.white);
        background.raycastTarget = true;
        var button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = BuildColorBlock(Color.white, Color.white, Color.white);

        var label = CreateText("Label", buttonRect, 16, FontStyle.Bold, TextAnchor.MiddleCenter);
        label.raycastTarget = false;
        StretchToParent(label.rectTransform, 0f, 0f, 0f, 0f);
        label.text = text;
        return (button, background, label);
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static void AddFixedLayout(RectTransform rect, float preferredHeight)
    {
        var layoutElement = rect.gameObject.AddComponent<LayoutElement>();
        layoutElement.minHeight = preferredHeight;
        layoutElement.preferredHeight = preferredHeight;
        layoutElement.flexibleHeight = 0f;
        layoutElement.flexibleWidth = 1f;
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

    private static Image AddChildImage(string name, Transform parent, Color color)
    {
        var imageRect = CreateRect(name, parent);
        StretchToParent(imageRect, 0f, 0f, 0f, 0f);
        return AddImage(imageRect.gameObject, color);
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

    private static Text CreateText(
        string name,
        Transform parent,
        int fontSize,
        FontStyle fontStyle,
        TextAnchor alignment
    )
    {
        var rect = CreateRect(name, parent);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = GetUiFont();
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.supportRichText = false;
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

    private static void ApplyButtonColors(
        Button? button,
        Image? background,
        Text? label,
        CombatStatusBarButtonKind kind,
        bool interactable,
        Color normalColor,
        Color pressedColor,
        Color unavailableColor,
        Color textColor
    )
    {
        if (button == null || background == null || label == null)
            return;

        var visuals = ResolveButtonVisuals(
            kind,
            interactable,
            new CombatStatusBarButtonPalette(
                ToRgba(normalColor),
                ToRgba(pressedColor),
                ToRgba(unavailableColor),
                ToRgba(textColor)
            )
        );

        button.interactable = visuals.Interactable;
        button.colors = BuildColorBlock(
            ToUnityColor(visuals.Normal),
            ToUnityColor(visuals.Pressed),
            ToUnityColor(visuals.Disabled)
        );
        background.color = ToUnityColor(visuals.Background);
        label.color = ToUnityColor(visuals.Text);
    }

    private static CombatStatusBarRgba ToRgba(Color color)
    {
        return new CombatStatusBarRgba(color.r, color.g, color.b, color.a);
    }

    private static Color ToUnityColor(CombatStatusBarRgba color)
    {
        return new Color(color.R, color.G, color.B, color.A);
    }

    private static void SetImageColor(Image? image, Color color)
    {
        if (image != null)
            image.color = color;
    }

    private static void SetTextColor(Text? text, Color color)
    {
        if (text != null)
            text.color = color;
    }

    private static void SetOutlineColor(Outline? outline, Color color)
    {
        if (outline != null)
            outline.effectColor = color;
    }

    private static void SetLabel(Text? label, string content)
    {
        if (label != null)
            label.text = content;
    }

    private static Font GetUiFont()
    {
        return _uiFont ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static Sprite GetRoundedSprite()
    {
        if (_roundedSprite != null)
            return _roundedSprite;

        const int size = 32;
        const int radius = 12;
        const float edgeSoftness = 1.5f;
        var texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var alpha = ResolveRoundedRectAlpha(x + 0.5f, y + 0.5f, size, radius, edgeSoftness);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
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
}
