using BazaarPlusPlus.Android.Collection;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.LiveBuild;
using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
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
/// dialog (<see cref="Settings.AndroidNativeSettingsSection"/>), as on the desktop. History closes
/// this menu and opens <see cref="AndroidHistoryPanel"/>, and the button hides while History or
/// the settings dialog's BazaarPlusPlus section is open. Collection opens the full-screen
/// <see cref="AndroidCollectionPanel"/>, and Ten-Win Builds the full-screen
/// <see cref="AndroidLiveBuildPanel"/> when the record chain composed its build catalog. All of them close on every scene change, and the back key closes the top-most of
/// them first (<see cref="AndroidBackKey"/>). The geometry is Core's
/// <see cref="TouchLauncherLayout"/>, laid out again whenever the screen changes.
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

    private readonly AndroidSettings _android;
    private readonly Action? _openHistory;
    private readonly Func<bool> _historyVisible;
    private readonly BuildRecommendationRepository? _recommendations;
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
        Action? openHistory,
        Func<bool> historyVisible,
        BuildRecommendationRepository? recommendations
    )
    {
        _android = android ?? throw new ArgumentNullException(nameof(android));
        _openHistory = openHistory;
        _historyVisible = historyVisible ?? throw new ArgumentNullException(nameof(historyVisible));
        _recommendations = recommendations;
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
            .. AndroidBackKey.Summary(),
        ];

    /// <summary>Main thread: mounts the UI once the game's font is available.</summary>
    public void EnsureMounted()
    {
        if (_root != null || _mountFailed)
            return;
        var font = VersionLabelPatch.Font;
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

    /// <summary>A scene loaded: the menu and the panels never outlive the scene they opened in.</summary>
    public void OnSceneLoaded()
    {
        Close("scene_changed");
        _collection?.Close("scene_changed");
        _liveBuild?.Close("scene_changed");
    }

    /// <summary>The Collection switch went off, or another surface needs the panel closed.</summary>
    public void CloseCollection(string reason) => _collection?.Close(reason);

    /// <summary>The Ten-Win Builds switch went off, or another surface needs the panel closed.</summary>
    public void CloseLiveBuild(string reason) => _liveBuild?.Close(reason);

    private void Mount(TMP_FontAsset font)
    {
        _root = AndroidUiKit.CreateOverlayCanvas("BPP.TouchUi", SortingOrder);
        _root.AddComponent<AndroidTouchRoot>();
        AndroidTouchRoot.Frame = Frame;
        _font = font;
        var root = _root.transform;

        _launcher = AndroidTouchScreen.Child("Launcher", root, default);
        var (_, _, launcherLabel) = AndroidUiKit.Button(_launcher, font, "BPP", 30f, Toggle);
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

        // Top-most first: an open full-screen panel, then this menu.
        AndroidBackKey.Register("live_build", () => _liveBuild?.HandleBack() == true);
        AndroidBackKey.Register("collection", () => _collection?.HandleBack() == true);
        AndroidBackKey.Register(
            "touch_menu",
            () =>
            {
                if (_menu == null)
                    return false;
                Close("back_key");
                return true;
            }
        );
        LayoutLauncher(force: true);
    }

    private void Frame()
    {
        try
        {
            // The button stays out of History and of the game's settings dialog, which it would cover.
            SetActive(
                _launcher?.gameObject,
                _android.TouchMenu.Value
                    && !_historyVisible()
                    && !Settings.AndroidNativeSettingsSection.AnyVisible
            );
            if (!_android.TouchMenu.Value && _menu != null)
                Close("touch_menu_off");
            LayoutLauncher(force: false);
            _collection?.Tick();
            _liveBuild?.Tick();
            // The back key also reaches the game's escape hook (BackKeyPatches); a press both
            // see closes one panel.
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                AndroidBackKey.TryConsume("keyboard");
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
            _launcherLabel.fontSize = layout.LauncherFont;
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
        AndroidUiKit.CloseButton(close, font, layout.TitleFont, () => Close("close_button"));

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
            AndroidUiKit.Button(row, font, text, layout.EntryFont, open);
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
        if (_openHistory == null || !_android.HistoryPanel.Value)
            return;
        Close("history");
        _openHistory();
    }

    private void OpenCollection()
    {
        if (!_android.CollectionPanel.Value || _root == null || _font == null)
            return;
        Close("collection");
        try
        {
            _collection ??= new AndroidCollectionPanel(_root.transform, _font);
            _collection.Open();
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.collection.open_failed"),
                exception
            );
            _collection?.Close("open_failed");
        }
    }

    private bool LiveBuildOffered() => _android.LiveBuildPanel.Value && _recommendations != null;

    private void OpenLiveBuild()
    {
        if (!LiveBuildOffered() || _root == null || _font == null)
            return;
        Close("live_build");
        try
        {
            _liveBuild ??= new AndroidLiveBuildPanel(_root.transform, _font, _recommendations!);
            _liveBuild.Open("touch_menu");
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.live_build.open_failed"),
                exception
            );
            _liveBuild?.Close("open_failed");
        }
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
