#nullable enable

using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed partial class CombatStatusBar
{
    private const float BarHeight = 60f;
    private const float BarBottomMargin = 0f;
    private const float SegmentSpacing = 4f;
    private const int CanvasSortingOrder = 10;

    private static Sprite? _roundedSprite;
    private static Font? _uiFont;

    private GameObject? _canvasObject;
    private Canvas? _canvas;
    private RectTransform? _barRoot;
    private Image? _barBackground;
    private Image? _barGlow;

    private Text? _timeLabel;
    private Text? _timeValue;
    private Image? _timeBackground;

    private Text? _frameLabel;
    private Text? _frameValue;
    private Image? _frameBackground;

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
    private Image? _frameDivider;
    private Image? _speedDivider;

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
        _barRoot.sizeDelta = new Vector2(462f, BarHeight);

        _barBackground = AddImage(_barRoot.gameObject, new Color(0.06f, 0.07f, 0.09f, 0.90f));
        _barGlow = AddChildImage("BarGlow", _barRoot, new Color(0.28f, 0.22f, 0.12f, 0.10f));
        _barGlow.rectTransform.offsetMin = new Vector2(3f, 3f);
        _barGlow.rectTransform.offsetMax = new Vector2(-3f, -3f);

        var layout = _barRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = SegmentSpacing;
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = true;
        layout.childForceExpandWidth = false;

        var timeSegment = CreateReadoutSegment(
            "TimeSegment",
            _barRoot,
            116f,
            out _timeBackground,
            out _timeLabel,
            out _timeValue
        );
        SetLabel(_timeLabel, "Time");

        _timeDivider = CreateDivider(_barRoot);

        var frameSegment = CreateReadoutSegment(
            "FrameSegment",
            _barRoot,
            116f,
            out _frameBackground,
            out _frameLabel,
            out _frameValue
        );
        SetLabel(_frameLabel, "Frame");

        _frameDivider = CreateDivider(_barRoot);

        var speedSegment = CreateInteractiveSegment(
            "SpeedSegment",
            _barRoot,
            84f,
            out _speedBackground,
            out _speedLabel
        );
        SetLabel(_speedLabel, "Speed");
        CreateSpeedContent(speedSegment);

        _speedDivider = CreateDivider(_barRoot);

        var pauseSegment = CreateInteractiveSegment(
            "PauseSegment",
            _barRoot,
            110f,
            out _pauseBackground,
            out _pauseLabel
        );
        SetLabel(_pauseLabel, "Pause");
        CreatePauseContent(pauseSegment);
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
        _timeLabel = null;
        _timeValue = null;
        _timeBackground = null;
        _frameLabel = null;
        _frameValue = null;
        _frameBackground = null;
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
        _frameDivider = null;
        _speedDivider = null;
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

        var barColor = Color.Lerp(
            new Color(0.06f, 0.07f, 0.09f, 0.90f),
            new Color(0.16f, 0.12f, 0.08f, 0.96f),
            _visualBlend
        );
        var glowColor = Color.Lerp(
            new Color(0.20f, 0.24f, 0.28f, 0.08f),
            new Color(0.44f, 0.30f, 0.14f, 0.20f),
            _visualBlend
        );
        var segmentColor = Color.Lerp(
            new Color(0.11f, 0.13f, 0.16f, 0.90f),
            new Color(0.23f, 0.18f, 0.11f, 0.92f),
            _visualBlend
        );
        var labelColor = Color.Lerp(
            new Color(0.62f, 0.67f, 0.74f, 0.88f),
            new Color(0.90f, 0.84f, 0.62f, 0.96f),
            _visualBlend
        );
        var valueColor = Color.Lerp(
            new Color(0.84f, 0.87f, 0.93f, 0.96f),
            new Color(1f, 0.96f, 0.90f, 1f),
            _visualBlend
        );
        var dividerColor = Color.Lerp(
            new Color(0.42f, 0.46f, 0.53f, 0.22f),
            new Color(0.84f, 0.74f, 0.44f, 0.42f),
            _visualBlend
        );

        SetImageColor(_barBackground, barColor);
        SetImageColor(_barGlow, glowColor);
        SetImageColor(_timeBackground, segmentColor);
        SetImageColor(_frameBackground, segmentColor);
        SetImageColor(_speedBackground, segmentColor);
        SetImageColor(_pauseBackground, segmentColor);
        SetImageColor(_timeDivider, dividerColor);
        SetImageColor(_frameDivider, dividerColor);
        SetImageColor(_speedDivider, dividerColor);

        SetTextColor(_timeLabel, labelColor);
        SetTextColor(_frameLabel, labelColor);
        SetTextColor(_speedLabel, labelColor);
        SetTextColor(_pauseLabel, labelColor);
        SetTextColor(_timeValue, valueColor);
        SetTextColor(_frameValue, valueColor);

        SetLabel(_timeLabel, GetDisplayedTimeLabel());
        if (_timeValue != null)
            _timeValue.text = GetDisplayedTimeText();
        if (_frameValue != null)
            _frameValue.text = GetDisplayedFrameText();

        var speedButtonColor = Color.Lerp(
            new Color(0.26f, 0.30f, 0.36f, 0.92f),
            new Color(0.48f, 0.33f, 0.13f, 0.95f),
            _visualBlend
        );
        var speedButtonPressedColor = Color.Lerp(
            new Color(0.35f, 0.39f, 0.46f, 1f),
            new Color(0.66f, 0.47f, 0.16f, 1f),
            _visualBlend
        );
        var speedButtonDisabledColor = new Color(0.22f, 0.24f, 0.28f, 0.45f);
        ApplyButtonColors(
            _decrementButton,
            _decrementButtonBackground,
            _decrementButtonText,
            CanStepCombatSpeed(-1),
            speedButtonColor,
            speedButtonPressedColor,
            speedButtonDisabledColor,
            valueColor
        );
        ApplyButtonColors(
            _incrementButton,
            _incrementButtonBackground,
            _incrementButtonText,
            CanStepCombatSpeed(1),
            speedButtonColor,
            speedButtonPressedColor,
            speedButtonDisabledColor,
            valueColor
        );
        RefreshSpeedDot();

        var pauseInteractable = CanToggleCombatPause();
        var pauseBaseColor = IsCombatPaused
            ? Color.Lerp(
                new Color(0.28f, 0.33f, 0.40f, 0.95f),
                new Color(0.54f, 0.40f, 0.16f, 0.96f),
                _visualBlend
            )
            : Color.Lerp(
                new Color(0.24f, 0.27f, 0.33f, 0.90f),
                new Color(0.41f, 0.31f, 0.13f, 0.93f),
                _visualBlend
            );
        var pausePressedColor = IsCombatPaused
            ? Color.Lerp(
                new Color(0.36f, 0.42f, 0.50f, 1f),
                new Color(0.68f, 0.50f, 0.18f, 1f),
                _visualBlend
            )
            : Color.Lerp(
                new Color(0.32f, 0.36f, 0.43f, 1f),
                new Color(0.56f, 0.41f, 0.15f, 1f),
                _visualBlend
            );
        var pauseDisabledColor = new Color(0.22f, 0.24f, 0.28f, 0.45f);
        ApplyButtonColors(
            _pauseButton,
            _pauseButtonBackground,
            _pauseButtonText,
            pauseInteractable,
            pauseBaseColor,
            pausePressedColor,
            pauseDisabledColor,
            valueColor
        );
        if (_pauseButtonText != null)
            _pauseButtonText.text = IsCombatPaused ? ">" : "||";
    }

    private void CreateSpeedContent(RectTransform parent)
    {
        var row = CreateRect("SpeedRow", parent);
        row.anchorMin = Vector2.zero;
        row.anchorMax = Vector2.one;
        row.offsetMin = new Vector2(8f, 8f);
        row.offsetMax = new Vector2(-8f, -22f);

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

        var color = Mathf.RoundToInt(CombatSpeedMultiplier * 100f) switch
        {
            50 => new Color(0.46f, 0.30f, 0.16f, 0.98f),
            67 => new Color(0.58f, 0.44f, 0.24f, 0.98f),
            _ => new Color(0.42f, 0.78f, 0.36f, 0.98f),
        };

        _speedDot.color = color;
    }

    private void CreatePauseContent(RectTransform parent)
    {
        var buttonArea = CreateRect("PauseButtonArea", parent);
        buttonArea.anchorMin = Vector2.zero;
        buttonArea.anchorMax = Vector2.one;
        buttonArea.offsetMin = new Vector2(12f, 6f);
        buttonArea.offsetMax = new Vector2(-12f, -20f);

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
        label = CreateText("Label", segment, 10, FontStyle.Normal, TextAnchor.MiddleCenter);
        AnchorTopStretch(label.rectTransform, 6f, 12f);

        value = CreateText("Value", segment, 15, FontStyle.Bold, TextAnchor.MiddleCenter);
        value.verticalOverflow = VerticalWrapMode.Overflow;
        StretchToParent(value.rectTransform, 8f, 8f, 18f, 6f);
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
        label = CreateText("Label", segment, 10, FontStyle.Normal, TextAnchor.MiddleCenter);
        AnchorTopStretch(label.rectTransform, 6f, 12f);
        return segment;
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
        var image = AddImage(divider.gameObject, Color.white);
        divider.offsetMin = new Vector2(0f, 8f);
        divider.offsetMax = new Vector2(0f, -8f);
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

    private static void AnchorTopStretch(RectTransform rect, float top, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -top);
        rect.sizeDelta = new Vector2(0f, height);
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
            : new Color(textColor.r, textColor.g, textColor.b, 0.45f);
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

    private static void SetLabel(Text? label, string content)
    {
        if (label != null)
            label.text = content;
    }

    private static Font GetUiFont()
    {
        _uiFont ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
                var alpha = IsInsideRoundedRect(x, y, size, radius) ? 1f : 0f;
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

    private static bool IsInsideRoundedRect(int x, int y, int size, int radius)
    {
        var clampedX = Mathf.Clamp(x, radius, size - radius - 1);
        var clampedY = Mathf.Clamp(y, radius, size - radius - 1);
        var deltaX = x - clampedX;
        var deltaY = y - clampedY;
        return (deltaX * deltaX) + (deltaY * deltaY) <= radius * radius;
    }
}
