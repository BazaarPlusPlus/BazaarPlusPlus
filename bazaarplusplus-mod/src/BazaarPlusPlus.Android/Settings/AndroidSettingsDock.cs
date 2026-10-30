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
/// turned off, open History or the newer game APK. The host supplies these;
/// <see cref="AndroidSettingsDock"/> stays free of Unity, so a test registers the rows.
/// </summary>
internal sealed record AndroidSettingsDockHooks(
    Action<string> ReleaseUpgradePreview,
    Func<bool> HistoryAvailable,
    Action OpenHistory,
    Func<AndroidGameUpdateStatus> GameUpdateStatus,
    Action OpenGameUpdate,
    Action? RefreshPreviews = null
);

/// <summary>
/// The Android host's rows of the BazaarPlusPlus settings section, registered the desktop's way
/// (Core's <see cref="SettingsDockEntryRegistry"/> and <see cref="BppSettingsDockCatalog"/>) and
/// drawn by <see cref="AndroidNativeSettingsSection"/> in the game's own settings dialog. The
/// shared rows are Core's: BazaarDB upload (<c>[BazaarDB] UploadScreenshots</c>, which on Android
/// gates only the account link, since the host takes no end-of-run screenshot), Combat Status,
/// Enchant preview, with every mode the desktop offers, and Game History, which here has no
/// hotkey. The rest are the host's Upgrade preview switch,
/// which stands in for the desktop's hotkey, and, when the game is behind the published APK, the
/// link to it.
/// </summary>
internal static class AndroidSettingsDock
{
    public static readonly LocalizedTextSet RuntimeUpdated = new(
        "BazaarPlusPlus needs a new APK: download it",
        "BazaarPlusPlus 需要新版安装包，请下载",
        "BazaarPlusPlus 需要新版安裝包，請下載"
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
                Ui.AndroidTouchText.UpgradePreview,
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

    // Offered only while this copy needs the published APK (a newer game, or a BPP runtime it
    // cannot take by itself); otherwise it says why not, dimmed: the APK is current, the check has
    // not answered yet, or it failed. A failed check never reads as current.
    private sealed class GameUpdateEntry(AndroidSettingsDockHooks hooks) : ISettingsDockEntry
    {
        public int Order => BppSettingsDockOrder.GameUpdate;

        public BppSettingsDockDefinition Build(BppSettings config) =>
            new(
                "GameUpdate",
                languageCode =>
                    Label(hooks.GameUpdateStatus()).Resolve(languageCode, L.CurrentMode),
                () =>
                    hooks.GameUpdateStatus()
                        is AndroidGameUpdateStatus.GameUpdated
                            or AndroidGameUpdateStatus.RuntimeChanged,
                hooks.OpenGameUpdate,
                collapseAfterActivate: false
            );

        private static LocalizedTextSet Label(AndroidGameUpdateStatus status) =>
            status switch
            {
                AndroidGameUpdateStatus.GameUpdated => Ui.AndroidTouchText.GameUpdated,
                AndroidGameUpdateStatus.RuntimeChanged => RuntimeUpdated,
                AndroidGameUpdateStatus.Current => GameUpToDate,
                AndroidGameUpdateStatus.CheckFailed => GameUpdateCheckFailed,
                _ => GameUpdateChecking,
            };
    }
}
