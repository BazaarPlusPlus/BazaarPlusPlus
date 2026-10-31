using BazaarPlusPlus.Android.Collection;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Android.Supporters;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile.Collection;
using BazaarPlusPlus.Mobile.LiveBuild;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>The touch panels' entries: native-style icons in a corner around the settings button,
/// Collection below it in the lobby and in a run, Ten-Win Builds left of it in a run only; History in the game's own menus, a
/// tile in the lobby's MENU grid and a row in the in-run menu. Their lifecycle stays with the Overlay Panel Host. Views and
/// catalog data are still built on first open.</summary>
internal sealed class AndroidTouchUi
{
    private const string EntrySource = "touch_icon";
    private const string MenuSource = "native_menu";

    private readonly AndroidOverlayPanels _panels;
    private readonly Action<string>? _openHistory;
    private readonly TouchLiveBuildData? _liveBuildData;
    private readonly TouchCollectionEntry _collection;
    private readonly AndroidCollectionCatalog _collectionCatalog;
    private readonly TouchLiveBuildEntry? _liveBuild;
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
    private string? _shortcutScreen;
    private TouchRect? _shortcutAnchor;
    private bool _mountFailed;
    private long _relayouts;

    public AndroidTouchUi(
        AndroidOverlayPanels panels,
        Action<string>? openHistory,
        TouchLiveBuildData? liveBuildData,
        AndroidSupporterData? supporters,
        AndroidCollectionCatalog collectionCatalog
    )
    {
        _panels = panels ?? throw new ArgumentNullException(nameof(panels));
        _collectionCatalog =
            collectionCatalog ?? throw new ArgumentNullException(nameof(collectionCatalog));
        _openHistory = openHistory;
        _liveBuildData = liveBuildData;
        // Collection and Ten-Win Builds are the Mobile UI's (ADR-0025): each registers itself
        // through the port and builds under the touch root on its first open.
        var run = new AndroidCollectionRunReads();
        _collection = new TouchCollectionEntry(
            new TouchCollectionData(
                collectionCatalog,
                new EmbeddedCollectionSourceCatalog(AndroidCardFacets.IsHeroAvailable),
                new AndroidCollectionDayTierSource(run),
                new AndroidCollectionHeroPreferenceStore(),
                run,
                AndroidComposition.NoEncounterState.Instance,
                supporters
            ),
            () => _root?.transform
        );
        if (liveBuildData != null)
            _liveBuild = new TouchLiveBuildEntry(liveBuildData, () => _root?.transform);
    }

    /// <summary>Panel lifecycle and shortcut layout counters for device validation.</summary>
    public BppLogField[] Summary() =>
        [
            ("touch_icon_relayouts", _relayouts),
            ("live_build_opened", _liveBuild?.Panel?.Opened ?? 0),
            ("live_build_closed", _liveBuild?.Panel?.Closed ?? 0),
            ("live_build_leases", _liveBuild?.Panel?.Leases ?? 0),
            ("live_build_refreshes", _liveBuild?.Panel?.Refreshes ?? 0),
            ("live_build_refreshes_completed", _liveBuild?.Panel?.RefreshesCompleted ?? 0),
            ("live_build_relayouts", _liveBuild?.Panel?.Relayouts ?? 0),
            ("collection_relayouts", _collection.Panel?.Relayouts ?? 0),
            .. _panels.BackSummary(),
        ];

    /// <summary>Main thread, when the app leaves the foreground: persists staged preferences.</summary>
    public void OnAppPaused() =>
        Guarded("touch_ui:pause_collection", _collection.FlushHeroPreference);

    /// <summary>Main thread: mounts the UI once the game's font is available.</summary>
    public void EnsureMounted()
    {
        if (_root != null || _mountFailed)
            return;
        if (AndroidGameFont.Font == null)
            return;
        try
        {
            if (!AndroidTouchRoot.Register())
            {
                _mountFailed = true;
                return;
            }
            Mount();
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
        Guarded("touch_ui:dispose_collection", _collection.Dispose);
        Guarded("touch_ui:dispose_live_build", () => _liveBuild?.Dispose());
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

    private void Mount()
    {
        _root = UiKit.CreateOverlayCanvas("BPP.TouchUi", UiKit.SortingOrder);
        _root.AddComponent<AndroidTouchRoot>();
        AndroidTouchRoot.Frame = Frame;
        _icons = new AndroidControlIcons();
        if (_openHistory != null)
        {
            Func<bool> offered = () => true;
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
            var playing = IsPlaying();
            // The gear anchors only the shortcuts, which show outside fights: no scene search
            // otherwise. A gear hidden during a fight is kept, so it
            // needs no search, and its shortcuts no new layout, when it shows again.
            if (
                !playing
                && (_anchor == null || !_anchor.gameObject.activeInHierarchy)
                && Time.unscaledTime >= _nextAnchorCheck
            )
            {
                _nextAnchorCheck = Time.unscaledTime + 0.5f;
                _anchor = AndroidNativeHud.FindSettingsButton(out _inRun);
                _anchorRect = _anchor?.GetComponent<RectTransform>();
                _anchorCanvas = _anchorRect?.GetComponentInParent<Canvas>()?.rootCanvas;
                _shortcutAnchor = null;
            }
            var visible =
                _panels.OpenPanelId == null
                && !playing
                && !AndroidNativeHud.HasPopup
                && !AndroidNativeHud.IsInspectingCard
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
                _liveBuildData.Warm("touch_ui");
            // Built ahead of the first open, outside fights and replays; an open panel paces it.
            if (
                !playing
                && _panels.OpenPanelId != TouchCollectionEntry.PanelId
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
            UiKit.ReferenceResolution.y
        );
        var screen = TouchScreen.Key();
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
        var metrics = TouchScreen.Read();
        var chrome = TouchChrome.For(metrics);
        var art = AndroidNativeArt.RoundArtFor(_anchor!, _anchorRect!, _anchorCanvas!);
        _shortcuts = TouchScreen.Child(
            "PanelShortcuts",
            _root!.transform,
            new(0, 0, metrics.CanvasWidth, metrics.CanvasHeight)
        );
        // A corner around the gear: Collection below it, Ten-Win Builds left of it. In a run on a
        // screen that gathers the HUD above the board (ScreenFill), the board reaches up
        // to just under the gear, so both sit in a row left of it.
        var row = _inRun && ScreenFill.GathersHudAbove(metrics.CanvasWidth / metrics.CanvasHeight);
        Add(
            "Collection",
            _icons!.Collection,
            row
                ? TouchHudLayout.ShortcutInRow(metrics, bounds, 0)
                : TouchHudLayout.ShortcutBelow(metrics, bounds),
            () => true,
            () => LogOpenOutcome(TouchCollectionEntry.PanelId, _collection.RequestOpen(EntrySource))
        );
        Add(
            "TenWinBuilds",
            _icons.TenWins,
            row
                ? TouchHudLayout.ShortcutInRow(metrics, bounds, 1)
                : TouchHudLayout.ShortcutBeside(metrics, bounds),
            () => _inRun && LiveBuildOffered(),
            () => LogOpenOutcome(TouchLiveBuildEntry.PanelId, _liveBuild!.RequestOpen(EntrySource))
        );
        _relayouts++;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.ui.shortcuts_layout"),
            ("anchor", bounds),
            ("target", chrome.Target),
            ("round_layers", art.Layers.Count)
        );

        void Add(string name, Sprite icon, TouchRect area, Func<bool> offered, Action open)
        {
            var rect = TouchScreen.Child(name, _shortcuts, area);
            AndroidRoundControl.Create(
                rect,
                art,
                icon,
                new(bounds.Width, bounds.Height),
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

    private bool LiveBuildOffered() => _liveBuild != null;

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
