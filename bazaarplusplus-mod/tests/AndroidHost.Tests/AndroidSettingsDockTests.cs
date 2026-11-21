#nullable enable
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.Lobby;
using BazaarPlusPlus.Android.Settings;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.Mobile.Previews;
using Xunit;

namespace AndroidHost.Tests;

public sealed class AndroidSettingsDockTests
{
    [Fact]
    public void Rows_follow_the_shared_dock_order()
    {
        var dock = new Dock();

        Assert.Equal(
            new[]
            {
                "DetailedPreview",
                "PreviewTextSize",
                "BazaarDbUpload",
                "CombatStatusBar",
                "EnchantPreview",
                "ChineseLocaleMode",
                "GameHistory",
                "UpgradePreview",
                "GameUpdate",
                "FrameRatePriority",
                "ChineseTranslationImprovements",
                "RenderScale",
            },
            dock.Rows.Select(row => row.Key)
        );
        Assert.Equal(
            new[]
            {
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Choice,
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Choice,
                BppSettingsControlKind.Choice,
                BppSettingsControlKind.Action,
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Action,
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Choice,
            },
            dock.Rows.Select(row => row.ControlKind)
        );
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("zh-CN", true)]
    public void The_section_resolves_every_roster_row(string language, bool chineseImprovements)
    {
        var dock = new Dock();

        var states = BppSettingsSectionRules
            .BuildOrder(dock.Rows)
            .Select(row => BppSettingsSectionRules.Resolve(row, language))
            .ToArray();

        Assert.Equal(
            new[]
            {
                "DetailedPreview",
                "BazaarDbUpload",
                "CombatStatusBar",
                "UpgradePreview",
                "FrameRatePriority",
                "ChineseTranslationImprovements",
                "PreviewTextSize",
                "EnchantPreview",
                "ChineseLocaleMode",
                "RenderScale",
                "GameHistory",
                "GameUpdate",
            },
            states.Select(state => state.Key)
        );
        Assert.All(states, state => Assert.True(state.Resolved));
        Assert.All(states, state => Assert.False(string.IsNullOrEmpty(state.Label)));
        Assert.Equal(
            chineseImprovements,
            states.Single(state => state.Key == "ChineseTranslationImprovements").Visible
        );
        Assert.All(
            states.Where(state => state.Key != "ChineseTranslationImprovements"),
            state => Assert.True(state.Visible)
        );
        Assert.All(
            states.Where(state => state.Kind == BppSettingsControlKind.Choice),
            state =>
            {
                Assert.True(state.Interactable);
                Assert.NotEmpty(state.Options);
            }
        );
        Assert.All(
            states.Where(state => state.Kind == BppSettingsControlKind.Action),
            state => Assert.False(state.Interactable)
        );
        Assert.True(states.Single(state => state.Key == "DetailedPreview").ToggleOn);
    }

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
        Assert.Equal(new[] { "refresh_previews" }, dock.Calls);
    }

    [Fact]
    public void Combat_status_writes_the_shared_default_on_setting()
    {
        var dock = new Dock();
        var row = dock.Row("CombatStatusBar");
        Assert.Equal("Combat Status", row.ResolveLabel("en"));
        Assert.True(row.ReadToggle!());
        row.WriteToggle!(false);
        Assert.False(dock.Shared.EnableCombatStatusBarConfig.Value);
        row.WriteToggle(true);
        Assert.True(dock.Shared.EnableCombatStatusBarConfig.Value);
        Assert.Empty(dock.Calls);
    }

    [Fact]
    public void Switching_upgrade_preview_off_releases_its_tooltip()
    {
        var dock = new Dock();
        var row = dock.Row("UpgradePreview");
        Assert.Equal("Upgrade preview", row.ResolveLabel("en"));
        Assert.False(dock.Store.Bool("Touch", "UpgradePreview").Value);
        Assert.False(row.ReadToggle!());

        row.WriteToggle!(true);
        Assert.True(dock.Store.Bool("Touch", "UpgradePreview").Value);
        Assert.Empty(dock.Calls);

        row.WriteToggle(false);
        Assert.False(dock.Store.Bool("Touch", "UpgradePreview").Value);
        Assert.Equal(new[] { "release_upgrade_preview:switched_off" }, dock.Calls);
    }

    [Fact]
    public void Preview_text_size_starts_at_the_old_size_and_refreshes_the_preview()
    {
        var dock = new Dock();
        var row = dock.Row("PreviewTextSize");
        Assert.Equal("Preview text size", row.ResolveLabel("en"));
        Assert.Equal("预览字号", row.ResolveLabel("zh-Hans"));

        var state = row.ResolveChoiceState!("en");
        Assert.Equal(new[] { "75%", "90%", "100%" }, state.Options);
        Assert.Equal(0, state.SelectedIndex);

        row.SelectStandardChoice!(2);

        Assert.Equal(100, dock.Store.Int("Touch", "PreviewTextSize").Value);
        Assert.Equal(2, row.ResolveChoiceState("en").SelectedIndex);
        Assert.Equal(new[] { "refresh_previews" }, dock.Calls);
    }

    [Theory]
    [InlineData(75, "<size=75%>x</size>")]
    [InlineData(100, "<size=100%>x</size>")]
    [InlineData(0, "<size=50%>x</size>")]
    [InlineData(400, "<size=150%>x</size>")]
    public void Preview_text_size_wraps_within_a_readable_range(int percent, string expected) =>
        Assert.Equal(expected, TouchPreviewTextSize.Wrap("x", percent));

    [Fact]
    public void Frame_rate_priority_starts_off_and_writes_the_android_setting()
    {
        var dock = new Dock();
        var row = dock.Row("FrameRatePriority");
        Assert.Equal("Frame rate priority", row.ResolveLabel("en"));
        Assert.Equal("帧率优先", row.ResolveLabel("zh-Hans"));
        Assert.False(row.ReadToggle!());

        row.WriteToggle!(true);
        Assert.True(dock.Android.FrameRatePriority.Value);
        row.WriteToggle(false);
        Assert.False(dock.Android.FrameRatePriority.Value);
        Assert.Empty(dock.Calls);
    }

    [Fact]
    public void Render_scale_starts_native_and_writes_the_android_setting()
    {
        var dock = new Dock();
        var row = dock.Row("RenderScale");
        Assert.Equal("Render scale", row.ResolveLabel("en"));
        Assert.Equal("渲染比例", row.ResolveLabel("zh-Hans"));
        var state = row.ResolveChoiceState!("en");
        Assert.Equal(new[] { "100%", "80%", "67%", "50%" }, state.Options);
        Assert.Equal(0, state.SelectedIndex);

        row.SelectStandardChoice!(3);

        Assert.Equal(50, dock.Android.RenderScale.Value);
        Assert.Equal(50, dock.Store.Int("Graphics", "RenderScale").Value);
        Assert.Empty(dock.Calls);
    }

    [Fact]
    public void Chinese_locale_writes_the_shared_setting_and_tells_the_host()
    {
        var dock = new Dock();
        var row = dock.Row("ChineseLocaleMode");

        var state = row.ResolveChoiceState!("en");
        Assert.Equal(new[] { "CN", "TW" }, state.Options);
        Assert.Equal(0, state.SelectedIndex);

        row.SelectStandardChoice!(1);

        Assert.Equal(BppChineseLocaleMode.Taiwan, dock.Shared.ChineseLocaleModeConfig.Value);
        Assert.Equal(new[] { "locale_changed" }, dock.Calls);
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
    public void The_new_android_app_is_offered_only_while_the_release_needs_it()
    {
        var dock = new Dock { GameUpdateStatus = AndroidGameUpdateStatus.Current };
        var row = dock.Row("GameUpdate");

        Assert.False(row.IsActive());
        Assert.Equal("The game is up to date", row.ResolveLabel("en"));
        Assert.False(row.CollapseAfterActivate);

        dock.GameUpdateStatus = AndroidGameUpdateStatus.AppUpdated;
        Assert.True(row.IsActive());
        Assert.Equal("Install the new BazaarPlusPlus App", row.ResolveLabel("en"));
        Assert.Equal("请安装新版 BazaarPlusPlus App", row.ResolveLabel("zh-Hans"));
        row.Activate!();
        Assert.Equal(new[] { "open_game_update" }, dock.Calls);
    }

    [Fact]
    public void A_newer_game_is_named_but_offers_no_download()
    {
        var dock = new Dock { GameUpdateStatus = AndroidGameUpdateStatus.GameUpdated };
        var row = dock.Row("GameUpdate");

        Assert.False(row.IsActive());
        Assert.Equal("The Bazaar has an update: get it from its store", row.ResolveLabel("en"));
        Assert.Equal("The Bazaar 有更新，请从它的商店更新", row.ResolveLabel("zh-Hans"));
    }

    [Fact]
    public void A_game_the_release_does_not_adapt_is_named_but_offers_no_download()
    {
        var dock = new Dock { GameUpdateStatus = AndroidGameUpdateStatus.NotAdapted };
        var row = dock.Row("GameUpdate");

        Assert.False(row.IsActive());
        Assert.Equal("BazaarPlusPlus is not adapted to this game version", row.ResolveLabel("en"));
        Assert.Equal("BazaarPlusPlus 未适配此游戏版本", row.ResolveLabel("zh-Hans"));
    }

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
            Shared = new BppSettings(Store);
            Android = new AndroidSettings(Store);
            var registry = new SettingsDockEntryRegistry();
            AndroidSettingsDock.Register(
                registry,
                new AndroidSettingsDockSettings(
                    TouchUpgradePreviewSetting.Bind(Store),
                    TouchPreviewTextSize.Bind(Store),
                    Android.FrameRatePriority,
                    Android.RenderScale
                ),
                new AndroidSettingsDockHooks(
                    reason => Calls.Add($"release_upgrade_preview:{reason}"),
                    () => HistoryAvailable,
                    () => Calls.Add("open_history"),
                    () => GameUpdateStatus,
                    () => Calls.Add("open_game_update"),
                    () => Calls.Add("refresh_previews"),
                    () => Calls.Add("locale_changed")
                )
            );
            Rows = registry
                .MaterializeWithOrder(Shared)
                .OrderBy(row => row.Order)
                .Select(row => row.Definition)
                .ToArray();
        }

        public MemoryStore Store { get; } = new();

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
        private readonly Dictionary<(string, string), object> _bound = new();

        public ISetting<T> Bind<T>(
            string section,
            string key,
            T defaultValue,
            string description,
            SettingRange<T>? acceptableRange = null
        )
            where T : IComparable
        {
            var setting = new Setting<T> { Value = defaultValue };
            _bound[(section, key)] = setting;
            return setting;
        }

        public ISetting<bool> Bool(string section, string key) =>
            (ISetting<bool>)_bound[(section, key)];

        public ISetting<int> Int(string section, string key) =>
            (ISetting<int>)_bound[(section, key)];

        private sealed class Setting<T> : ISetting<T>
        {
            public T Value { get; set; } = default!;
        }
    }
}
