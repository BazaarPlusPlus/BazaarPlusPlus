using BazaarPlusPlus.Android.Collection;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.LiveBuild;
using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Android.Supporters;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTMPro;
using UnityEngine;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>The touch panels' entries: in a run, native-style icons in a corner around the settings
/// button, Collection below it and Ten-Win Builds left of it; History in the game's own menus, a
/// tile in the lobby's MENU grid and a row in the in-run menu. Their lifecycle stays with the Overlay Panel Host. Views and
/// catalog data are still built on first open.</summary>
internal sealed class AndroidTouchUi
{
    private const int SortingOrder = 30000;

    private const string CollectionPanelId = "collection";
    private const string LiveBuildPanelId = "live_build";
    private const string EntrySource = "touch_icon";
    private const string MenuSource = "native_menu";

    private readonly AndroidSettings _android;
    private readonly AndroidOverlayPanels _panels;
    private readonly Action<string>? _openHistory;
    private readonly AndroidLiveBuildData? _liveBuildData;
    private readonly AndroidSupporterData? _supporters;
    private readonly AndroidPanelHandle _collectionHandle;
    private readonly AndroidPanelHandle _liveBuildHandle;
    private readonly List<(GameObject Root, Func<bool> Offered)> _entries = new();
    private bool _inRun;

    private GameObject? _root;
    private RectTransform? _shortcuts;
    private AndroidControlIcons? _icons;
    private AndroidNavigationMenuEntry? _historyMenu;
    private AndroidFightMenuEntry? _historyFightMenu;
    private UnityEngine.UI.Button? _anchor;
    private RectTransform? _anchorRect;
    private Canvas? _anchorCanvas;
    private float _nextAnchorCheck;
    private TMP_FontAsset? _font;
    private AndroidCollectionPanel? _collection;
    private readonly AndroidCollectionCatalog _collectionCatalog;
    private AndroidLiveBuildPanel? _liveBuild;
    private string? _shortcutScreen;
    private TouchRect? _shortcutAnchor;
    private bool _mountFailed;
    private long _relayouts;

    public AndroidTouchUi(
        AndroidSettings android,
        AndroidOverlayPanels panels,
        Action<string>? openHistory,
        AndroidLiveBuildData? liveBuildData,
        AndroidSupporterData? supporters,
        AndroidCollectionCatalog collectionCatalog
    )
    {
        _android = android ?? throw new ArgumentNullException(nameof(android));
        _panels = panels ?? throw new ArgumentNullException(nameof(panels));
        _collectionCatalog =
            collectionCatalog ?? throw new ArgumentNullException(nameof(collectionCatalog));
        _openHistory = openHistory;
        _liveBuildData = liveBuildData;
        _supporters = supporters;
        _collectionHandle = panels.Register(handle => new OverlayPanelRegistration(
            CollectionPanelId,
            onOpen: () => OpenCollectionPanel(handle),
            onClose: reason => _collection?.Close(handle.Describe(reason)),
            tick: (_, _) => _collection?.Tick()
        )
        {
            Enabled = () => _android.CollectionPanel.Value,
            IsModalInputActive = () => _collection?.HasModalBackStep == true,
            DismissModal = () => _collection?.DismissModalBackStep(),
        });
        _liveBuildHandle = panels.Register(handle => new OverlayPanelRegistration(
            LiveBuildPanelId,
            onOpen: () => OpenLiveBuildPanel(handle),
            onClose: reason => _liveBuild?.Close(handle.Describe(reason)),
            tick: (_, _) => _liveBuild?.Tick()
        )
        {
            Enabled = LiveBuildOffered,
            IsModalInputActive = () => _liveBuild?.IsInspecting == true,
            DismissModal = () => _liveBuild?.CloseInspection(),
        });
    }

    /// <summary>Panel lifecycle and shortcut layout counters for device validation.</summary>
    public BppLogField[] Summary() =>
        [
            ("touch_icon_relayouts", _relayouts),
            ("live_build_opened", _liveBuild?.Opened ?? 0),
            ("live_build_closed", _liveBuild?.Closed ?? 0),
            ("live_build_leases", _liveBuild?.Leases ?? 0),
            ("live_build_refreshes", _liveBuild?.Refreshes ?? 0),
            ("live_build_refreshes_completed", _liveBuild?.RefreshesCompleted ?? 0),
            ("live_build_relayouts", _liveBuild?.Relayouts ?? 0),
            ("collection_relayouts", _collection?.Relayouts ?? 0),
            .. _panels.BackSummary(),
        ];

    /// <summary>Main thread: mounts the UI once the game's font is available.</summary>
    public void EnsureMounted()
    {
        if (_root != null || _mountFailed)
            return;
        var font = AndroidGameFont.Font;
        if (font == null)
            return;
        try
        {
            if (!AndroidTouchRoot.Register())
            {
                _mountFailed = true;
                return;
            }
            Mount(font);
            BppLog.InfoEvent(new BppLogEvent(BppLogFeatureScope.Android, "android.ui.mounted"));
        }
        catch (Exception exception)
        {
            _mountFailed = true;
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.ui.mount_failed"),
                exception
            );
        }
    }

    /// <summary>
    /// The host stops: the panels leave the Overlay Panel Host, then give their leases back and
    /// their views go. One step that throws never skips the next.
    /// </summary>
    public void Dispose()
    {
        _collectionHandle.Dispose();
        _liveBuildHandle.Dispose();
        Guarded("touch_ui:dispose_collection", () => _collection?.Close("stopped"));
        Guarded("touch_ui:dispose_live_build", () => _liveBuild?.Dispose());
        _liveBuild = null;
        AndroidTouchRoot.Frame = null;
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
        _icons?.Dispose();
    }

    private static void Guarded(string probe, Action step)
    {
        try
        {
            step();
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError(probe, exception);
        }
    }

    private void Mount(TMP_FontAsset font)
    {
        _root = AndroidUiKit.CreateOverlayCanvas("BPP.TouchUi", SortingOrder);
        _root.AddComponent<AndroidTouchRoot>();
        AndroidTouchRoot.Frame = Frame;
        _font = font;
        _icons = new AndroidControlIcons();
        if (_openHistory != null)
        {
            Func<bool> offered = () => _android.HistoryPanel.Value;
            Action open = () =>
                AndroidUiActions.Run("open_History", () => _openHistory(MenuSource));
            _historyMenu = new AndroidNavigationMenuEntry(
                "History",
                HistoryPanelText.Title,
                _icons.History,
                offered,
                open
            );
            _historyFightMenu = new AndroidFightMenuEntry(
                "History",
                HistoryPanelText.Title,
                offered,
                () => !IsPlaying(),
                open
            );
        }
    }

    private void Frame()
    {
        try
        {
            if (
                (_anchor == null || !_anchor.gameObject.activeInHierarchy)
                && Time.unscaledTime >= _nextAnchorCheck
            )
            {
                _nextAnchorCheck = Time.unscaledTime + 0.5f;
                _anchor = AndroidNativeHud.FindSettingsButton(out _inRun);
                _anchorRect = _anchor?.GetComponent<RectTransform>();
                _anchorCanvas = _anchorRect?.GetComponentInParent<Canvas>()?.rootCanvas;
                _shortcutAnchor = null;
            }
            var playing = IsPlaying();
            var visible =
                _android.TouchMenu.Value
                && _panels.OpenPanelId == null
                && !playing
                && !AndroidNativeHud.HasPopup
                && !AndroidNativeHud.IsInspectingCard
                && _inRun
                && _anchor != null
                && _anchor.gameObject.activeInHierarchy;
            SetActive(_shortcuts?.gameObject, visible);
            if (visible)
            {
                LayoutShortcuts();
                foreach (var entry in _entries)
                    SetActive(entry.Root, entry.Offered());
            }
            if (LiveBuildOffered() && !_liveBuildData!.Warmed)
                _liveBuildData.Warm("setting_on");
            // Built ahead of the first open, outside fights and replays; an open panel paces it.
            if (
                _android.CollectionPanel.Value
                && !playing
                && _panels.OpenPanelId != CollectionPanelId
                && _collectionCatalog.IsBuilding
            )
                _collectionCatalog.Advance(AndroidCollectionCatalogPace.Warm);
            _historyMenu?.Tick();
            _historyFightMenu?.Tick();
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                _panels.TryConsumeBack("keyboard");
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("touch_ui", exception);
        }
    }

    private void LayoutShortcuts()
    {
        var bounds = AndroidNativeHud.ScreenBounds(
            _anchorRect!,
            _anchorCanvas!,
            AndroidUiKit.ReferenceResolution.y
        );
        var screen = AndroidTouchScreen.Key();
        if (
            bounds == _shortcutAnchor
            && string.Equals(screen, _shortcutScreen, StringComparison.Ordinal)
        )
            return;
        _shortcutScreen = screen;
        _shortcutAnchor = bounds;
        if (_shortcuts != null)
            UnityEngine.Object.Destroy(_shortcuts.gameObject);
        _entries.Clear();
        var metrics = AndroidTouchScreen.Read();
        var chrome = TouchChrome.For(metrics);
        var layers = AndroidNativeArt.RoundLayersFor(_anchor!, _anchorRect!, _anchorCanvas!);
        _shortcuts = AndroidTouchScreen.Child(
            "PanelShortcuts",
            _root!.transform,
            new(0, 0, metrics.CanvasWidth, metrics.CanvasHeight)
        );
        // A corner around the gear: Collection below it, Ten-Win Builds left of it.
        Add(
            "Collection",
            _icons!.Collection,
            TouchHudLayout.ShortcutBelow(metrics, bounds),
            () => _android.CollectionPanel.Value,
            () => LogOpenOutcome(CollectionPanelId, _collectionHandle.RequestOpen(EntrySource))
        );
        Add(
            "TenWinBuilds",
            _icons.TenWins,
            TouchHudLayout.ShortcutBeside(metrics, bounds),
            LiveBuildOffered,
            () => LogOpenOutcome(LiveBuildPanelId, _liveBuildHandle.RequestOpen(EntrySource))
        );
        _relayouts++;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.ui.shortcuts_layout"),
            ("anchor", bounds),
            ("target", chrome.Target),
            ("round_layers", layers.Count)
        );

        void Add(string name, Sprite icon, TouchRect area, Func<bool> offered, Action open)
        {
            var rect = AndroidTouchScreen.Child(name, _shortcuts, area);
            AndroidRoundControl.Create(
                rect,
                layers,
                icon,
                chrome.Metrics.Dp(32),
                () => AndroidUiActions.Run("open_" + name, open)
            );
            _entries.Add((rect.gameObject, offered));
        }
    }

    private static void LogOpenOutcome(string panel, OverlayRequestOutcome outcome)
    {
        if (outcome is OverlayRequestOutcome.Executed)
            return;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.touch_icon.open_skipped"),
            ("panel", panel),
            ("outcome", outcome)
        );
    }

    // The host's open: the switch and the combat gate already passed. The panel is built on its
    // first open; a build that throws is the host's to log and roll back.
    private bool OpenCollectionPanel(AndroidPanelHandle handle)
    {
        if (_root == null || _font == null)
            return false;
        _collection ??= new AndroidCollectionPanel(
            _root.transform,
            _font,
            _collectionCatalog,
            _supporters,
            reason => handle.RequestClose(reason)
        );
        return _collection.Open();
    }

    private bool LiveBuildOffered() => _android.LiveBuildPanel.Value && _liveBuildData != null;

    private bool OpenLiveBuildPanel(AndroidPanelHandle handle)
    {
        if (_root == null || _font == null || _liveBuildData == null)
            return false;
        _liveBuild ??= new AndroidLiveBuildPanel(
            _root.transform,
            _font,
            _liveBuildData,
            reason => handle.RequestClose(reason)
        );
        return _liveBuild.Open(handle.OpenSource);
    }

    // A combat or a replay is on: no panel opens, as on the desktop.
    private static bool IsPlaying() =>
        Il2CppTheBazaar.Data.IsInCombat
        || Il2CppTheBazaar.AppState.CurrentState?.TryCast<Il2CppTheBazaar.ReplayState>() != null;

    private static void SetActive(GameObject? target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }
}
