#nullable enable
using System.Globalization;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.GameInterop.NativeSkin;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Infrastructure.UiTokens;
using TheBazaar;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed partial class CombatStatusBar
{
    private static readonly Color Ink = InGameTheme.Ink;
    private static readonly Color MutedInk = InGameTheme.InkSoft;
    private NativeGameTypography.OwnedTextPreparation? _uiTypography;
    private bool _nativeSkinRequested;
    private float _nativeSkinRetryAt;
    private bool _nativeSkinFailureReported;
    private GameObject? _canvasObject;
    private Canvas? _canvas;
    private bool _canvasEnabled;
    private SearchOpponentTransition? _nativeCanvasOwner;
    private Canvas? _nativeCanvas;
    private int _layoutScreenWidth;
    private int _layoutScreenHeight;
    private Rect _layoutCameraRect;
    private RectTransform? _barRoot;
    private CanvasGroup? _visibilityGroup;
    private float _visibilityProgress;
    private readonly NativePlayerPortraitBounds _portraitBounds = new();
    private Image? _progressFill;
    private TextMeshProUGUI? _timeValue;
    private TextMeshProUGUI? _speedValue;
    private Button? _pauseButton;
    private CombatPlaybackIcon? _pauseIcon;
    private string? _renderedTimeText;
    private float _renderedSpeed = float.NaN;
    private bool? _renderedPause;
    private bool? _renderedInteractable;

    private void EnsureUi()
    {
        if (_canvasObject != null)
            return;
        // A switched-off bar prepares no fonts and builds no canvas. Gate on the setting alone:
        // ShouldDraw also waits for a run and would move this one-time cost into the first run.
        if (_services?.Config.EnableCombatStatusBarConfig.Value != true)
            return;
        if (
            NativeGameTypography.PrepareOwnedText(out _uiTypography)
                != NativeGameTypography.Outcome.Ready
            || _uiTypography == null
        )
            return;
        if (!EnsureNativeSkin())
            return;

        _canvasObject = new GameObject(
            "CombatStatusBarCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        _canvasObject.transform.SetParent(transform, false);
        var canvas = _canvasObject.GetComponent<Canvas>();
        // Keep the screen-space layout while participating in the native transition rendering.
        // An overlay canvas would bypass the game's camera layers and transition covers.
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.enabled = false;
        _canvas = canvas;
        _canvasEnabled = false;
        var scaler = _canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.55f;

        var barRoot = CreateRect("CombatControls", _canvasObject.transform, 0f, 0f, 220f, 40f);
        barRoot.anchorMin = barRoot.anchorMax = Vector2.zero;
        barRoot.pivot = new Vector2(0.5f, 0.5f);
        barRoot.anchoredPosition = Vector2.zero;
        _barRoot = barRoot;
        _visibilityGroup = barRoot.gameObject.AddComponent<CanvasGroup>();
        SetUiVisible(false);
        CreatePlaque(barRoot);
    }

    private void CreatePlaque(RectTransform root)
    {
        ApplyPlaque(root);
        CreateDivider(root, 0.24f);
        CreateDivider(root, 0.76f);
        _timeValue = CreateText("Elapsed", root, 0f, 0f, 104f, 28f, 18);
        StretchRegion(_timeValue.rectTransform, 0.24f, 0.76f);
        _pauseButton = CreateControlButton("Pause", root, 0.76f, 1f);
        _pauseIcon = CreateRect("PauseIcon", _pauseButton.transform, 0f, 0f, 13f, 13f)
            .gameObject.AddComponent<CombatPlaybackIcon>();
        _pauseIcon.raycastTarget = false;
        _pauseIcon.color = Ink;
        _pauseButton.onClick.AddListener(() => State.ToggleCombatPause());

        var speedButton = CreateControlButton("CycleSpeed", root, 0f, 0.24f);
        _speedValue = CreateText("Multiplier", speedButton.transform, 0f, 0f, 42f, 28f, 18);
        StretchRegion(_speedValue.rectTransform, 0f, 1f);
        speedButton.onClick.AddListener(() => State.CycleCombatSpeed());
        BppLog.InfoEvent(
            new BppLogEvent(
                BppLogFeatureScope.CombatStatusBar,
                "combat_status_bar.native_skin.ready"
            )
        );
    }

    private bool EnsureNativeSkin()
    {
        DesktopNativeSkin.Poll();
        if (
            DesktopNativeSkin.Has(DesktopSkinPart.Frame)
            && DesktopNativeSkin.Has(DesktopSkinPart.BrownCenter)
        )
        {
            _nativeSkinRequested = false;
            _nativeSkinFailureReported = false;
            return true;
        }
        if (DesktopNativeSkin.IsLoading)
        {
            _nativeSkinRequested = true;
            return false;
        }
        if (!_nativeSkinRequested)
        {
            if (Time.realtimeSinceStartup >= _nativeSkinRetryAt)
                _nativeSkinRequested = DesktopNativeSkin.Request();
            return false;
        }

        // The load this bar waited for finished without the sprites.
        _nativeSkinRequested = false;
        var error = DesktopNativeSkin.LastLoadError;
        _nativeSkinRetryAt = Time.realtimeSinceStartup + 30f;
        if (!_nativeSkinFailureReported)
        {
            _nativeSkinFailureReported = true;
            if (error == null)
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.CombatStatusBar,
                        "combat_status_bar.native_skin.unavailable"
                    )
                );
            else
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.CombatStatusBar,
                        "combat_status_bar.native_skin.unavailable"
                    ),
                    error
                );
        }
        return false;
    }

    // The native plaque: the brown centre inset two pixels under the frame.
    private void ApplyPlaque(RectTransform root)
    {
        PlaqueLayer(root, "Backing", DesktopSkinPart.BrownCenter, new Vector2(2f, 2f));
        var progress = CreateRect("CombatProgress", root, 0f, 0f, 0f, 0f);
        progress.anchorMin = Vector2.zero;
        progress.anchorMax = Vector2.one;
        progress.offsetMin = new Vector2(4f, 4f);
        progress.offsetMax = new Vector2(-4f, -4f);
        _progressFill = progress.gameObject.AddComponent<Image>();
        _progressFill.color = InGameTheme.WithAlpha(InGameTheme.Accent, 0.18f);
        _progressFill.raycastTarget = false;
        progress.gameObject.SetActive(false);
        PlaqueLayer(root, "Frame", DesktopSkinPart.Frame, Vector2.zero);
    }

    private static void PlaqueLayer(
        RectTransform root,
        string name,
        DesktopSkinPart part,
        Vector2 inset
    )
    {
        var sprite = DesktopNativeSkin.Get(part)!;
        // Keep the bevel thin at every dock size; stretch only the nine-slice center.
        var thinBevel = sprite.rect.height / 38f * 100f / sprite.pixelsPerUnit;
        DesktopNativeSkin.Layer(root, name, part, Color.white, thinBevel, inset);
    }

    private static void CreateDivider(Transform parent, float x)
    {
        var rect = CreateRect("Divider", parent, 0f, 0f, 0.5f, 0f);
        rect.anchorMin = new Vector2(x, 0.28f);
        rect.anchorMax = new Vector2(x, 0.72f);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = InGameTheme.WithAlpha(InGameTheme.RuleStrong, 0.32f);
        image.raycastTarget = false;
    }

    private static Button CreateControlButton(
        string name,
        Transform parent,
        float left,
        float right
    )
    {
        var rect = CreateRect(name, parent, 0f, 0f, 0f, 0f);
        StretchRegion(rect, left, right);
        var feedback = rect.gameObject.AddComponent<Image>();
        feedback.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = feedback;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = new ColorBlock
        {
            normalColor = Color.clear,
            highlightedColor = InGameTheme.WithAlpha(InGameTheme.Accent, 0.12f),
            pressedColor = InGameTheme.WithAlpha(InGameTheme.Accent, 0.24f),
            selectedColor = InGameTheme.WithAlpha(InGameTheme.Accent, 0.12f),
            disabledColor = Color.clear,
            colorMultiplier = 1f,
            fadeDuration = 0.08f,
        };
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        return button;
    }

    private static void StretchRegion(RectTransform rect, float left, float right)
    {
        rect.anchorMin = new Vector2(left, 0f);
        rect.anchorMax = new Vector2(right, 1f);
        rect.offsetMin = new Vector2(2f, 2f);
        rect.offsetMax = new Vector2(-2f, -2f);
    }

    private void RefreshUi()
    {
        if (_canvasObject == null)
            return;
        if (
            _screenshotSuppressionCount > 0
            || SearchOpponentTransition.IsInTransition
            || _services?.Config.EnableCombatStatusBarConfig.Value != true
        )
        {
            SetUiVisible(false);
            return;
        }
        var visible = ShouldDraw();
        if (visible && !RefreshLayout())
        {
            SetUiVisible(false);
            return;
        }
        AnimateUiVisibility(visible);
        if (!visible)
            return;

        if (_progressFill != null)
        {
            var active = State.IsCombatPlaybackActive;
            if (_progressFill.gameObject.activeSelf != active)
                _progressFill.gameObject.SetActive(active);
            var progress =
                State.TotalCombatFrames > 1
                    ? Mathf.Clamp01(
                        (State.ProcessedCombatFrames - 1f) / (State.TotalCombatFrames - 1f)
                    )
                : State.ProcessedCombatFrames > 0 ? 1f
                : 0f;
            _progressFill.rectTransform.anchorMax = new Vector2(progress, 1f);
            _progressFill.rectTransform.offsetMax = new Vector2(4f - 8f * progress, -4f);
        }
        var time = State.GetDisplayedTimeText();
        if (_timeValue != null && time != _renderedTimeText)
        {
            _timeValue.text = time;
            _renderedTimeText = time;
        }
        if (_timeValue != null)
            _timeValue.color = Color.Lerp(MutedInk, Ink, _visualBlend);
        if (_renderedSpeed != State.CombatSpeedMultiplier)
        {
            _renderedSpeed = State.CombatSpeedMultiplier;
            if (_speedValue != null)
                _speedValue.text =
                    State.CombatSpeedMultiplier.ToString("0.##", CultureInfo.InvariantCulture)
                    + "x";
        }
        var interactable = State.CanToggleCombatPause();
        if (_pauseButton != null && _renderedInteractable != interactable)
        {
            _renderedInteractable = interactable;
            _pauseButton.interactable = interactable;
            _renderedPause = null;
        }
        if (_pauseIcon != null && _renderedPause != State.IsCombatPaused)
        {
            _renderedPause = State.IsCombatPaused;
            _pauseIcon.ShowPlay = State.IsCombatPaused;
            _pauseIcon.SetVerticesDirty();
            var icon = State.IsCombatPaused ? InGameTheme.AccentBright : Ink;
            _pauseIcon.color = interactable ? icon : InGameTheme.WithAlpha(icon, icon.a * .4f);
        }
    }

    private bool RefreshLayout()
    {
        if (
            _canvasObject == null
            || _canvas == null
            || _barRoot == null
            || !_portraitBounds.TryRead(out var frame, out var bottom, out var viewport)
        )
            return false;

        var transition = Singleton<SearchOpponentTransition>.Instance;
        if (transition != _nativeCanvasOwner || _nativeCanvas == null)
        {
            _nativeCanvasOwner = transition;
            _nativeCanvas = transition != null ? transition.GetComponent<Canvas>() : null;
        }
        var nativeCanvas = _nativeCanvas;
        if (nativeCanvas == null || nativeCanvas.renderMode != RenderMode.ScreenSpaceCamera)
            return false;
        var camera = nativeCanvas.worldCamera;
        if (camera == null || !camera.isActiveAndEnabled)
            return false;
        var depth = nativeCanvas.planeDistance;
        if (!float.IsFinite(depth) || depth <= camera.nearClipPlane || depth >= camera.farClipPlane)
            return false;
        var canvas = _canvas;
        // Write only what changed: in the shop these inputs hold still for minutes.
        var placementChanged = !_canvasEnabled;
        if (canvas.worldCamera != camera)
        {
            canvas.worldCamera = camera;
            placementChanged = true;
        }
        if (canvas.planeDistance != depth)
        {
            canvas.planeDistance = depth;
            placementChanged = true;
        }
        var sortingLayerID = nativeCanvas.sortingLayerID;
        if (canvas.sortingLayerID != sortingLayerID)
            canvas.sortingLayerID = sortingLayerID;
        // AboveBoard clears the scene depth before drawing native foreground UI.
        // Its transition backgrounds start at order 0 (new day) and 14 (PvP).
        if (canvas.sortingOrder != -1)
            canvas.sortingOrder = -1;
        var layer = ObjectLayers.aboveBoardLayer;
        if (_canvasObject.layer != layer)
        {
            _canvasObject.layer = layer;
            Helpers.UpdateLayerRecursive(_canvasObject, layer);
        }
        SetCanvasEnabled(true);
        var scale = canvas.scaleFactor;
        if (
            !CombatStatusBarLayout.TryPlace(
                frame.xMin,
                frame.xMax,
                bottom,
                viewport.xMin,
                viewport.xMax,
                viewport.yMin,
                viewport.yMax,
                scale,
                out var height
            )
        )
            return false;

        var canvasRect = (RectTransform)_canvasObject.transform;
        var screenWidth = Screen.width;
        var screenHeight = Screen.height;
        var cameraRect = camera.pixelRect;
        if (
            screenWidth != _layoutScreenWidth
            || screenHeight != _layoutScreenHeight
            || cameraRect != _layoutCameraRect
        )
        {
            _layoutScreenWidth = screenWidth;
            _layoutScreenHeight = screenHeight;
            _layoutCameraRect = cameraRect;
            placementChanged = true;
        }
        // A canvas that was hidden, re-targeted, or resized has not driven its root rect yet;
        // refresh it before projecting into it. Otherwise the rect from the last frame holds.
        if (placementChanged)
            canvasRect.ForceUpdateRectTransforms();
        if (
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                new Vector2(frame.center.x, viewport.yMin + (bottom - viewport.yMin) * 0.5f),
                camera,
                out var position
            )
        )
            return false;
        var anchoredPosition = position - canvasRect.rect.min;
        if (_barRoot.anchoredPosition != anchoredPosition)
            _barRoot.anchoredPosition = anchoredPosition;
        var sizeDelta = new Vector2(frame.width / scale, height / scale);
        if (_barRoot.sizeDelta != sizeDelta)
        {
            _barRoot.sizeDelta = sizeDelta;
            var contentScale = Mathf.Min(
                1f,
                Mathf.Min(_barRoot.rect.width / 220f, _barRoot.rect.height / 40f)
            );
            if (_timeValue != null)
                _timeValue.fontSize = 18f * contentScale;
            if (_speedValue != null)
                _speedValue.fontSize = 18f * contentScale;
            if (_pauseIcon != null)
                _pauseIcon.rectTransform.sizeDelta = new Vector2(13f, 13f) * contentScale;
        }
        return true;
    }

    private void AnimateUiVisibility(bool visible)
    {
        var duration = visible ? 0.18f : 0.12f;
        _visibilityProgress = Mathf.MoveTowards(
            _visibilityProgress,
            visible ? 1f : 0f,
            Time.unscaledDeltaTime / duration
        );
        ApplyUiVisibility(visible);
    }

    private void SetUiVisible(bool visible)
    {
        _visibilityProgress = visible ? 1f : 0f;
        ApplyUiVisibility(visible);
    }

    private void ApplyUiVisibility(bool interactive)
    {
        var drawn = _visibilityProgress > 0f;
        // Keep the scaler active while hidden; input stops as soon as the exit begins.
        SetCanvasEnabled(drawn);
        if (_barRoot != null)
        {
            if (_barRoot.gameObject.activeSelf != drawn)
                _barRoot.gameObject.SetActive(drawn);
            var eased = Mathf.SmoothStep(0f, 1f, _visibilityProgress);
            _barRoot.localScale = Vector3.one * Mathf.Lerp(0.93f, 1f, eased);
            if (_visibilityGroup != null)
            {
                _visibilityGroup.alpha = eased;
                _visibilityGroup.interactable = interactive && drawn;
                _visibilityGroup.blocksRaycasts = interactive && drawn;
            }
        }
    }

    private void SetCanvasEnabled(bool enabled)
    {
        if (_canvas == null || _canvasEnabled == enabled)
            return;
        _canvas.enabled = enabled;
        _canvasEnabled = enabled;
    }

    private void DisposeUi()
    {
        if (_canvasObject != null)
        {
            _canvasObject.SetActive(false);
            Destroy(_canvasObject);
        }
        _canvasObject = null;
        _canvas = null;
        _canvasEnabled = false;
        _nativeCanvasOwner = null;
        _nativeCanvas = null;
        _layoutScreenWidth = 0;
        _layoutScreenHeight = 0;
        _layoutCameraRect = default;
        _barRoot = null;
        _visibilityGroup = null;
        _visibilityProgress = 0f;
        _uiTypography = null;
        _progressFill = null;
        _timeValue = null;
        _speedValue = null;
        _pauseButton = null;
        _pauseIcon = null;
        _renderedTimeText = null;
        _renderedSpeed = float.NaN;
        _renderedPause = null;
        _renderedInteractable = null;
    }

    private static RectTransform CreateRect(
        string name,
        Transform parent,
        float x,
        float y,
        float width,
        float height
    )
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
        return rect;
    }

    private TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        float x,
        float y,
        float width,
        float height,
        int size
    )
    {
        var text = CreateRect(name, parent, x, y, width, height)
            .gameObject.AddComponent<TextMeshProUGUI>();
        var typography = _uiTypography;
        if (typography?.Apply(text) != NativeGameTypography.Outcome.Applied)
            throw new InvalidOperationException("Native game typography became unavailable.");
        text.fontSize = size;
        text.fontStyle = FontStyles.Normal;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.richText = false;
        text.raycastTarget = false;
        text.color = Ink;
        return text;
    }
}
