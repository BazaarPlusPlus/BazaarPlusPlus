using System.Globalization;
using System.Text;
using BazaarPlusPlus.Android.Collection;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Android.History;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.Lobby;
using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using Il2CppTMPro;
using UnityEngine;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The touch entry that replaces the desktop's hotkeys and settings dock: a small BPP button at
/// the left edge of the screen opens one panel with two pages and the Collection entry. Settings
/// holds the switches of the features the Android host has (upgrade preview, enchant preview,
/// Collection) and, when the game is behind the published APK, the link to it. History lists the
/// Runs the record chain wrote and the battles of the selected one. Collection opens the full-screen
/// <see cref="AndroidCollectionPanel"/> in place of the menu. Both close on every scene change.
/// </summary>
internal sealed class AndroidTouchUi
{
    private const int SortingOrder = 30000;
    private const int RunsPerPage = 7;
    private const int BattleLines = 16;

    private readonly BppSettings _settings;
    private readonly AndroidSettings _android;
    private readonly AndroidVersionCheck? _versionCheck;
    private readonly AndroidHistoryReader? _history;
    private readonly List<(TextMeshProUGUI Label, Func<string> Text)> _settingRows = new();
    private readonly List<RunRow> _runRows = new();

    private GameObject? _root;
    private GameObject? _launcher;
    private GameObject? _dim;
    private GameObject? _panel;
    private GameObject? _settingsPage;
    private GameObject? _historyPage;
    private GameObject? _gameUpdateRow;
    private GameObject? _collectionTab;
    private TMP_FontAsset? _font;
    private AndroidCollectionPanel? _collection;
    private TextMeshProUGUI? _launcherLabel;
    private TextMeshProUGUI? _titleText;
    private TextMeshProUGUI? _pageText;
    private TextMeshProUGUI? _battlesText;
    private Page _page = Page.Settings;
    private Task<AndroidHistoryPage>? _runsRead;
    private Task<IReadOnlyList<AndroidHistoryBattle>>? _battlesRead;
    private AndroidHistoryPage? _runs;
    private int _offset;
    private string? _selectedRunId;
    private bool _mountFailed;

    public AndroidTouchUi(
        BppSettings settings,
        AndroidSettings android,
        AndroidVersionCheck? versionCheck,
        AndroidHistoryReader? history
    )
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _android = android ?? throw new ArgumentNullException(nameof(android));
        _versionCheck = versionCheck;
        _history = history;
    }

    private enum Page
    {
        Settings,
        History,
    }

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

    /// <summary>A scene loaded: the panel never outlives the scene it was opened in.</summary>
    public void OnSceneLoaded()
    {
        Close("scene_changed");
        _collection?.Close("scene_changed");
    }

    private void Mount(TMP_FontAsset font)
    {
        _root = AndroidUiKit.CreateOverlayCanvas("BPP.TouchUi", SortingOrder);
        _root.AddComponent<AndroidTouchRoot>();
        AndroidTouchRoot.Frame = Frame;
        _font = font;
        var root = _root.transform;

        var launcher = AndroidUiKit.Fixed(
            "Launcher",
            root,
            new(0f, 0.5f),
            new(8f, 0f),
            new(120f, 60f)
        );
        var (_, _, launcherLabel) = AndroidUiKit.Button(launcher, font, "BPP", 30f, () => Toggle());
        _launcher = launcher.gameObject;
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

        var panel = AndroidUiKit.Fixed(
            "Panel",
            root,
            new(0.5f, 0.5f),
            Vector2.zero,
            new(1560f, 900f)
        );
        AndroidUiKit.Fill(panel, AndroidUiKit.PanelColor);
        _panel = panel.gameObject;

        var title = AndroidUiKit.Rect(
            "Title",
            panel,
            new(0f, 1f),
            new(1f, 1f),
            new(32f, -84f),
            new(-140f, -16f)
        );
        _titleText = AndroidUiKit.Text(
            title,
            font,
            "",
            34f,
            TextAlignmentOptions.MidlineLeft,
            AndroidUiKit.TextColor
        );
        var close = AndroidUiKit.Fixed(
            "Close",
            panel,
            new(1f, 1f),
            new(-16f, -16f),
            new(100f, 68f)
        );
        AndroidUiKit.Button(close, font, "X", 34f, () => Close("close_button"));

        var settingsTab = AndroidUiKit.Fixed(
            "SettingsTab",
            panel,
            new(0f, 1f),
            new(24f, -110f),
            new(280f, 88f)
        );
        AndroidUiKit.Button(
            settingsTab,
            font,
            L.Resolve(AndroidTouchText.Settings),
            30f,
            () => Show(Page.Settings)
        );
        var historyTab = AndroidUiKit.Fixed(
            "HistoryTab",
            panel,
            new(0f, 1f),
            new(24f, -214f),
            new(280f, 88f)
        );
        AndroidUiKit.Button(
            historyTab,
            font,
            L.Resolve(AndroidTouchText.History),
            30f,
            () => Show(Page.History)
        );
        var collectionTab = AndroidUiKit.Fixed(
            "CollectionTab",
            panel,
            new(0f, 1f),
            new(24f, -318f),
            new(280f, 88f)
        );
        AndroidUiKit.Button(collectionTab, font, CollectionPanelText.Title(), 30f, OpenCollection);
        _collectionTab = collectionTab.gameObject;

        var content = AndroidUiKit.Rect(
            "Content",
            panel,
            Vector2.zero,
            Vector2.one,
            new(336f, 24f),
            new(-24f, -110f)
        );
        MountSettings(content, font);
        MountHistory(content, font);

        Close("mounted");
    }

    private void MountSettings(RectTransform content, TMP_FontAsset font)
    {
        var page = AndroidUiKit.Rect(
            "SettingsPage",
            content,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        _settingsPage = page.gameObject;
        AddSettingRow(
            page,
            font,
            0,
            () => SwitchText(AndroidTouchText.UpgradePreview, _android.UpgradePreview.Value),
            ToggleUpgradePreview
        );
        AddSettingRow(
            page,
            font,
            1,
            () => SwitchText(AndroidTouchText.EnchantPreview, EnchantPreviewOn()),
            ToggleEnchantPreview
        );
        AddSettingRow(
            page,
            font,
            2,
            () => SwitchText(AndroidTouchText.CollectionPanel, _android.CollectionPanel.Value),
            ToggleCollectionPanel
        );

        var update = AndroidUiKit.Fixed(
            "GameUpdate",
            page,
            new(0f, 1f),
            new(0f, -3f * 104f - 24f),
            new(1100f, 96f)
        );
        var (_, background, _) = AndroidUiKit.Button(
            update,
            font,
            L.Resolve(AndroidTouchText.GameUpdated),
            28f,
            OpenGameUpdate
        );
        background.color = AndroidUiKit.ActiveColor;
        _gameUpdateRow = update.gameObject;
    }

    private void AddSettingRow(
        RectTransform page,
        TMP_FontAsset font,
        int index,
        Func<string> text,
        Action onTap
    )
    {
        var rect = AndroidUiKit.Fixed(
            $"Row{index}",
            page,
            new(0f, 1f),
            new(0f, -index * 104f),
            new(760f, 88f)
        );
        var (_, _, label) = AndroidUiKit.Button(
            rect,
            font,
            text(),
            30f,
            () =>
            {
                onTap();
                RefreshSettings();
            }
        );
        _settingRows.Add((label, text));
    }

    private void MountHistory(RectTransform content, TMP_FontAsset font)
    {
        var page = AndroidUiKit.Rect(
            "HistoryPage",
            content,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        _historyPage = page.gameObject;
        for (var index = 0; index < RunsPerPage; index++)
        {
            var slot = index;
            var rect = AndroidUiKit.Fixed(
                $"Run{index}",
                page,
                new(0f, 1f),
                new(0f, -index * 96f),
                new(560f, 88f)
            );
            var (_, background, label) = AndroidUiKit.Button(
                rect,
                font,
                "",
                22f,
                () => SelectRun(slot)
            );
            label.alignment = TextAlignmentOptions.MidlineLeft;
            _runRows.Add(new RunRow(rect.gameObject, background, label));
        }

        var previous = AndroidUiKit.Fixed(
            "Previous",
            page,
            new(0f, 0f),
            new(0f, 0f),
            new(110f, 72f)
        );
        AndroidUiKit.Button(previous, font, "<", 34f, () => Turn(-1));
        var next = AndroidUiKit.Fixed("Next", page, new(0f, 0f), new(450f, 0f), new(110f, 72f));
        AndroidUiKit.Button(next, font, ">", 34f, () => Turn(1));
        var pageLabel = AndroidUiKit.Fixed(
            "PageLabel",
            page,
            new(0f, 0f),
            new(120f, 0f),
            new(320f, 72f)
        );
        _pageText = AndroidUiKit.Text(
            pageLabel,
            font,
            "",
            24f,
            TextAlignmentOptions.Center,
            AndroidUiKit.MutedTextColor
        );

        var battles = AndroidUiKit.Rect(
            "Battles",
            page,
            Vector2.zero,
            Vector2.one,
            new(600f, 0f),
            Vector2.zero
        );
        _battlesText = AndroidUiKit.Text(
            battles,
            font,
            "",
            24f,
            TextAlignmentOptions.TopLeft,
            AndroidUiKit.TextColor
        );
    }

    private void Frame()
    {
        try
        {
            var visible = _android.TouchMenu.Value;
            SetActive(_launcher, visible);
            if (!visible && _panel != null && _panel.activeSelf)
                Close("touch_menu_off");
            if (_launcherLabel != null)
                _launcherLabel.text = _versionCheck?.GameUpdate != null ? "BPP !" : "BPP";
            ObserveRuns();
            ObserveBattles();
            _collection?.Tick();
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("touch_ui", exception);
        }
    }

    private void Toggle()
    {
        if (_panel != null && _panel.activeSelf)
            Close("launcher");
        else
            Open();
    }

    private void Open()
    {
        if (_panel == null)
            return;
        SetActive(_dim, true);
        SetActive(_panel, true);
        SetActive(_collectionTab, _android.CollectionPanel.Value);
        Show(_page);
    }

    private void Close(string reason)
    {
        if (_panel == null || !_panel.activeSelf)
            return;
        SetActive(_panel, false);
        SetActive(_dim, false);
        if (reason != "mounted")
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.ui.panel_closed"),
                ("reason", reason)
            );
    }

    private void Show(Page page)
    {
        _page = page;
        SetActive(_settingsPage, page == Page.Settings);
        SetActive(_historyPage, page == Page.History);
        if (_titleText != null)
            _titleText.text = $"BazaarPlusPlus {BppPluginVersion.Current}";
        if (page == Page.Settings)
            RefreshSettings();
        else
            LoadRuns(_offset);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.ui.panel_opened"),
            ("page", page)
        );
    }

    private void RefreshSettings()
    {
        foreach (var (label, text) in _settingRows)
            label.text = text();
        SetActive(_gameUpdateRow, _versionCheck?.GameUpdate != null);
        SetActive(_collectionTab, _android.CollectionPanel.Value);
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

    private void ToggleCollectionPanel()
    {
        _android.CollectionPanel.Value = !_android.CollectionPanel.Value;
        if (!_android.CollectionPanel.Value)
        {
            _collection?.Close("switched_off");
            AndroidCardRental.ReleaseAll("switched_off");
        }
        LogSettingChanged("Android", "CollectionPanel", _android.CollectionPanel.Value);
    }

    private void ToggleUpgradePreview()
    {
        _android.UpgradePreview.Value = !_android.UpgradePreview.Value;
        if (!_android.UpgradePreview.Value)
            AndroidUpgradePreview.Release("switched_off");
        LogSettingChanged("Android", "UpgradePreview", _android.UpgradePreview.Value);
    }

    // Android has no pedestal probe, so it offers Off and Always and treats
    // AutoOnPedestalChoice, which only a desktop file can hold, as Off.
    private bool EnchantPreviewOn() =>
        _settings.EnchantPreviewModeConfig.Value == PreviewVisibilityMode.Always;

    private void ToggleEnchantPreview()
    {
        _settings.EnchantPreviewModeConfig.Value = EnchantPreviewOn()
            ? PreviewVisibilityMode.Off
            : PreviewVisibilityMode.Always;
        LogSettingChanged("EnchantPreview", "Mode", _settings.EnchantPreviewModeConfig.Value);
    }

    private void OpenGameUpdate()
    {
        var apk = _versionCheck?.GameUpdate;
        if (apk == null)
            return;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.ui.game_update_opened"),
            ("game_version", apk.GameVersion)
        );
        Application.OpenURL(apk.Url.ToString());
    }

    private void LoadRuns(int offset)
    {
        if (_history == null)
        {
            ShowHistoryMessage(L.Resolve(AndroidTouchText.ReadFailed));
            return;
        }
        if (_runsRead != null)
            return;
        _offset = Math.Max(0, offset);
        var history = _history;
        var requested = _offset;
        _runsRead = Task.Run(() => history.ReadRuns(requested, RunsPerPage));
        if (_runs == null)
            ShowHistoryMessage(L.Resolve(AndroidTouchText.Loading));
    }

    private void ObserveRuns()
    {
        var read = _runsRead;
        if (read is not { IsCompleted: true })
            return;
        _runsRead = null;
        if (read.Exception is { } exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Android,
                    "android.history.read_failed",
                    storm: ["reason_code"]
                ),
                exception,
                ("reason_code", "runs")
            );
            ShowHistoryMessage(L.Resolve(AndroidTouchText.ReadFailed));
            return;
        }
        _runs = read.Result;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.loaded"),
            ("offset", _runs.Offset),
            ("runs", _runs.Runs.Count),
            ("total", _runs.Total)
        );
        RenderRuns();
        if (_selectedRunId == null && _runs.Runs.Count > 0)
            SelectRun(0);
        else if (_runs.Runs.Count == 0)
            ShowHistoryMessage(L.Resolve(AndroidTouchText.NoRuns));
    }

    private void RenderRuns()
    {
        var runs = _runs?.Runs ?? [];
        for (var index = 0; index < _runRows.Count; index++)
        {
            var row = _runRows[index];
            SetActive(row.Root, index < runs.Count);
            if (index >= runs.Count)
                continue;
            var run = runs[index];
            var fields = HistoryPanelFormatter.RunRowFields(run, run.Hero);
            var record = $"{run.Victories ?? 0}-{run.Losses ?? 0}";
            row.Label.text =
                $"<b>{fields.Name}</b>  {record}\n<size=80%><color=#B8A88E>{fields.Meta} · {fields.Stamp}</color></size>";
            row.Background.color =
                run.RunId == _selectedRunId ? AndroidUiKit.ActiveColor : AndroidUiKit.ButtonColor;
        }
        if (_pageText != null && _runs != null)
            _pageText.text =
                _runs.Total == 0
                    ? ""
                    : $"{_runs.Offset + 1}-{_runs.Offset + runs.Count} / {_runs.Total}";
    }

    private void Turn(int direction)
    {
        if (_runs == null)
            return;
        var offset = _runs.Offset + direction * RunsPerPage;
        if (offset < 0 || offset >= _runs.Total)
            return;
        _selectedRunId = null;
        LoadRuns(offset);
    }

    private void SelectRun(int slot)
    {
        var runs = _runs?.Runs;
        if (runs == null || slot >= runs.Count || _history == null || _battlesRead != null)
            return;
        var run = runs[slot];
        _selectedRunId = run.RunId;
        RenderRuns();
        var history = _history;
        _battlesRead = Task.Run(() => history.ReadBattles(run.RunId));
    }

    private void ObserveBattles()
    {
        var read = _battlesRead;
        if (read is not { IsCompleted: true })
            return;
        _battlesRead = null;
        if (read.Exception is { } exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Android,
                    "android.history.read_failed",
                    storm: ["reason_code"]
                ),
                exception,
                ("reason_code", "battles")
            );
            ShowHistoryMessage(L.Resolve(AndroidTouchText.ReadFailed));
            return;
        }
        var battles = read.Result;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.run_selected"),
            ("battles", battles.Count)
        );
        if (_battlesText != null)
            _battlesText.text = FormatBattles(battles);
    }

    private static string FormatBattles(IReadOnlyList<AndroidHistoryBattle> battles)
    {
        if (battles.Count == 0)
            return L.Resolve(AndroidTouchText.NoBattles);
        var text = new StringBuilder();
        foreach (var battle in battles.Take(BattleLines))
        {
            var outcome = battle.Result?.ToLowerInvariant() switch
            {
                "win" => $"<color=#9ECC8A>{L.Resolve(AndroidTouchText.Win)}</color>",
                "loss" => $"<color=#E5664F>{L.Resolve(AndroidTouchText.Loss)}</color>",
                _ => HistoryPanelText.Unknown(),
            };
            var opponent = battle.OpponentName ?? HistoryPanelText.UnknownOpponent();
            var hero = battle.OpponentHero == null ? "" : $" ({battle.OpponentHero})";
            var final = battle.IsFinal ? $" · {HistoryPanelText.FinalBattle()}" : "";
            var standing = string.Join(
                " ",
                new[]
                {
                    battle.OpponentRank,
                    battle.OpponentRating?.ToString(CultureInfo.CurrentCulture),
                }.Where(part => !string.IsNullOrEmpty(part))
            );
            var rank = standing.Length == 0 ? "" : $" · {standing}";
            text.Append(HistoryPanelText.DayBadge(battle.Day))
                .Append("  ")
                .Append(outcome)
                .Append("  ")
                .Append(opponent)
                .Append(hero)
                .Append("<color=#B8A88E>")
                .Append(rank)
                .Append(final)
                .Append("</color>\n");
        }
        if (battles.Count > BattleLines)
            text.Append("+")
                .Append(battles.Count - BattleLines)
                .Append(" ")
                .Append(L.Resolve(AndroidTouchText.More));
        return text.ToString();
    }

    private void ShowHistoryMessage(string message)
    {
        if (_battlesText != null)
            _battlesText.text = message;
    }

    private static string SwitchText(LocalizedTextSet name, bool on) =>
        $"{L.Resolve(name)}: {L.Resolve(on ? AndroidTouchText.On : AndroidTouchText.Off)}";

    private static void LogSettingChanged(string section, string key, object value) =>
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.settings.changed"),
            ("section", section),
            ("key", key),
            ("value", value)
        );

    private static void SetActive(GameObject? target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    private sealed record RunRow(
        GameObject Root,
        UnityEngine.UI.Image Background,
        TextMeshProUGUI Label
    );
}
