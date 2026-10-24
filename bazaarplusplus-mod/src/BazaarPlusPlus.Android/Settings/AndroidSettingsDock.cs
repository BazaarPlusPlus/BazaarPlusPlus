using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.Lobby;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Android.Settings;

/// <summary>
/// What a settings row does beyond writing its setting: close the panel a switch turned off,
/// release its rented cards, open History or the newer game APK. The host supplies these;
/// <see cref="AndroidSettingsDock"/> stays free of Unity, so a test registers the rows.
/// </summary>
internal sealed record AndroidSettingsDockHooks(
    Action<string> CloseCollection,
    Action<string> CloseLiveBuild,
    Action<string> CloseHistory,
    Action<string> ReleaseUpgradePreview,
    Action<string> ReleaseRentedCards,
    Func<bool> HistoryAvailable,
    Action OpenHistory,
    Func<AndroidGameUpdateStatus> GameUpdateStatus,
    Action OpenGameUpdate
);

/// <summary>
/// The Android host's rows of the BazaarPlusPlus settings section, registered the desktop's way
/// (Core's <see cref="SettingsDockEntryRegistry"/> and <see cref="BppSettingsDockCatalog"/>) and
/// drawn by <see cref="AndroidNativeSettingsSection"/> in the game's own settings dialog. The
/// shared rows are Core's: Enchant preview, with every mode the desktop offers, and Game History,
/// which here has no hotkey. The rest are the host's switches that stand in for the desktop's
/// hotkeys (Upgrade preview, the Collection, Ten-Win Builds and History panels, the touch button)
/// and, when the game is behind the published APK, the link to it.
/// </summary>
internal static class AndroidSettingsDock
{
    public static readonly LocalizedTextSet TouchMenuLabel = new(
        "BPP touch button",
        "BPP 触控按钮",
        "BPP 觸控按鈕"
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
            ItemEnchantPreviewSettingsDockEntry.Create(mode =>
                LogChanged("EnchantPreview", "Mode", mode)
            )
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
            Switch(
                BppSettingsDockOrder.CollectionPanelSwitch,
                "CollectionPanel",
                Ui.AndroidTouchText.CollectionPanel,
                android.CollectionPanel,
                enabled =>
                {
                    if (enabled)
                        return;
                    hooks.CloseCollection("switched_off");
                    hooks.ReleaseRentedCards("switched_off");
                }
            )
        );
        registry.Register(
            Switch(
                BppSettingsDockOrder.LiveBuildPanelSwitch,
                "LiveBuildPanel",
                Ui.AndroidTouchText.LiveBuildPanel,
                android.LiveBuildPanel,
                enabled =>
                {
                    if (enabled)
                        return;
                    hooks.CloseLiveBuild("switched_off");
                    hooks.ReleaseRentedCards("switched_off");
                }
            )
        );
        registry.Register(
            Switch(
                BppSettingsDockOrder.HistoryPanelSwitch,
                "HistoryPanel",
                Ui.AndroidTouchText.HistoryPanel,
                android.HistoryPanel,
                enabled =>
                {
                    if (enabled)
                        return;
                    hooks.CloseHistory("switched_off");
                    hooks.ReleaseRentedCards("switched_off");
                }
            )
        );
        registry.Register(
            Switch(
                BppSettingsDockOrder.TouchMenu,
                "TouchMenu",
                TouchMenuLabel,
                android.TouchMenu,
                _ => { }
            )
        );
        registry.Register(
            new HistoryPanelSettingsDockEntry(
                resolveHotkeyDisplay: null,
                () => android.HistoryPanel.Value && hooks.HistoryAvailable(),
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
            (_, enabled) => setting.Value = enabled,
            enabled =>
            {
                LogChanged("Android", key, enabled);
                onChanged(enabled);
            }
        );

    private static void LogChanged(string section, string key, object value) =>
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.settings.changed"),
            ("section", section),
            ("key", key),
            ("value", value)
        );

    // Offered only while the published APK is for a newer game; otherwise it says why not, dimmed:
    // the game is current, the check has not answered yet, or it failed. A failed check never
    // reads as current.
    private sealed class GameUpdateEntry(AndroidSettingsDockHooks hooks) : ISettingsDockEntry
    {
        public int Order => BppSettingsDockOrder.GameUpdate;

        public BppSettingsDockDefinition Build(BppSettings config) =>
            new(
                "GameUpdate",
                languageCode =>
                    Label(hooks.GameUpdateStatus()).Resolve(languageCode, L.CurrentMode),
                () => hooks.GameUpdateStatus() == AndroidGameUpdateStatus.Available,
                hooks.OpenGameUpdate,
                collapseAfterActivate: false
            );

        private static LocalizedTextSet Label(AndroidGameUpdateStatus status) =>
            status switch
            {
                AndroidGameUpdateStatus.Available => Ui.AndroidTouchText.GameUpdated,
                AndroidGameUpdateStatus.Current => GameUpToDate,
                AndroidGameUpdateStatus.CheckFailed => GameUpdateCheckFailed,
                _ => GameUpdateChecking,
            };
    }
}
