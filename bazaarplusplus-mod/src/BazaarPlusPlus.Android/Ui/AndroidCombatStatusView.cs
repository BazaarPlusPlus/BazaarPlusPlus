using BazaarPlusPlus.Android.Game;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CombatStatusBar;
using Il2CppTheBazaar;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Ui;

internal sealed class AndroidCombatStatusView(
    AndroidCombatStatus playback,
    AndroidOverlayPanels panels,
    AndroidSettings settings
) : IDisposable
{
    private GameObject? _root;
    private TextMeshProUGUI? _time;
    private Image? _pauseIcon;
    private AndroidPlaybackIcons? _icons;
    private TextMeshProUGUI? _speedText;
    private Button? _pause;
    private Button? _speed;
    private string? _screen;
    private AndroidNativeSkin? _skin;

    // Hidden frames between checks for a run or replay to build the plaque ahead in.
    private const int BuildAheadCheckFrames = 30;

    private int _hiddenFrames;

    // Built ahead while the native art was not yet found: build again when first shown.
    private bool _builtWithoutArt;

    // What the view last wrote, so a frame that changes nothing makes no Unity call.
    private bool _shown;
    private string? _shownTime;
    private bool? _shownPaused;
    private float _shownSpeed = float.NaN;
    private (bool Active, bool CanToggle)? _shownControls;

    public void Tick()
    {
        var state = playback.State;
        state.OnVisibilitySettingChanged(settings.CombatTimer.Value);
        var visible =
            settings.CombatTimer.Value
            && state.IsCombatPlaybackActive
            && (Data.HasActiveRun || AppState.CurrentState?.TryCast<ReplayState>() != null)
            && panels.OpenPanelId == null
            && !AndroidNativeHud.HasPopup
            && !AndroidNativeHud.IsInspectingCard;
        if (!visible)
        {
            if (_shown)
                _root?.SetActive(false);
            _shown = false;
            BuildAhead(state);
            return;
        }
        var screen = AndroidTouchScreen.Key();
        if (_root == null || screen != _screen || _builtWithoutArt)
        {
            var font = AndroidGameFont.Font;
            if (font == null)
                return;
            Build(font, screen, ahead: false);
        }
        if (!_shown)
            _root!.SetActive(true);
        _shown = true;
        // The state hands back the same string until the displayed time changes.
        var time = state.GetDisplayedTimeText();
        if (!string.Equals(time, _shownTime, StringComparison.Ordinal))
        {
            _shownTime = time;
            _time!.text = CombatStatusBarState.WithTabularDigits(time);
        }
        var paused = state.IsCombatPaused;
        if (paused != _shownPaused)
        {
            _shownPaused = paused;
            _pauseIcon!.sprite = paused ? _icons!.Play : _icons!.Pause;
        }
        var speed = state.CombatSpeedMultiplier;
        if (speed != _shownSpeed)
        {
            _shownSpeed = speed;
            _speedText!.text =
                speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "×";
        }
        var controls = (
            Active: state.IsCombatPlaybackActive,
            CanToggle: state.CanToggleCombatPause()
        );
        if (controls != _shownControls)
        {
            _shownControls = controls;
            _pause!.gameObject.SetActive(controls.Active);
            _speed!.gameObject.SetActive(controls.Active);
            _pause.interactable = controls.CanToggle;
            _speed.interactable = controls.Active;
        }
    }

    // Outside a fight, in a run or a replay, and once per scene (a scene load disposes the view):
    // the plaque is built hidden, so the first fight's first frame does not pay for the build.
    private void BuildAhead(CombatStatusBarState state)
    {
        if (_root != null || !settings.CombatTimer.Value || state.IsCombatPlaybackActive)
            return;
        if (_hiddenFrames++ % BuildAheadCheckFrames != 0)
            return;
        if (!Data.HasActiveRun && AppState.CurrentState?.TryCast<ReplayState>() == null)
            return;
        var font = AndroidGameFont.Font;
        if (font == null)
            return;
        Build(font, AndroidTouchScreen.Key(), ahead: true);
        _root!.SetActive(false);
    }

    private void Build(TMP_FontAsset font, string screen, bool ahead)
    {
        DestroyCanvas();
        _screen = screen;
        // The glyphs are owned sprites that outlive the canvas; only Dispose destroys them.
        var icons = _icons ??= new AndroidPlaybackIcons();
        var chrome = TouchChrome.For(AndroidTouchScreen.Read());
        // Beneath the native card drawer (Tooltip_Mobile_P, order 150) so its slide-out covers
        // the clock as it reappears; above the board HUD (order 0).
        _root = AndroidUiKit.CreateOverlayCanvas("BPP.CombatStatus", 149);
        var layout = TouchHudLayout.Playback(chrome.Metrics);
        var plaque = AndroidTouchScreen.Child("ClockPlaque", _root.transform, layout.Panel);
        var background = AndroidUiKit.Fill(plaque, AndroidUiKit.ButtonColor, false);
        if (_skin == null || _skin.NeedsRefresh)
            _skin = new AndroidNativeSkin();
        _skin.Select(background, false);
        // Only the button sprite is read here; a plaque built ahead without it tries again.
        _builtWithoutArt = ahead && background.sprite == null;
        var rule = chrome.Metrics.Dp(.5f);
        var inset = chrome.Metrics.Dp(8);
        Divider(
            new TouchRect(
                layout.Controls.Left + inset,
                layout.Controls.Top,
                layout.Controls.Width - 2 * inset,
                rule
            )
        );
        Divider(
            new TouchRect(
                layout.Controls.CenterX,
                layout.Controls.Top + inset,
                rule,
                layout.Controls.Height - 2 * inset
            )
        );
        _time = AndroidUiKit.Text(
            AndroidTouchScreen.Child("Time", _root.transform, layout.Time),
            font,
            "",
            chrome.Metrics.Sp(22),
            TextAlignmentOptions.Center,
            AndroidUiKit.TextColor
        );
        Outline(_time);
        var half = layout.Controls with { Width = layout.Controls.Width / 2 };
        _speedText = AndroidUiKit.Text(
            AndroidTouchScreen.Child("Multiplier", _root.transform, half),
            font,
            "",
            chrome.Metrics.Sp(16),
            TextAlignmentOptions.Center,
            AndroidUiKit.TextColor
        );
        Outline(_speedText);
        var glyph = AndroidUiKit.Fixed(
            "PauseIcon",
            AndroidTouchScreen.Child("PauseCell", _root.transform, half with { Left = half.Right }),
            new(.5f, .5f),
            Vector2.zero,
            new(chrome.Metrics.Dp(16), chrome.Metrics.Dp(16))
        );
        _pauseIcon = AndroidUiKit.Fill(glyph, Color.white, false);
        _pauseIcon.sprite = icons.Pause;
        _pauseIcon.preserveAspect = true;
        var shadow = glyph.gameObject.AddComponent<Shadow>();
        shadow.effectColor = InGameTheme.WithAlpha(InGameTheme.Outline, 0.85f);
        shadow.effectDistance = new Vector2(chrome.Metrics.Dp(1), -chrome.Metrics.Dp(1));
        _speed = Control(
            AndroidTouchScreen.Child("Speed", _root.transform, layout.Speed),
            _speedText,
            () => playback.State.CycleCombatSpeed()
        );
        _pause = Control(
            AndroidTouchScreen.Child("Pause", _root.transform, layout.Pause),
            _pauseIcon,
            playback.TogglePause
        );
        BazaarPlusPlus.Infrastructure.BppLog.InfoEvent(
            new BazaarPlusPlus.Infrastructure.Logging.BppLogEvent(
                BazaarPlusPlus.Infrastructure.Logging.BppLogFeatureScope.Android,
                "android.combat_status.layout"
            ),
            ("ahead", ahead),
            ("panel", layout.Panel),
            ("pause", layout.Pause),
            ("speed", layout.Speed)
        );
    }

    private static void Outline(TextMeshProUGUI label)
    {
        label.outlineColor = InGameTheme.Outline;
        label.outlineWidth = 0.1f;
    }

    private void Divider(TouchRect area) =>
        AndroidUiKit.Fill(
            AndroidTouchScreen.Child("Divider", _root!.transform, area),
            InGameTheme.WithAlpha(InGameTheme.RuleStrong, .65f),
            false
        );

    /// <summary>A transparent hit area that tints the visible label or glyph it drives.</summary>
    private static Button Control(RectTransform rect, Graphic visual, Action action)
    {
        AndroidUiKit.Fill(rect, new Color(0, 0, 0, .001f));
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = visual;
        button.onClick.AddListener((UnityAction)action);
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        return button;
    }

    /// <summary>A scene change or the host's stop: the canvas and the glyphs go.</summary>
    public void Dispose()
    {
        DestroyCanvas();
        _icons?.Dispose();
        _icons = null;
        _hiddenFrames = 0;
    }

    private void DestroyCanvas()
    {
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
        _root = null;
        _builtWithoutArt = false;
        _shown = false;
        _shownTime = null;
        _shownPaused = null;
        _shownSpeed = float.NaN;
        _shownControls = null;
    }
}
