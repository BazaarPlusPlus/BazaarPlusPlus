#nullable enable
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.Lobby;
using BazaarPlusPlus.Android.Settings;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The rows the Android host adds to the game's settings dialog, registered through Core's
/// registry as the desktop's are: which rows, in which order, and what each does beyond writing
/// its setting.
/// </summary>
public sealed class AndroidSettingsDockTests
{
    // The catalog's order, BppSettingsDockOrder's; the dialog groups the rows by control.
    [Fact]
    public void Rows_follow_the_shared_dock_order()
    {
        var dock = new Dock();

        Assert.Equal(
            new[]
            {
                "DetailedPreview",
                "BazaarDbUpload",
                "CombatStatusBar",
                "GameHistory",
                "UpgradePreview",
                "GameUpdate",
            },
            dock.Rows.Select(row => row.Key)
        );
        Assert.Equal(
            new[]
            {
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Action,
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Action,
            },
            dock.Rows.Select(row => row.ControlKind)
        );
    }

    // The account link waits on [BazaarDB] UploadScreenshots; the row is its touch switch, off by
    // default, labelled as the desktop's row.
    [Fact]
    public void Bazaardb_upload_writes_the_shared_setting_and_starts_off()
    {
        var dock = new Dock();
        var row = dock.Row("BazaarDbUpload");

        Assert.Equal("BazaarDB upload", row.ResolveLabel("en"));
        Assert.False(row.ReadToggle!());

        row.WriteToggle!(true);
        Assert.True(dock.Shared.BazaarDbUploadEnabled.Value);
        Assert.True(row.ReadToggle());
        row.WriteToggle(false);
        Assert.False(dock.Shared.BazaarDbUploadEnabled.Value);
        Assert.Empty(dock.Calls);
    }

    [Fact]
    public void Detailed_preview_defaults_on_and_controls_both_features_with_one_row()
    {
        var dock = new Dock();
        var row = dock.Row("DetailedPreview");
        Assert.Equal("Detailed Preview", row.ResolveLabel("en"));
        Assert.True(row.ReadToggle!());
        row.WriteToggle!(false);
        Assert.False(dock.Shared.EnableDetailedPreviewConfig.Value);
        row.WriteToggle(true);
        Assert.True(dock.Shared.EnableDetailedPreviewConfig.Value);
    }

    [Fact]
    public void Combat_timer_is_an_independent_default_on_touch_switch()
    {
        var dock = new Dock();
        var row = dock.Row("CombatStatusBar");
        Assert.Equal("Combat timer", row.ResolveLabel("en"));
        Assert.True(row.ReadToggle!());
        row.WriteToggle!(true);
        Assert.True(dock.Android.CombatTimer.Value);
        row.WriteToggle(false);
        Assert.False(dock.Android.CombatTimer.Value);
        Assert.True(dock.Shared.EnableCombatStatusBarConfig.Value);
    }

    [Fact]
    public void Switching_upgrade_preview_off_releases_its_tooltip()
    {
        var dock = new Dock();
        var row = dock.Row("UpgradePreview");
        Assert.False(dock.Android.UpgradePreview.Value);
        Assert.False(row.ReadToggle!());

        row.WriteToggle!(true);
        Assert.True(dock.Android.UpgradePreview.Value);
        Assert.Empty(dock.Calls);

        row.WriteToggle(false);
        Assert.False(dock.Android.UpgradePreview.Value);
        Assert.Equal(new[] { "release_upgrade_preview:switched_off" }, dock.Calls);
    }

    [Fact]
    public void Game_history_has_no_hotkey_and_follows_history_availability()
    {
        var dock = new Dock { HistoryAvailable = true };
        var row = dock.Row("GameHistory");

        Assert.Equal("Game History", row.ResolveLabel("en"));
        Assert.True(row.CollapseAfterActivate);
        Assert.True(row.IsActive());
        row.Activate!();
        Assert.Equal(new[] { "open_history" }, dock.Calls);

        dock.HistoryAvailable = false;
        Assert.False(row.IsActive());
    }

    [Fact]
    public void Game_update_is_offered_only_for_a_newer_game()
    {
        var dock = new Dock { GameUpdateStatus = AndroidGameUpdateStatus.Current };
        var row = dock.Row("GameUpdate");

        Assert.False(row.IsActive());
        Assert.Equal("The game is up to date", row.ResolveLabel("en"));
        Assert.False(row.CollapseAfterActivate);

        dock.GameUpdateStatus = AndroidGameUpdateStatus.Available;
        Assert.True(row.IsActive());
        Assert.Equal("The game has updated: download the new APK", row.ResolveLabel("en"));
        row.Activate!();
        Assert.Equal(new[] { "open_game_update" }, dock.Calls);
    }

    // The published manifest could not be read (a 404 while Android is unreleased, no network):
    // the row says so and never claims the game is current.
    [Theory]
    [InlineData(nameof(AndroidGameUpdateStatus.Checking), "Checking for a game update")]
    [InlineData(nameof(AndroidGameUpdateStatus.CheckFailed), "Could not check for a game update")]
    public void Game_update_without_an_answer_is_not_up_to_date(string status, string label)
    {
        var dock = new Dock { GameUpdateStatus = Enum.Parse<AndroidGameUpdateStatus>(status) };
        var row = dock.Row("GameUpdate");

        Assert.False(row.IsActive());
        Assert.Equal(label, row.ResolveLabel("en"));
        Assert.NotEqual("游戏已是最新版本", row.ResolveLabel("zh-Hans"));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(65536, null)]
    [InlineData(8765, "http://127.0.0.1:8765/latest/android-aarch64.json")]
    public void The_dev_manifest_seam_is_off_by_default_and_loopback_only(
        int port,
        string? expected
    )
    {
        var uri = AndroidVersionCheck.DevManifest(port);

        Assert.Equal(expected, uri?.ToString());
        if (uri != null)
            Assert.True(uri.IsLoopback);
    }

    [Fact]
    public void The_dev_manifest_port_defaults_to_off()
    {
        var android = new AndroidSettings(new MemoryStore());

        Assert.Equal(0, android.DevReleaseManifestPort.Value);
        Assert.Null(AndroidVersionCheck.DevManifest(android.DevReleaseManifestPort.Value));
    }

    private sealed class Dock
    {
        public Dock()
        {
            var store = new MemoryStore();
            Shared = new BppSettings(store);
            Android = new AndroidSettings(store);
            var registry = new SettingsDockEntryRegistry();
            AndroidSettingsDock.Register(
                registry,
                Android,
                new AndroidSettingsDockHooks(
                    reason => Calls.Add($"release_upgrade_preview:{reason}"),
                    () => HistoryAvailable,
                    () => Calls.Add("open_history"),
                    () => GameUpdateStatus,
                    () => Calls.Add("open_game_update")
                )
            );
            Rows = registry
                .MaterializeWithOrder(Shared)
                .OrderBy(row => row.Order)
                .Select(row => row.Definition)
                .ToArray();
        }

        public BppSettings Shared { get; }

        public AndroidSettings Android { get; }

        public IReadOnlyList<BppSettingsDockDefinition> Rows { get; }

        public List<string> Calls { get; } = new();

        public bool HistoryAvailable { get; set; }

        public AndroidGameUpdateStatus GameUpdateStatus { get; set; }

        public BppSettingsDockDefinition Row(string key) => Rows.Single(row => row.Key == key);
    }

    private sealed class MemoryStore : IBppSettingsStore
    {
        public ISetting<T> Bind<T>(
            string section,
            string key,
            T defaultValue,
            string description,
            SettingRange<T>? acceptableRange = null
        )
            where T : IComparable => new Setting<T> { Value = defaultValue };

        private sealed class Setting<T> : ISetting<T>
        {
            public T Value { get; set; } = default!;
        }
    }
}
