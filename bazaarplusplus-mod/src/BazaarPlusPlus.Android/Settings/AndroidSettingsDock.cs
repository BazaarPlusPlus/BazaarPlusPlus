using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.Lobby;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.DetailedPreview;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Android.Settings;

/// <summary>
/// What a settings row does beyond writing its setting: release the upgrade preview a switch
/// turned off, open History or the new Android App's download. The host supplies these;
/// <see cref="AndroidSettingsDock"/> stays free of Unity, so a test registers the rows.
/// <see cref="CompatibilityMode"/> is the Launch Record's answer for this launch.
/// </summary>
internal sealed record AndroidSettingsDockHooks(
    Action<string> ReleaseUpgradePreview,
    Func<bool> HistoryAvailable,
    Action OpenHistory,
    Func<AndroidGameUpdateStatus> GameUpdateStatus,
    Action OpenGameUpdate,
    Action? RefreshPreviews = null,
    bool CompatibilityMode = false
);

/// <summary>
/// The Android host's rows of the BazaarPlusPlus settings section, registered the desktop's way
/// (Core's <see cref="SettingsDockEntryRegistry"/> and <see cref="BppSettingsDockCatalog"/>) and
/// drawn by <see cref="AndroidNativeSettingsSection"/> in the game's own settings dialog. The
/// shared rows are Core's: BazaarDB upload (<c>[BazaarDB] UploadScreenshots</c>, which on Android
/// gates only the account link, since the host takes no end-of-run screenshot), Combat Status,
/// Enchant preview, with every mode the desktop offers, and Game History, which here has no
/// hotkey. The rest are the host's Upgrade preview switch,
/// which stands in for the desktop's hotkey, the game-update row, which offers the new Android
/// App's download when the release needs it and otherwise says, dimmed, where the game stands,
/// and, in Compatibility Mode only, a dimmed row that says uploads are off.
/// </summary>
internal static class AndroidSettingsDock
{
    public static readonly LocalizedTextSet AppUpdated = new(
        "Install the new BazaarPlusPlus App",
        "请安装新版 BazaarPlusPlus App",
        "請安裝新版 BazaarPlusPlus App"
    );
    public static readonly LocalizedTextSet CompatibilityMode = new(
        "Compatibility mode: uploads are off",
        "兼容模式：上传已关闭",
        "相容模式：上傳已關閉"
    );
    public static readonly LocalizedTextSet GameUpToDate = new(
        "The game is up to date",
        "游戏已是最新版本",
        "遊戲已是最新版本"
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

    /// <summary>Registers every row and installs them as the catalog's definitions.</summary>
    public static void Install(
        BppSettings shared,
        AndroidSettings android,
        AndroidSettingsDockHooks hooks
    )
    {
        var registry = new SettingsDockEntryRegistry();
        Register(registry, android, hooks);
        BppSettingsDockCatalog.Install(shared, registry);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.settings.rows_registered"),
            ("rows", BppSettingsDockCatalog.Definitions.Count),
            ("keys", string.Join(",", BppSettingsDockCatalog.Definitions.Select(row => row.Key)))
        );
    }

    public static void Register(
        SettingsDockEntryRegistry registry,
        AndroidSettings android,
        AndroidSettingsDockHooks hooks
    )
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(android);
        ArgumentNullException.ThrowIfNull(hooks);

        registry.Register(
            DetailedPreviewSettingsDockEntry.Create(enabled =>
            {
                LogChanged("DetailedPreview", "Enabled", enabled);
                hooks.RefreshPreviews?.Invoke();
            })
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
        registry.Register(
            Switch(
                BppSettingsDockOrder.CombatStatusBar,
                "CombatStatusBar",
                new LocalizedTextSet("Combat timer", "战斗计时", "戰鬥計時"),
                android.CombatTimer,
                _ => { }
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
            Switch(
                BppSettingsDockOrder.UpgradePreviewSwitch,
                "UpgradePreview",
                AndroidTouchText.UpgradePreview,
                android.UpgradePreview,
                enabled =>
                {
                    if (!enabled)
                        hooks.ReleaseUpgradePreview("switched_off");
                }
            )
        );
        registry.Register(
            new HistoryPanelSettingsDockEntry(
                resolveHotkeyDisplay: null,
                hooks.HistoryAvailable,
                hooks.OpenHistory
            )
        );
        registry.Register(new GameUpdateEntry(hooks));
        if (hooks.CompatibilityMode)
            registry.Register(new CompatibilityModeEntry());
    }

    private static CyclingSettingsDockEntry<bool> Switch(
        int order,
        string key,
        LocalizedTextSet label,
        ISetting<bool> setting,
        Action<bool> onChanged
    ) =>
        CyclingSettingsDockEntry<bool>.Toggle(
            order,
            key,
            languageCode => label.Resolve(languageCode, L.CurrentMode),
            _ => setting.Value,
            (_, enabled) => WriteSwitch(key, setting, enabled),
            onChanged
        );

    private static void WriteSwitch(string key, ISetting<bool> setting, bool enabled)
    {
        setting.Value = enabled;
        LogChanged("Android", key, enabled);
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
    // BPP to download), current, the check has not answered yet, or it failed. A failed check
    // never reads as current.
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
                AndroidGameUpdateStatus.CheckFailed => GameUpdateCheckFailed,
                _ => GameUpdateChecking,
            };
    }

    // Information only: Compatibility Mode is the Android App's choice for this launch.
    private sealed class CompatibilityModeEntry : ISettingsDockEntry
    {
        public int Order => BppSettingsDockOrder.CompatibilityMode;

        public BppSettingsDockDefinition Build(BppSettings config) =>
            new(
                "CompatibilityMode",
                languageCode => CompatibilityMode.Resolve(languageCode, L.CurrentMode),
                static () => false,
                static () => { },
                collapseAfterActivate: false
            );
    }
}
