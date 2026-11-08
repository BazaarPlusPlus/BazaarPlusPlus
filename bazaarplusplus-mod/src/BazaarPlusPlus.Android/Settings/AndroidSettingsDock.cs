using BazaarPlusPlus.Android.Lobby;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.DetailedPreview;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.Mobile.Previews;

namespace BazaarPlusPlus.Android.Settings;

/// <summary>
/// What a settings row does beyond writing its setting: release the upgrade preview a switch
/// turned off, open History or the new Android App's download. The host supplies these;
/// <see cref="AndroidSettingsDock"/> stays free of Unity, so a test registers the rows.
/// </summary>
internal sealed record AndroidSettingsDockHooks(
    Action<string> ReleaseUpgradePreview,
    Func<bool> HistoryAvailable,
    Action OpenHistory,
    Func<AndroidGameUpdateStatus> GameUpdateStatus,
    Action OpenGameUpdate,
    Action? RefreshPreviews = null,
    Action? ChineseLocaleModeChanged = null
);

/// <summary>
/// The settings behind the Android host's own rows: the Mobile UI's <c>[Touch]</c> upgrade switch
/// and preview text size, and the host's <c>[Graphics] FrameRatePriority</c>.
/// </summary>
internal sealed record AndroidSettingsDockSettings(
    ISetting<bool> UpgradePreview,
    ISetting<int> PreviewTextSize,
    ISetting<bool> FrameRatePriority
);

/// <summary>
/// The Android host's rows of the BazaarPlusPlus settings section, registered the desktop's way
/// (Core's <see cref="SettingsDockEntryRegistry"/> and <see cref="BppSettingsDockCatalog"/>) and
/// drawn by <see cref="AndroidNativeSettingsSection"/> in the game's own settings dialog. The
/// shared rows are Core's: BazaarDB upload (<c>[BazaarDB] UploadScreenshots</c>, which on Android
/// gates only the account link, since the host takes no end-of-run screenshot), Combat Status,
/// Enchant preview, with every mode the desktop offers, Chinese Locale, and Game History, which
/// here has no hotkey. The Mobile UI's Upgrade preview switch stands in for the desktop's hotkey,
/// and its Preview text size sets the Detailed Preview's size inside the native tooltip. The
/// host's own are the game-update row, which offers the new Android App's download when the
/// release needs it and otherwise says, dimmed, where the game stands, and Frame rate priority
/// (<see cref="Game.AndroidFrameRatePriority"/>).
/// </summary>
internal static class AndroidSettingsDock
{
    public static readonly LocalizedTextSet AppUpdated = new(
        "Install the new BazaarPlusPlus App",
        "请安装新版 BazaarPlusPlus App",
        "請安裝新版 BazaarPlusPlus App"
    );
    public static readonly LocalizedTextSet GameUpToDate = new(
        "The game is up to date",
        "游戏已是最新版本",
        "遊戲已是最新版本"
    );
    public static readonly LocalizedTextSet GameNotAdapted = new(
        "BazaarPlusPlus is not adapted to this game version",
        "BazaarPlusPlus 未适配此游戏版本",
        "BazaarPlusPlus 未適配此遊戲版本"
    );
    public static readonly LocalizedTextSet GameUpdateChecking = new(
        "Checking for a game update",
        "正在检查游戏更新",
        "正在檢查遊戲更新"
    );
    public static readonly LocalizedTextSet GameUpdateCheckFailed = new(
        "Could not check for a game update",
        "无法检查游戏更新",
        "無法檢查遊戲更新"
    );

    public static readonly LocalizedTextSet FrameRatePriorityLabel = new(
        "Frame rate priority",
        "帧率优先",
        "幀率優先"
    );

    /// <summary>Registers every row and installs them as the catalog's definitions.</summary>
    public static void Install(
        BppSettings shared,
        AndroidSettingsDockSettings settings,
        AndroidSettingsDockHooks hooks
    )
    {
        var registry = new SettingsDockEntryRegistry();
        Register(registry, settings, hooks);
        BppSettingsDockCatalog.Install(shared, registry);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.settings.rows_registered"),
            ("rows", BppSettingsDockCatalog.Definitions.Count),
            ("keys", string.Join(",", BppSettingsDockCatalog.Definitions.Select(row => row.Key)))
        );
    }

    public static void Register(
        SettingsDockEntryRegistry registry,
        AndroidSettingsDockSettings settings,
        AndroidSettingsDockHooks hooks
    )
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(hooks);

        registry.Register(
            DetailedPreviewSettingsDockEntry.Create(enabled =>
            {
                LogChanged("DetailedPreview", "Enabled", enabled);
                hooks.RefreshPreviews?.Invoke();
            })
        );
        registry.Register(
            TouchPreviewTextSize.DockEntry(
                settings.PreviewTextSize,
                percent =>
                {
                    LogChanged("Touch", "PreviewTextSize", percent);
                    hooks.RefreshPreviews?.Invoke();
                }
            )
        );
        registry.Register(
            CyclingSettingsDockEntry<bool>.Toggle(
                BppSettingsDockOrder.BazaarDbUpload,
                "BazaarDbUpload",
                BazaarDbBundleSettingsMenuLabel.Resolve,
                config => config.BazaarDbUploadEnabled.Value,
                (config, enabled) => config.BazaarDbUploadEnabled.Value = enabled,
                enabled => LogChanged("BazaarDB", "UploadScreenshots", enabled)
            )
        );
        // The Mobile UI's combat clock resumes a paused fight itself once its setting is off.
        registry.Register(
            CombatStatusBarSettingsDockEntry.Create(enabled =>
                LogChanged("CombatStatusBar", "Enabled", enabled)
            )
        );
        registry.Register(
            ItemEnchantPreviewSettingsDockEntry.Create(mode =>
            {
                LogChanged("EnchantPreview", "Mode", mode);
                hooks.RefreshPreviews?.Invoke();
            })
        );
        registry.Register(
            TouchUpgradePreviewSetting.DockEntry(
                settings.UpgradePreview,
                enabled =>
                {
                    LogChanged("Touch", "UpgradePreview", enabled);
                    if (!enabled)
                        hooks.ReleaseUpgradePreview("switched_off");
                }
            )
        );
        registry.Register(
            ChineseLocaleModeSettingsDockEntry.Create(() =>
            {
                LogChanged("BazaarPlusPlus", "ChineseLocaleMode", L.CurrentMode);
                hooks.ChineseLocaleModeChanged?.Invoke();
            })
        );
        registry.Register(
            new HistoryPanelSettingsDockEntry(
                resolveHotkeyDisplay: null,
                hooks.HistoryAvailable,
                hooks.OpenHistory
            )
        );
        registry.Register(new GameUpdateEntry(hooks));
        // Read once a second by Game.AndroidFrameRatePriority, which applies a change itself.
        registry.Register(
            CyclingSettingsDockEntry<bool>.Toggle(
                BppSettingsDockOrder.FrameRatePriority,
                "FrameRatePriority",
                languageCode => FrameRatePriorityLabel.Resolve(languageCode, L.CurrentMode),
                _ => settings.FrameRatePriority.Value,
                (_, enabled) => settings.FrameRatePriority.Value = enabled,
                enabled => LogChanged("Graphics", "FrameRatePriority", enabled)
            )
        );
    }

    private static void LogChanged(string section, string key, object value) =>
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.settings.changed"),
            ("section", section),
            ("key", key),
            ("value", value)
        );

    // Offered only while the release needs a new Android App, which the row downloads; otherwise
    // it says where the game stands, dimmed: a newer game to update from its store (nothing for
    // BPP to download), current, current but not adapted, the check has not answered yet, or it
    // failed. A failed check never reads as current.
    private sealed class GameUpdateEntry(AndroidSettingsDockHooks hooks) : ISettingsDockEntry
    {
        public int Order => BppSettingsDockOrder.GameUpdate;

        public BppSettingsDockDefinition Build(BppSettings config) =>
            new(
                "GameUpdate",
                languageCode =>
                    Label(hooks.GameUpdateStatus()).Resolve(languageCode, L.CurrentMode),
                () => hooks.GameUpdateStatus() == AndroidGameUpdateStatus.AppUpdated,
                hooks.OpenGameUpdate,
                collapseAfterActivate: false
            );

        private static LocalizedTextSet Label(AndroidGameUpdateStatus status) =>
            status switch
            {
                AndroidGameUpdateStatus.GameUpdated => AndroidTouchText.GameUpdated,
                AndroidGameUpdateStatus.AppUpdated => AppUpdated,
                AndroidGameUpdateStatus.Current => GameUpToDate,
                AndroidGameUpdateStatus.NotAdapted => GameNotAdapted,
                AndroidGameUpdateStatus.CheckFailed => GameUpdateCheckFailed,
                _ => GameUpdateChecking,
            };
    }
}
