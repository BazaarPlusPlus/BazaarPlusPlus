using BazaarPlusPlus.Android.Collection;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.LiveBuild;
using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Android.Supporters;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTMPro;
using UnityEngine;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>Direct native-style icon entries for the three touch panels; their lifecycle stays
/// with the Overlay Panel Host. Views and catalog data are still built on first open.</summary>
internal sealed class AndroidTouchUi
{
    private const int SortingOrder = 30000;

    private const string CollectionPanelId = "collection";
    private const string LiveBuildPanelId = "live_build";
    private const string EntrySource = "touch_icon";

    private readonly AndroidSettings _android;
    private readonly AndroidOverlayPanels _panels;
    private readonly Action<string>? _openHistory;
    private readonly AndroidLiveBuildData? _liveBuildData;
    private readonly AndroidSupporterData? _supporters;
    private readonly AndroidPanelHandle _collectionHandle;
    private readonly AndroidPanelHandle _liveBuildHandle;
    private readonly List<(GameObject Root, Func<bool> Offered)> _entries = new();

    private GameObject? _root;
    private RectTransform? _shortcuts;
    private AndroidControlIcons? _icons;
    private UnityEngine.UI.Button? _anchor;
    private float _nextAnchorCheck;
    private TMP_FontAsset? _font;
    private AndroidCollectionPanel? _collection;
    private AndroidLiveBuildPanel? _liveBuild;
    private string _screenKey = string.Empty;
    private bool _mountFailed;
    private long _relayouts;

    public AndroidTouchUi(
        AndroidSettings android,
        AndroidOverlayPanels panels,
        Action<string>? openHistory,
        AndroidLiveBuildData? liveBuildData,
        AndroidSupporterData? supporters
    )
    {
        _android = android ?? throw new ArgumentNullException(nameof(android));
        _panels = panels ?? throw new ArgumentNullException(nameof(panels));
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
                _anchor = AndroidNativeHud.FindSettingsButton();
                _screenKey = "";
            }
            var visible =
                _android.TouchMenu.Value
                && _panels.OpenPanelId == null
                && !Il2CppTheBazaar.Data.IsInCombat
                && Il2CppTheBazaar.AppState.CurrentState?.TryCast<Il2CppTheBazaar.ReplayState>()
                    == null
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
                _liveBuildData.Warm("setting_on");
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
            _anchor!.GetComponent<RectTransform>(),
            AndroidUiKit.ReferenceResolution.y
        );
        var key = AndroidTouchScreen.Key() + "|" + bounds;
        if (key == _screenKey)
            return;
        _screenKey = key;
        if (_shortcuts != null)
            UnityEngine.Object.Destroy(_shortcuts.gameObject);
        _entries.Clear();
        var metrics = AndroidTouchScreen.Read();
        var chrome = TouchChrome.For(metrics);
        var layout = TouchHudLayout.Shortcuts(metrics, bounds, 3);
        var art = new AndroidNativeArt();
        _shortcuts = AndroidTouchScreen.Child(
            "PanelShortcuts",
            _root!.transform,
            new(0, 0, metrics.CanvasWidth, metrics.CanvasHeight)
        );
        Add(
            "Collection",
            _icons!.Collection,
            () => _android.CollectionPanel.Value,
            () => LogOpenOutcome(CollectionPanelId, _collectionHandle.RequestOpen(EntrySource)),
            0
        );
        Add(
            "History",
            _icons.History,
            () => _openHistory != null && _android.HistoryPanel.Value,
            () => _openHistory?.Invoke(EntrySource),
            1
        );
        Add(
            "TenWinBuilds",
            _icons.TenWins,
            LiveBuildOffered,
            () => LogOpenOutcome(LiveBuildPanelId, _liveBuildHandle.RequestOpen(EntrySource)),
            2
        );
        _relayouts++;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.ui.shortcuts_layout"),
            ("anchor", bounds),
            ("first", layout[0]),
            ("target", chrome.Target)
        );

        void Add(string name, Sprite icon, Func<bool> offered, Action open, int index)
        {
            var rect = AndroidTouchScreen.Child(name, _shortcuts, layout[index]);
            AndroidRoundControl.Create(
                rect,
                art.RoundLayers,
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

    private static void SetActive(GameObject? target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }
}
