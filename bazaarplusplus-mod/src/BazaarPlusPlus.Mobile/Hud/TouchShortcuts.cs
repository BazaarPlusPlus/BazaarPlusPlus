using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.Hud;

/// <summary>
/// The touch panels' shortcuts: native-style round icons by the game's settings button,
/// Collection in the lobby, hero select and a run, Ten-Win Builds in a run only. Core places them
/// (<see cref="TouchHudLayout.Shortcut"/>): a corner around the gear, or a row left of it in a run
/// on a screen that gathers the HUD above the board. They hide during a fight or a replay, under
/// a native popup or card inspection, under an open panel, and when the host's rule hides them.
/// History has no shortcut: its entries live in the game's own menus, which each host adds.
/// </summary>
internal sealed class TouchShortcuts : IDisposable
{
    private const string Source = "touch_icon";

    private readonly Func<string, OverlayRequestOutcome> _openCollection;
    private readonly Func<string, OverlayRequestOutcome>? _openLiveBuild;
    private readonly Func<bool> _shown;
    private readonly List<(GameObject Root, bool InRunOnly)> _entries = new();
    private TouchHudIcons? _icons;
    private GameObject? _canvas;
    private RectTransform? _shortcuts;
    private Button? _anchor;
    private RectTransform? _anchorRect;
    private Canvas? _anchorCanvas;
    private bool _inRun;
    private float _nextAnchorCheck;
    private string? _laidOutScreen;
    private TouchRect? _laidOutAnchor;

    /// <param name="openCollection">Opens Collection with the open's source.</param>
    /// <param name="openLiveBuild">Opens Ten-Win Builds, or null when the host has none.</param>
    /// <param name="shown">The host's own rule beyond the HUD's (<see cref="TouchHudData"/>).</param>
    public TouchShortcuts(
        Func<string, OverlayRequestOutcome> openCollection,
        Func<string, OverlayRequestOutcome>? openLiveBuild,
        Func<bool> shown
    )
    {
        _openCollection = openCollection ?? throw new ArgumentNullException(nameof(openCollection));
        _openLiveBuild = openLiveBuild;
        _shown = shown ?? throw new ArgumentNullException(nameof(shown));
    }

    /// <summary>How many times the shortcuts were laid out, for device validation.</summary>
    public long Relayouts { get; private set; }

    /// <summary>The shortcuts' artwork, decoded on first use. Main thread.</summary>
    public TouchHudIcons Icons => _icons ??= new TouchHudIcons();

    /// <summary>Every frame, from the host's Overlay Panel Host.</summary>
    public void Tick(bool panelOpen)
    {
        var port = MobilePlatform.Port;
        var playing = port.IsPlaying();
        // The gear anchors only the shortcuts, which show outside fights: no scene search
        // otherwise. A gear hidden during a fight is kept, so it needs no search, and its
        // shortcuts no new layout, when it shows again.
        if (
            !playing
            && (_anchor == null || !_anchor.gameObject.activeInHierarchy)
            && Time.unscaledTime >= _nextAnchorCheck
        )
        {
            _nextAnchorCheck = Time.unscaledTime + 0.5f;
            _anchor = port.FindSettingsButton(out _inRun);
            _anchorRect = _anchor?.GetComponent<RectTransform>();
            _anchorCanvas = _anchorRect?.GetComponentInParent<Canvas>()?.rootCanvas;
            _laidOutAnchor = null;
        }
        var visible =
            !panelOpen
            && !playing
            && _anchor != null
            && _anchor.gameObject.activeInHierarchy
            && !port.NativeHudBusy()
            && _shown();
        SetActive(_shortcuts?.gameObject, visible);
        if (!visible)
            return;
        Layout(port);
        foreach (var entry in _entries)
            SetActive(entry.Root, !entry.InRunOnly || _inRun);
    }

    /// <summary>Hides the shortcuts until the next <see cref="Tick"/> shows them again.</summary>
    public void Hide() => SetActive(_shortcuts?.gameObject, false);

    private void Layout(IMobilePlatformPort port)
    {
        var gear = port.ScreenBounds(_anchorRect!, _anchorCanvas!, UiKit.ReferenceResolution.y);
        var screen = TouchScreen.Key();
        if (
            gear == _laidOutAnchor
            && string.Equals(screen, _laidOutScreen, StringComparison.Ordinal)
        )
            return;
        _laidOutScreen = screen;
        _laidOutAnchor = gear;
        if (_shortcuts != null)
            UnityEngine.Object.Destroy(_shortcuts.gameObject);
        _entries.Clear();
        _canvas ??= UiKit.CreateOverlayCanvas("BPP.TouchHud", UiKit.SortingOrder);
        var metrics = TouchScreen.Read();
        var art = port.ReadRoundArt(_anchor!, _anchorRect!, _anchorCanvas!);
        _shortcuts = TouchScreen.Child(
            "PanelShortcuts",
            _canvas.transform,
            new(0, 0, metrics.CanvasWidth, metrics.CanvasHeight)
        );
        Add("Collection", Icons.Collection, 0, false, _openCollection);
        if (_openLiveBuild != null)
            Add("TenWinBuilds", Icons.TenWins, 1, true, _openLiveBuild);
        Relayouts++;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.hud.shortcuts_layout"),
            ("anchor", gear),
            ("in_run", _inRun),
            ("target", metrics.MinTarget),
            ("round_layers", art.Layers.Count)
        );

        void Add(
            string name,
            Sprite icon,
            int index,
            bool inRunOnly,
            Func<string, OverlayRequestOutcome> open
        )
        {
            var rect = TouchScreen.Child(
                name,
                _shortcuts,
                TouchHudLayout.Shortcut(metrics, gear, index, _inRun)
            );
            TouchRoundControl.Create(
                rect,
                art,
                icon,
                new(gear.Width, gear.Height),
                "open_" + name,
                () => LogOpenOutcome(name, open(Source))
            );
            _entries.Add((rect.gameObject, inRunOnly));
        }
    }

    private static void LogOpenOutcome(string shortcut, OverlayRequestOutcome outcome)
    {
        if (outcome is OverlayRequestOutcome.Executed)
            return;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.hud.open_skipped"),
            ("shortcut", shortcut),
            ("outcome", outcome)
        );
    }

    private static void SetActive(GameObject? target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    /// <summary>The host stops: the canvas and the artwork go.</summary>
    public void Dispose()
    {
        if (_canvas != null)
            UnityEngine.Object.Destroy(_canvas);
        _canvas = null;
        _shortcuts = null;
        _entries.Clear();
        _icons?.Dispose();
        _icons = null;
    }
}
