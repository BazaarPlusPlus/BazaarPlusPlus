using BazaarPlusPlus.Android.Collection;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.LiveBuild;
using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Android.Supporters;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using Il2CppTMPro;
using UnityEngine;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The touch entry for the panels the desktop opens by hotkey: a BPP button at the left edge of
/// the safe area opens a small menu of History, Collection, and Ten-Win Builds, each offered while
/// its switch is on. The switches and every other BPP setting live in the game's own settings
/// dialog (<see cref="Settings.AndroidNativeSettingsSection"/>), as on the desktop. An entry closes
/// this menu and asks the Overlay Panel Host (<see cref="AndroidOverlayPanels"/>) to open its
/// panel: History (<see cref="AndroidHistoryPanel"/>), the full-screen
/// <see cref="AndroidCollectionPanel"/>, and the full-screen <see cref="AndroidLiveBuildPanel"/>
/// when the record chain composed its data (<see cref="AndroidLiveBuildData"/>), which warms only
/// while that panel's switch is on. Collection and Ten-Win Builds register here, eagerly, so their
/// switch-off is seen before they are first built; they are built on their first open. The button
/// hides while a panel or the settings dialog's BazaarPlusPlus section is open. The menu is not a
/// Main Overlay Panel: it closes on every scene change, and the back key closes it after the
/// panels. The geometry is Core's <see cref="TouchLauncherLayout"/>, laid out again whenever the
/// screen changes.
/// </summary>
internal sealed class AndroidTouchUi
{
    private const int SortingOrder = 30000;

    private static readonly LocalizedTextSet MenuTitle = new("BazaarPlusPlus", "BazaarPlusPlus");
    private static readonly LocalizedTextSet NoPanels = new(
        "Every panel is off: turn one on in Settings, BazaarPlusPlus",
        "所有面板均已关闭：可在游戏设置的 BazaarPlusPlus 中开启",
        "所有面板均已關閉：可在遊戲設定的 BazaarPlusPlus 中開啟"
    );

    private const string CollectionPanelId = "collection";
    private const string LiveBuildPanelId = "live_build";
    private const string TouchMenuSource = "touch_menu";

    private readonly AndroidSettings _android;
    private readonly AndroidOverlayPanels _panels;
    private readonly Action<string>? _openHistory;
    private readonly AndroidLiveBuildData? _liveBuildData;
    private readonly AndroidSupporterData? _supporters;
    private readonly AndroidPanelHandle _collectionHandle;
    private readonly AndroidPanelHandle _liveBuildHandle;
    private readonly List<GameObject> _entries = new();

    private GameObject? _root;
    private RectTransform? _launcher;
    private TextMeshProUGUI? _launcherLabel;
    private GameObject? _dim;
    private RectTransform? _menu;
    private TMP_FontAsset? _font;
    private AndroidCollectionPanel? _collection;
    private AndroidLiveBuildPanel? _liveBuild;
    private string _screenKey = string.Empty;
    private bool _mountFailed;
    private long _menuOpened;
    private long _menuClosed;
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

    /// <summary>The menu's and the Live Build panel's counters, which a device run checks return to balance.</summary>
    public BppLogField[] Summary() =>
        [
            ("touch_menu_opened", _menuOpened),
            ("touch_menu_closed", _menuClosed),
            ("touch_menu_relayouts", _relayouts),
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
        _panels.SetTouchMenu(null);
        _collectionHandle.Dispose();
        _liveBuildHandle.Dispose();
        Guarded("touch_ui:dispose_menu", () => Close("stopped"));
        Guarded("touch_ui:dispose_collection", () => _collection?.Close("stopped"));
        Guarded("touch_ui:dispose_live_build", () => _liveBuild?.Dispose());
        _liveBuild = null;
        AndroidTouchRoot.Frame = null;
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
        var root = _root.transform;

        _launcher = AndroidTouchScreen.Child("Launcher", root, default);
        var (_, _, launcherLabel) = AndroidUiKit.Button(_launcher, font, "BPP", 30f, 0f, Toggle);
        _launcherLabel = launcherLabel;

        var dim = AndroidUiKit.Rect(
            "Dim",
            root,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        AndroidUiKit.Fill(dim, AndroidUiKit.DimColor);
        var dimButton = dim.gameObject.AddComponent<UnityEngine.UI.Button>();
        dimButton.onClick.AddListener((UnityEngine.Events.UnityAction)(() => Close("outside_tap")));
        _dim = dim.gameObject;
        _dim.SetActive(false);

        // Asked after the panels, on a back press and on a scene load.
        _panels.SetTouchMenu(reason =>
        {
            if (_menu == null)
                return false;
            Close(reason);
            return true;
        });
        LayoutLauncher(force: true);
    }

    private void Frame()
    {
        try
        {
            // The button stays out of an open panel and of the game's settings dialog, which it
            // would cover.
            SetActive(
                _launcher?.gameObject,
                _android.TouchMenu.Value
                    && _panels.OpenPanelId == null
                    && !Settings.AndroidNativeSettingsSection.AnyVisible
            );
            if (!_android.TouchMenu.Value && _menu != null)
                Close("touch_menu_off");
            LayoutLauncher(force: false);
            if (LiveBuildOffered() && !_liveBuildData!.Warmed)
                _liveBuildData.Warm("setting_on");
            // The back key also reaches the game's escape hook (BackKeyPatches); a press both
            // see closes one panel.
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                _panels.TryConsumeBack("keyboard");
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("touch_ui", exception);
        }
    }

    // The button follows the safe area and the density; an open menu is rebuilt for the screen.
    private void LayoutLauncher(bool force)
    {
        var key = AndroidTouchScreen.Key();
        if (!force && key == _screenKey)
            return;
        _screenKey = key;
        _relayouts++;
        var layout = TouchLauncherLayout.Compute(AndroidTouchScreen.Read(), 0);
        if (_launcher != null)
            AndroidTouchScreen.Place(_launcher, layout.Launcher);
        if (_launcherLabel != null)
        {
            _launcherLabel.fontSize = layout.LauncherFont;
            AndroidUiKit.InsetLabel(_launcherLabel.rectTransform, layout.LabelInset);
        }
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.ui.touch_layout"),
            ("screen", key),
            ("launcher", Describe(layout.Launcher))
        );
        if (_menu != null)
        {
            DestroyMenu();
            BuildMenu("relayout");
        }
    }

    private void Toggle()
    {
        if (_menu != null)
            Close("launcher");
        else
            Open();
    }

    private void Open()
    {
        if (_root == null || _font == null || _menu != null)
            return;
        BuildMenu("launcher");
        _menuOpened++;
    }

    private void BuildMenu(string reason)
    {
        var font = _font!;
        var entries = new List<(string Name, string Text, Action Open)>();
        if (_openHistory != null && _android.HistoryPanel.Value)
            entries.Add(("History", L.Resolve(AndroidTouchText.History), OpenHistory));
        if (_android.CollectionPanel.Value)
            entries.Add(("Collection", CollectionPanelText.Title(), OpenCollection));
        if (LiveBuildOffered())
            entries.Add(("LiveBuild", LiveBuildPanelText.Title(), OpenLiveBuild));

        var layout = TouchLauncherLayout.Compute(AndroidTouchScreen.Read(), entries.Count);
        var menu = AndroidTouchScreen.Child("Menu", _root!.transform, layout.Menu);
        AndroidUiKit.Fill(menu, AndroidUiKit.PanelColor);
        var origin = layout.Menu;
        var title = AndroidTouchScreen.Child("Title", menu, Relative(layout.Title, origin));
        AndroidUiKit.Text(
            title,
            font,
            L.Resolve(MenuTitle),
            layout.TitleFont,
            TextAlignmentOptions.MidlineLeft,
            AndroidUiKit.TextColor
        );
        var close = AndroidTouchScreen.Child("Close", menu, Relative(layout.Close, origin));
        AndroidUiKit.CloseButton(
            close,
            font,
            layout.TitleFont,
            layout.LabelInset,
            () => Close("close_button")
        );

        _entries.Clear();
        if (entries.Count == 0)
        {
            var hint = AndroidTouchScreen.Child("Hint", menu, Relative(layout.Entries[0], origin));
            AndroidUiKit.Text(
                hint,
                font,
                L.Resolve(NoPanels),
                layout.EntryFont,
                TextAlignmentOptions.MidlineLeft,
                AndroidUiKit.MutedTextColor
            );
        }
        for (var index = 0; index < entries.Count; index++)
        {
            var (name, text, open) = entries[index];
            var row = AndroidTouchScreen.Child(name, menu, Relative(layout.Entries[index], origin));
            AndroidUiKit.Button(row, font, text, layout.EntryFont, layout.LabelInset, open);
            _entries.Add(row.gameObject);
        }
        _menu = menu;
        SetActive(_dim, true);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.ui.panel_opened"),
            ("page", "menu"),
            ("reason", reason),
            ("entries", string.Join(",", entries.Select(entry => entry.Name))),
            ("menu", Describe(layout.Menu)),
            ("row_height", Math.Round(layout.Entries[0].Height)),
            ("min_target", Math.Round(AndroidTouchScreen.Read().MinTarget))
        );
    }

    private void Close(string reason)
    {
        if (_menu == null)
            return;
        DestroyMenu();
        _menuClosed++;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.ui.panel_closed"),
            ("reason", reason)
        );
    }

    private void DestroyMenu()
    {
        if (_menu != null)
            UnityEngine.Object.Destroy(_menu.gameObject);
        _menu = null;
        _entries.Clear();
        SetActive(_dim, false);
    }

    private void OpenHistory()
    {
        if (_openHistory == null)
            return;
        Close("history");
        _openHistory(TouchMenuSource);
    }

    private void OpenCollection()
    {
        Close("collection");
        LogOpenOutcome(CollectionPanelId, _collectionHandle.RequestOpen(TouchMenuSource));
    }

    private void OpenLiveBuild()
    {
        Close("live_build");
        LogOpenOutcome(LiveBuildPanelId, _liveBuildHandle.RequestOpen(TouchMenuSource));
    }

    // A tap the host turned down: the menu stays closed.
    private static void LogOpenOutcome(string panel, OverlayRequestOutcome outcome)
    {
        var reasonCode = outcome switch
        {
            OverlayRequestOutcome.SuppressedByCombat => "combat_active",
            OverlayRequestOutcome.Disabled => "switched_off",
            OverlayRequestOutcome.OpenFailed => "open_failed",
            OverlayRequestOutcome.Busy => "host_busy",
            OverlayRequestOutcome.UnknownPanel => "unknown_panel",
            _ => null,
        };
        if (reasonCode != null)
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.touch_menu.open_skipped"),
                ("panel", panel),
                ("reason_code", reasonCode)
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

    private static TouchRect Relative(TouchRect area, TouchRect origin) =>
        area with
        {
            Left = area.Left - origin.Left,
            Top = area.Top - origin.Top,
        };

    private static string Describe(TouchRect area) =>
        $"{area.Left:0},{area.Top:0},{area.Width:0}x{area.Height:0}";

    private static void SetActive(GameObject? target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }
}
