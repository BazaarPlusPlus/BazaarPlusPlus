#nullable enable
using BazaarPlusPlus.Android.Host;
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
                "EnchantPreview",
                "GameHistory",
                "UpgradePreview",
                "CollectionPanel",
                "LiveBuildPanel",
                "HistoryPanel",
                "TouchMenu",
                "GameUpdate",
            },
            dock.Rows.Select(row => row.Key)
        );
        Assert.Equal(
            new[]
            {
                BppSettingsControlKind.Choice,
                BppSettingsControlKind.Action,
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Action,
            },
            dock.Rows.Select(row => row.ControlKind)
        );
    }

    [Fact]
    public void Enchant_preview_offers_every_desktop_mode_and_writes_the_shared_setting()
    {
        var dock = new Dock();
        var row = dock.Row("EnchantPreview");

        var state = row.ResolveChoiceState!("en");
        Assert.Equal(new[] { "OFF", "AUTO", "ON" }, state.Options);
        Assert.Equal(2, state.SelectedIndex);

        row.SelectStandardChoice!(1);

        Assert.Equal(
            PreviewVisibilityMode.AutoOnPedestalChoice,
            dock.Shared.EnchantPreviewModeConfig.Value
        );
        Assert.Equal(1, row.ResolveChoiceState("en").SelectedIndex);
    }

    [Theory]
    [InlineData("CollectionPanel", "close_collection")]
    [InlineData("LiveBuildPanel", "close_live_build")]
    [InlineData("HistoryPanel", "close_history")]
    public void Switching_a_panel_off_closes_it_and_returns_its_cards(string key, string close)
    {
        var dock = new Dock();
        var row = dock.Row(key);
        Assert.True(row.ReadToggle!());

        row.WriteToggle!(false);

        Assert.False(row.ReadToggle());
        Assert.Equal(new[] { $"{close}:switched_off", "release_cards:switched_off" }, dock.Calls);

        dock.Calls.Clear();
        row.WriteToggle(true);
        Assert.True(row.ReadToggle());
        Assert.Empty(dock.Calls);
    }

    [Fact]
    public void Switching_upgrade_preview_off_releases_its_tooltip()
    {
        var dock = new Dock();
        var row = dock.Row("UpgradePreview");

        row.WriteToggle!(true);
        Assert.True(dock.Android.UpgradePreview.Value);
        Assert.Empty(dock.Calls);

        row.WriteToggle(false);
        Assert.False(dock.Android.UpgradePreview.Value);
        Assert.Equal(new[] { "release_upgrade_preview:switched_off" }, dock.Calls);
    }

    [Fact]
    public void Game_history_has_no_hotkey_and_follows_the_history_switch()
    {
        var dock = new Dock { HistoryAvailable = true };
        var row = dock.Row("GameHistory");

        Assert.Equal("Game History", row.ResolveLabel("en"));
        Assert.True(row.CollapseAfterActivate);
        Assert.True(row.IsActive());
        row.Activate!();
        Assert.Equal(new[] { "open_history" }, dock.Calls);

        dock.Android.HistoryPanel.Value = false;
        Assert.False(row.IsActive());
        dock.Android.HistoryPanel.Value = true;
        dock.HistoryAvailable = false;
        Assert.False(row.IsActive());
    }

    [Fact]
    public void Game_update_is_offered_only_for_a_newer_game()
    {
        var dock = new Dock();
        var row = dock.Row("GameUpdate");

        Assert.False(row.IsActive());
        Assert.Equal("The game is up to date", row.ResolveLabel("en"));
        Assert.False(row.CollapseAfterActivate);

        dock.GameUpdateAvailable = true;
        Assert.True(row.IsActive());
        Assert.NotEqual("The game is up to date", row.ResolveLabel("en"));
        row.Activate!();
        Assert.Equal(new[] { "open_game_update" }, dock.Calls);
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
                    reason => Calls.Add($"close_collection:{reason}"),
                    reason => Calls.Add($"close_live_build:{reason}"),
                    reason => Calls.Add($"close_history:{reason}"),
                    reason => Calls.Add($"release_upgrade_preview:{reason}"),
                    reason => Calls.Add($"release_cards:{reason}"),
                    () => HistoryAvailable,
                    () => Calls.Add("open_history"),
                    () => GameUpdateAvailable,
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

        public bool GameUpdateAvailable { get; set; }

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
