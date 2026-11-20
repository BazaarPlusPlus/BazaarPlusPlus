using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.Hud;

internal sealed class TouchShortcuts : IDisposable
{
    private const string Source = "touch_icon";

    private readonly Func<string, OverlayRequestOutcome> _openCollection;
    private readonly Func<string, OverlayRequestOutcome>? _openLiveBuild;
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

    public TouchShortcuts(
        Func<string, OverlayRequestOutcome> openCollection,
        Func<string, OverlayRequestOutcome>? openLiveBuild
    )
    {
        _openCollection = openCollection ?? throw new ArgumentNullException(nameof(openCollection));
        _openLiveBuild = openLiveBuild;
    }

    public long Relayouts { get; private set; }

    public TouchHudIcons Icons => _icons ??= new TouchHudIcons();

    public void Tick(bool panelOpen)
    {
        var port = MobilePlatform.Port;
        var playing = port.PlayState().IsPlaying;
        // Keep the hidden gear reference across combat to avoid another scene search and relayout.
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
        var anchorAvailable = _anchor != null && _anchor.gameObject.activeInHierarchy;
        var visible = HudVisibility.TouchShortcuts(default, panelOpen, playing, anchorAvailable);
        if (visible)
            visible = HudVisibility.TouchShortcuts(
                port.ReadNativeHud(),
                panelOpen,
                playing,
                anchorAvailable
            );
        SetActive(_shortcuts?.gameObject, visible);
        if (!visible)
            return;
        Layout(port);
        foreach (var entry in _entries)
            SetActive(entry.Root, !entry.InRunOnly || _inRun);
    }

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
