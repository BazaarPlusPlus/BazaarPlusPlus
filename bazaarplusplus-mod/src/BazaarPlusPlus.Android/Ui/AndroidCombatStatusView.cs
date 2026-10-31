using BazaarPlusPlus.Android.Game;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Mobile.Ui;
using Il2CppTheBazaar;
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
    private NativeSkin? _skin;

    // The vault the plaque sits beside, read from the board as the desktop reads the hero
    // portrait, every few shown frames; as on the desktop, no vault on screen means no plaque. A
    // vault that moves, or a screen that changes, lays the plaque out again once the new place
    // has held still for a few reads; a plaque shown again after hiding lays out at once.
    private const int VaultReadFrames = 5;
    private const int VaultStableReads = 2;
    private readonly AndroidVaultBounds _vaultBounds = new();
    private int _shownFrames;
    private int _missedFrames;
    private TouchRect _vault;
    private string? _layoutKey;
    private string? _keyScreen;
    private (int, int, int, int)? _keyVault;
    private float? _boardTop;
    private int? _keyBoardTop;
    private TouchRelayoutDebounce? _relayout;

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
    private bool? _shownCanToggle;
    private Color? _shownTimeColor;

    // As the desktop bar: the time fades to muted ink outside a fight and back to ink in one.
    private float _visualBlend;

    public void Tick()
    {
        var state = playback.State;
        state.OnVisibilitySettingChanged(settings.CombatTimer.Value);
        _visualBlend = CombatStatusBarState.AdvanceVisualBlend(
            _visualBlend,
            state.IsCombatPlaybackActive,
            Time.unscaledDeltaTime
        );
        // As the desktop bar: shown for the whole run or replay, not only while a fight plays.
        var visible =
            settings.CombatTimer.Value
            && (Data.HasActiveRun || AppState.CurrentState?.TryCast<ReplayState>() != null)
            && panels.OpenPanelId == null
            && !AndroidNativeHud.HasPopup
            && !AndroidNativeHud.IsInspectingCard;
        if (!visible)
        {
            Hide();
            BuildAhead();
            return;
        }
        var appearing = !_shown;
        // With no vault last read, as in the main menu beside a run to resume, check again only
        // every so often.
        var read =
            _layoutKey == null
                ? _missedFrames++ % BuildAheadCheckFrames == 0
                : appearing || _shownFrames++ % VaultReadFrames == 0;
        var key = read ? LayoutKey() : _layoutKey;
        if (key == null)
        {
            Hide();
            return;
        }
        if (
            _root == null
            || _builtWithoutArt
            || (appearing && !string.Equals(key, _relayout?.LaidOut, StringComparison.Ordinal))
            || (read && _relayout != null && _relayout.Observe(key, settingUp: false))
        )
        {
            var font = AndroidGameFont.Font;
            if (font == null)
                return;
            Build(font, key, ahead: false);
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
        var timeColor = Color.Lerp(InGameTheme.InkSoft, UiKit.TextColor, _visualBlend);
        if (timeColor != _shownTimeColor)
        {
            _shownTimeColor = timeColor;
            _time!.color = timeColor;
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
        // Speed stays tappable between fights and sets the next fight's pace; pause only
        // applies to a playing fight, so it dims outside one.
        var canToggle = state.CanToggleCombatPause();
        if (canToggle != _shownCanToggle)
        {
            _shownCanToggle = canToggle;
            _pause!.interactable = canToggle;
        }
    }

    private void Hide()
    {
        if (_shown)
            _root?.SetActive(false);
        _shown = false;
    }

    // While hidden by a panel, popup or inspection, in a run or a replay, and once per scene (a
    // scene load disposes the view): the plaque is built hidden, so the frame it reappears on
    // does not pay for the build.
    private void BuildAhead()
    {
        if (_root != null || !settings.CombatTimer.Value)
            return;
        if (_hiddenFrames++ % BuildAheadCheckFrames != 0)
            return;
        if (!Data.HasActiveRun && AppState.CurrentState?.TryCast<ReplayState>() == null)
            return;
        var font = AndroidGameFont.Font;
        if (font == null || LayoutKey() is not { } key)
            return;
        Build(font, key, ahead: true);
        _root!.SetActive(false);
    }

    /// <summary>
    /// The screen and the vault's place in whole canvas units, or null with no vault the plaque
    /// can sit beside; the string is formatted again only when one of them changed.
    /// </summary>
    private string? LayoutKey()
    {
        if (
            !_vaultBounds.TryRead(UiKit.ReferenceResolution.y, out var vault)
            || !TouchHudLayout.FitsBesideVault(TouchScreen.Read(), vault)
        )
            return _layoutKey = null;
        var screen = TouchScreen.Key();
        var rounded = (
            (int)MathF.Round(vault.Left),
            (int)MathF.Round(vault.Top),
            (int)MathF.Round(vault.Width),
            (int)MathF.Round(vault.Height)
        );
        _vault = vault;
        // On a screen the board is drawn nearer on, the HUD gathers above it (AndroidScreenFill).
        _boardTop = AndroidScreenFill.BoardTop * UiKit.ReferenceResolution.y / Screen.height;
        var boardTop = _boardTop is { } top ? (int?)MathF.Round(top) : null;
        if (
            _layoutKey != null
            && ReferenceEquals(screen, _keyScreen)
            && rounded == _keyVault
            && boardTop == _keyBoardTop
        )
            return _layoutKey;
        _keyScreen = screen;
        _keyVault = rounded;
        _keyBoardTop = boardTop;
        return _layoutKey = $"{screen}|vault {rounded}|board top {boardTop}";
    }

    private void Build(TMP_FontAsset font, string key, bool ahead)
    {
        DestroyCanvas();
        if (_relayout == null)
            _relayout = new TouchRelayoutDebounce(key, VaultStableReads);
        else
            _relayout.LaidOutAt(key);
        // The glyphs are owned sprites that outlive the canvas; only Dispose destroys them.
        var icons = _icons ??= new AndroidPlaybackIcons();
        var chrome = TouchChrome.For(TouchScreen.Read());
        // Beneath the native card drawer (Tooltip_Mobile_P, order 150) so its slide-out covers
        // the clock as it reappears; above the board HUD (order 0).
        _root = UiKit.CreateOverlayCanvas("BPP.CombatStatus", 149);
        var layout = TouchHudLayout.Playback(chrome.Metrics, _vault, _boardTop);
        var plaque = TouchScreen.Child("ClockPlaque", _root.transform, layout.Panel);
        var background = UiKit.Fill(plaque, UiKit.ButtonColor, false);
        if (_skin == null || _skin.NeedsRefresh)
            _skin = new NativeSkin();
        _skin.Select(background, false);
        // Only the button sprite is read here; a plaque built ahead without it tries again.
        _builtWithoutArt = ahead && background.sprite == null;
        var rule = chrome.Metrics.Dp(.5f);
        var inset = chrome.Padding;
        Divider(
            layout.Row
                ? new TouchRect(
                    layout.Controls.Left,
                    layout.Controls.Top + inset,
                    rule,
                    layout.Controls.Height - 2 * inset
                )
                : new TouchRect(
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
        _time = UiKit.Text(
            TouchScreen.Child("Time", _root.transform, layout.Time),
            font,
            "",
            chrome.ClockTimeFont,
            TextAlignmentOptions.Center,
            UiKit.TextColor
        );
        Outline(_time);
        var half = layout.Controls with { Width = layout.Controls.Width / 2 };
        _speedText = UiKit.Text(
            TouchScreen.Child("Multiplier", _root.transform, half),
            font,
            "",
            chrome.ClockSpeedFont,
            TextAlignmentOptions.Center,
            UiKit.TextColor
        );
        Outline(_speedText);
        var glyph = UiKit.Fixed(
            "PauseIcon",
            TouchScreen.Child("PauseCell", _root.transform, half with { Left = half.Right }),
            new(.5f, .5f),
            Vector2.zero,
            new(chrome.Metrics.Dp(16), chrome.Metrics.Dp(16))
        );
        _pauseIcon = UiKit.Fill(glyph, Color.white, false);
        _pauseIcon.sprite = icons.Pause;
        _pauseIcon.preserveAspect = true;
        var shadow = glyph.gameObject.AddComponent<Shadow>();
        shadow.effectColor = InGameTheme.WithAlpha(InGameTheme.Outline, 0.85f);
        shadow.effectDistance = new Vector2(chrome.Metrics.Dp(1), -chrome.Metrics.Dp(1));
        _speed = Control(
            TouchScreen.Child("Speed", _root.transform, layout.Speed),
            _speedText,
            () => playback.State.CycleCombatSpeed()
        );
        _pause = Control(
            TouchScreen.Child("Pause", _root.transform, layout.Pause),
            _pauseIcon,
            playback.TogglePause
        );
        BazaarPlusPlus.Infrastructure.BppLog.InfoEvent(
            new BazaarPlusPlus.Infrastructure.Logging.BppLogEvent(
                BazaarPlusPlus.Infrastructure.Logging.BppLogFeatureScope.Android,
                "android.combat_status.layout"
            ),
            ("ahead", ahead),
            ("vault", _vault),
            ("board_top", _boardTop),
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
        UiKit.Fill(
            TouchScreen.Child("Divider", _root!.transform, area),
            InGameTheme.WithAlpha(InGameTheme.RuleStrong, .65f),
            false
        );

    /// <summary>A transparent hit area that tints the visible label or glyph it drives.</summary>
    private static Button Control(RectTransform rect, Graphic visual, Action action)
    {
        UiKit.Fill(rect, new Color(0, 0, 0, .001f));
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
        _relayout = null;
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
        _shownCanToggle = null;
        _shownTimeColor = null;
    }
}
