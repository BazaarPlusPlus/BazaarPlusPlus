using System.Runtime.CompilerServices;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.BilingualItemNames;
using BazaarPlusPlus.Game.EventPreview;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.LegendaryPosition;
using BazaarPlusPlus.Game.NameOverride;
using BazaarPlusPlus.Game.QuestPreview;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Game.VoiceSubtitles;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.Patches;
using BazaarPlusPlus.Storage.Paths;
using BepInEx.Configuration;
using Xunit;

namespace BazaarPlusPlus.Tests.SettingsDockRegistry;

public class SettingsDockRegistryTests
{
    private sealed class FakeEntry : ISettingsDockEntry
    {
        public string Key { get; }
        public int Order { get; init; }
        public int BuildCount { get; private set; }

        public FakeEntry(string key)
        {
            Key = key;
        }

        public BppSettingsDockDefinition Build(IBppConfig config)
        {
            BuildCount++;
            return new BppSettingsDockDefinition(
                Key,
                resolveLabel: _ => $"Label-{Key}",
                isActive: () => true,
                activate: () => { },
                collapseAfterActivate: false
            );
        }
    }

    private sealed class TestLanguageProvider : ILanguageProvider
    {
        private readonly string _languageCode;

        public TestLanguageProvider(string languageCode = "en")
        {
            _languageCode = languageCode;
        }

        public string CurrentLanguageCode => _languageCode;
    }

    private sealed class TestLocaleModeProvider : ILocaleModeProvider
    {
        private readonly BppChineseLocaleMode _mode;

        public TestLocaleModeProvider(BppChineseLocaleMode mode = BppChineseLocaleMode.Mainland)
        {
            _mode = mode;
        }

        public BppChineseLocaleMode CurrentMode => _mode;
    }

    private sealed class ContractTestServices : IBppServices
    {
        public ContractTestServices(IBppConfig config)
        {
            Config = config;
        }

        public IBppConfig Config { get; }
        public IBppEventBus EventBus => null!;
        public IPathProvider Paths => null!;
        public IRunContext RunContext => null!;
        public IGameStateProbe GameStateProbe => null!;
        public IEncounterStateProbe EncounterState => null!;
        public IRunSnapshotProbe RunSnapshot => null!;
        public IGameBuildInfo GameBuild => null!;
    }

    [Fact]
    public void MaterializeAll_returns_one_definition_per_registered_entry()
    {
        var registry = new SettingsDockEntryRegistry();
        var a = new FakeEntry("A");
        var b = new FakeEntry("B");

        registry.Register(a);
        registry.Register(b);

        var defs = registry.MaterializeAll(config: null!);

        Assert.Equal(2, defs.Count);
        Assert.Contains(defs, d => d.Key == "A");
        Assert.Contains(defs, d => d.Key == "B");
        Assert.Equal(1, a.BuildCount);
        Assert.Equal(1, b.BuildCount);
    }

    [Fact]
    public void Register_null_throws()
    {
        var registry = new SettingsDockEntryRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void MaterializeWithOrder_returns_entries_paired_with_their_Order()
    {
        var registry = new SettingsDockEntryRegistry();
        registry.Register(new FakeEntry("A") { Order = 5 });
        registry.Register(new FakeEntry("B") { Order = 1 });

        var pairs = registry.MaterializeWithOrder(config: null!);

        Assert.Equal(2, pairs.Count);
        // Materialization preserves registration order; sorting is the caller's job.
        Assert.Equal(5, pairs[0].Order);
        Assert.Equal("A", pairs[0].Definition.Key);
        Assert.Equal(1, pairs[1].Order);
        Assert.Equal("B", pairs[1].Definition.Key);
    }

    [Fact]
    public void CyclingEntry_invokes_onChanged_after_write_with_the_new_value()
    {
        var value = 1;
        var observed = new List<(string Phase, int Value)>();
        var entry = new CyclingSettingsDockEntry<int>(
            order: 0,
            key: "Test",
            resolveLabel: _ => "Test",
            ladder: new[] { 1, 2 },
            read: _ => value,
            write: (_, next) =>
            {
                value = next;
                observed.Add(("write", value));
            },
            highlightWhen: _ => false,
            resolveStatus: (current, _) => current.ToString(),
            onChanged: next => observed.Add(("changed", next))
        );
        var definition = entry.Build(new BppConfig());

        definition.SelectStandardChoice!(1);

        Assert.Equal(new[] { ("write", 2), ("changed", 2) }, observed);
    }

    [Fact]
    public void CyclingEntry_status_and_highlight_read_the_current_value()
    {
        var value = 1;
        var entry = CreateIntegerCyclingEntry(() => value, next => value = next);
        var definition = entry.Build(new BppConfig());

        var state = definition.ResolveChoiceState!("en");
        Assert.Equal("1", state.Options[state.SelectedIndex]);
        Assert.False(definition.IsActive());

        value = 2;

        state = definition.ResolveChoiceState("en");
        Assert.Equal("2", state.Options[state.SelectedIndex]);
        Assert.True(definition.IsActive());
    }

    [Fact]
    public void CyclingEntry_Toggle_supplies_boolean_ladder_highlight_and_status()
    {
        var enabled = false;
        var entry = CyclingSettingsDockEntry<bool>.Toggle(
            order: 0,
            key: "Test",
            resolveLabel: _ => "Test",
            read: _ => enabled,
            write: (_, next) => enabled = next
        );
        var definition = entry.Build(new BppConfig());

        Assert.False(definition.IsActive());
        Assert.False(definition.ReadToggle!());

        definition.WriteToggle!(true);

        Assert.True(enabled);
        Assert.True(definition.IsActive());
        Assert.True(definition.ReadToggle());
    }

    [Fact]
    public void Toggle_definition_exposes_native_read_write_contract()
    {
        var enabled = false;
        var definition = CyclingSettingsDockEntry<bool>
            .Toggle(
                order: 0,
                key: "Test",
                resolveLabel: _ => "Test",
                read: _ => enabled,
                write: (_, next) => enabled = next
            )
            .Build(new BppConfig());

        Assert.Equal(BppSettingsControlKind.Toggle, definition.ControlKind);
        Assert.False(definition.ReadToggle!());

        definition.WriteToggle!(true);

        Assert.True(enabled);
        Assert.True(definition.ReadToggle());
    }

    [Fact]
    public void Choice_definition_selects_standard_ladder_value_directly()
    {
        var value = 1;
        var definition = CreateIntegerCyclingEntry(() => value, next => value = next)
            .Build(new BppConfig());

        var state = definition.ResolveChoiceState!("en");
        definition.SelectStandardChoice!(1);

        Assert.Equal(BppSettingsControlKind.Choice, definition.ControlKind);
        Assert.Equal(new[] { "1", "2" }, state.Options);
        Assert.Equal(0, state.SelectedIndex);
        Assert.False(state.HasSyntheticCurrentOption);
        Assert.Equal(2, value);
    }

    [Fact]
    public void Choice_definition_preserves_unknown_current_value_as_synthetic_option()
    {
        var value = 99;
        var definition = CreateIntegerCyclingEntry(() => value, next => value = next)
            .Build(new BppConfig());

        var state = definition.ResolveChoiceState!("en");

        Assert.Equal(new[] { "99", "1", "2" }, state.Options);
        Assert.Equal(0, state.SelectedIndex);
        Assert.True(state.HasSyntheticCurrentOption);
        Assert.Equal(99, value);
    }

    [Fact]
    public void Action_definitions_preserve_close_host_contract()
    {
        var history = new HistoryPanelSettingsDockEntry().Build(new BppConfig());

        Assert.Equal(BppSettingsControlKind.Action, history.ControlKind);
        Assert.True(history.CollapseAfterActivate);
    }

    [Fact]
    public void HistoryPanelDockEntry_label_follows_the_current_history_hotkey()
    {
        var hotkeyDisplay = "F8";
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var history = new HistoryPanelSettingsDockEntry(() => hotkeyDisplay).Build(
                new BppConfig()
            );

            Assert.Equal("Game History (Press F8 to open)", history.ResolveLabel("en"));
            Assert.Equal("对局历史（按 F8 打开）", history.ResolveLabel("zh-CN"));

            hotkeyDisplay = "MMB";

            Assert.Equal("Game History (Press MMB to open)", history.ResolveLabel("en"));
            Assert.Equal("对局历史（按 MMB 打开）", history.ResolveLabel("zh-CN"));
        }
        finally
        {
            L.Reset();
        }
    }

    [Fact]
    public void EndOfRunScreenshot_uses_disabled_native_toggle_while_forced()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-native-screenshot-toggle-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            var config = new BppConfig();
            config.Initialize(new ConfigFile(configPath, saveOnInit: false));
            config.BazaarDbUploadEnabled!.Value = true;
            config.EndOfRunScreenshotEnabledConfig!.Value = false;
            var definition = new EndOfRunScreenshotSettingsDockEntry().Build(config);

            Assert.Equal(BppSettingsControlKind.Toggle, definition.ControlKind);
            Assert.True(definition.ReadToggle!());
            Assert.False(definition.IsInteractable!());
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    private static CyclingSettingsDockEntry<int> CreateIntegerCyclingEntry(
        Func<int> read,
        Action<int> write
    ) =>
        new(
            order: 0,
            key: "Test",
            resolveLabel: _ => "Test",
            ladder: new[] { 1, 2 },
            read: _ => read(),
            write: (_, next) => write(next),
            highlightWhen: current => current != 1,
            resolveStatus: (current, _) => current.ToString()
        );

    [Fact]
    public void NameOverrideDockEntry_requests_refresh_after_every_write()
    {
        var refreshCount = 0;
        var definition = NameOverrideSettingsDockEntry
            .Create(() => refreshCount++)
            .Build(new BppConfig());

        definition.WriteToggle!(true);
        definition.WriteToggle(false);

        Assert.Equal(2, refreshCount);
    }

    [Fact]
    public void EventPreviewDockEntry_uses_true_when_config_is_unavailable()
    {
        var definition = EventPreviewSettingsDockEntry.Create().Build(new BppConfig());

        Assert.True(definition.IsActive());
        Assert.True(definition.ReadToggle!());
    }

    [Fact]
    public void QuestPreviewDockEntry_persists_off_on_off_without_changing_event_preview()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-quest-preview-toggle-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var questDefinition = QuestPreviewSettingsDockEntry.Create(() => { }).Build(config);
            var eventDefinition = EventPreviewSettingsDockEntry.Create().Build(config);

            Assert.False(questDefinition.ReadToggle!());
            Assert.True(eventDefinition.ReadToggle!());

            questDefinition.WriteToggle!(true);
            configFile.Save();

            var reloadedFile = new ConfigFile(configPath, saveOnInit: false);
            var reloaded = new BppConfig();
            reloaded.Initialize(reloadedFile);
            Assert.True(reloaded.EnableQuestPreviewConfig!.Value);
            Assert.True(reloaded.EnableEventPreviewConfig!.Value);

            var reloadedEventDefinition = EventPreviewSettingsDockEntry.Create().Build(reloaded);
            var reloadedQuestDefinition = QuestPreviewSettingsDockEntry
                .Create(() => { })
                .Build(reloaded);
            reloadedEventDefinition.WriteToggle!(false);
            Assert.True(reloadedQuestDefinition.ReadToggle!());

            reloadedQuestDefinition.WriteToggle!(false);
            reloadedFile.Save();

            var finalFile = new ConfigFile(configPath, saveOnInit: false);
            var finalConfig = new BppConfig();
            finalConfig.Initialize(finalFile);
            Assert.False(finalConfig.EnableQuestPreviewConfig!.Value);
            Assert.False(finalConfig.EnableEventPreviewConfig!.Value);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void QuestPreviewDockEntry_clears_pooled_tooltips_when_disabled()
    {
        var clearCount = 0;
        var definition = QuestPreviewSettingsDockEntry
            .Create(() => clearCount++)
            .Build(new BppConfig());

        definition.WriteToggle!(true);
        Assert.Equal(0, clearCount);

        definition.WriteToggle(false);
        Assert.Equal(1, clearCount);
    }

    [Theory]
    [InlineData("en", false, "Quest Preview")]
    [InlineData("zh-CN", false, "任务预览")]
    [InlineData("zh-Hant", true, "任務預覽")]
    [InlineData("de", false, "Questvorschau")]
    [InlineData("pt-BR", false, "Prévia de Missão")]
    [InlineData("ko", false, "퀘스트 미리보기")]
    [InlineData("it", false, "Anteprima Missione")]
    public void QuestPreviewDockEntry_localizes_broad_label_in_every_supported_locale(
        string languageCode,
        bool useTaiwanLocale,
        string expected
    )
    {
        L.Install(
            new TestLanguageProvider(languageCode),
            new TestLocaleModeProvider(
                useTaiwanLocale ? BppChineseLocaleMode.Taiwan : BppChineseLocaleMode.Mainland
            )
        );

        var definition = QuestPreviewSettingsDockEntry.Create(() => { }).Build(new BppConfig());

        Assert.Equal(expected, definition.ResolveLabel(languageCode));
    }

    [Fact]
    public void QuestPreviewGate_reads_only_quest_preview_config()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-quest-preview-gate-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            var config = new BppConfig();
            config.Initialize(new ConfigFile(configPath, saveOnInit: false));
            BppPatchHost.Install(
                new ContractTestServices(config),
                (BppPatchFeatures)RuntimeHelpers.GetUninitializedObject(typeof(BppPatchFeatures))
            );

            Assert.False(QuestPreviewGate.IsEnabled());

            config.EnableEventPreviewConfig!.Value = false;
            Assert.False(QuestPreviewGate.IsEnabled());

            config.EnableQuestPreviewConfig!.Value = true;
            Assert.True(QuestPreviewGate.IsEnabled());

            config.EnableEventPreviewConfig.Value = true;
            Assert.True(QuestPreviewGate.IsEnabled());
        }
        finally
        {
            BppPatchHost.Reset();
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void SettingsDockOrder_pins_complete_presented_sequence_and_toggle_pairings()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-settings-dock-order-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            var config = new BppConfig();
            config.Initialize(new ConfigFile(configPath, saveOnInit: false));
            var registry = new SettingsDockEntryRegistry();
            registry.Register(BazaarDbBundleSettingsDockEntry.Create());
            registry.Register(FixedSupporterListSettingsDockEntry.Create());
            VoiceSubtitlesSettingsDockEntry.RegisterAll(registry);
            registry.Register(ChineseLocaleModeSettingsDockEntry.Create(new InMemoryBppEventBus()));
            registry.Register(BilingualItemNamesSettingsDockEntry.Create());
            registry.Register(new EndOfRunScreenshotSettingsDockEntry());
            registry.Register(new HistoryPanelSettingsDockEntry());
            registry.Register(ItemEnchantPreviewSettingsDockEntry.Create());
            registry.Register(UpgradePreviewActivationSettingsDockEntry.Create());
            registry.Register(EventPreviewSettingsDockEntry.Create());
            registry.Register(QuestPreviewSettingsDockEntry.Create(() => { }));
            registry.Register(LegendaryPositionSettingsDockEntry.Create(() => { }));
            registry.Register(NameOverrideSettingsDockEntry.Create(() => { }));

            var presented = registry
                .MaterializeWithOrder(config)
                .OrderBy(entry => entry.Order)
                .ToArray();

            Assert.Equal(Enumerable.Range(0, 16), presented.Select(entry => entry.Order));
            Assert.Equal(
                new[]
                {
                    "NameOverride",
                    "StreamMode",
                    "BilingualItemNames",
                    "EventPreview",
                    "QuestPreview",
                    "EndOfRunScreenshot",
                    "BazaarDbUpload",
                    "EnchantPreview",
                    "UpgradePreviewActivation",
                    "LegendaryPositionDisplay",
                    "ChineseLocaleMode",
                    "VoiceSubtitles",
                    "VoiceSubtitlesPosition",
                    "VoiceSubtitlesEnglishFontScale",
                    "VoiceSubtitlesChineseFontScale",
                    "GameHistory",
                },
                presented.Select(entry => entry.Definition.Key)
            );

            var groups = BppNativeSettingsSectionController.PlanToggleRowGroups(
                presented.Select(entry => entry.Definition.ControlKind).ToArray()
            );
            Assert.Equal(
                new (string Left, string? Right)[]
                {
                    ("NameOverride", "StreamMode"),
                    ("BilingualItemNames", "EventPreview"),
                    ("QuestPreview", "EndOfRunScreenshot"),
                    ("BazaarDbUpload", null),
                },
                groups.Select(group =>
                    (
                        Left: presented[group.LeftIndex].Definition.Key,
                        Right: group.RightIndex.HasValue
                            ? presented[group.RightIndex.Value].Definition.Key
                            : null
                    )
                )
            );
            Assert.All(
                presented.Take(7),
                entry => Assert.Equal(BppSettingsControlKind.Toggle, entry.Definition.ControlKind)
            );
            Assert.All(
                presented.Skip(7).Take(8),
                entry => Assert.Equal(BppSettingsControlKind.Choice, entry.Definition.ControlKind)
            );
            Assert.Equal(BppSettingsControlKind.Action, presented[^1].Definition.ControlKind);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void ToggleRowGrouping_keeps_odd_toggle_and_leaves_following_non_toggle_rows_untouched()
    {
        var groups = BppNativeSettingsSectionController.PlanToggleRowGroups(
            new[]
            {
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Choice,
                BppSettingsControlKind.Action,
            }
        );

        Assert.Equal(new (int LeftIndex, int? RightIndex)[] { (0, 1), (2, null) }, groups);
        Assert.Single(groups, group => !group.RightIndex.HasValue);
        Assert.DoesNotContain(groups, group => group.LeftIndex >= 3 || group.RightIndex >= 3);
    }

    [Fact]
    public void ToggleRowGrouping_preserves_existing_paired_toggle_layout()
    {
        var groups = BppNativeSettingsSectionController.PlanToggleRowGroups(
            new[]
            {
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Toggle,
                BppSettingsControlKind.Choice,
            }
        );

        Assert.Equal(new (int LeftIndex, int? RightIndex)[] { (0, 1) }, groups);
    }

    [Fact]
    public void ChineseLocaleModeConfig_migrates_legacy_hong_kong_to_taiwan()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-chinese-locale-settings-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            File.WriteAllText(
                configPath,
                """
                [Localization]

                ChineseLocaleMode = HongKong
                """
            );
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();

            config.Initialize(configFile);

            Assert.Equal(BppChineseLocaleMode.Taiwan, config.ChineseLocaleModeConfig!.Value);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void ChineseLocaleModeDockEntry_selects_between_cn_and_tw_only()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-chinese-locale-dock-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var eventBus = new InMemoryBppEventBus();
            var changedCount = 0;
            using var subscription = eventBus.Subscribe<ChineseLocaleModeChanged>(_ =>
                changedCount++
            );
            var definition = ChineseLocaleModeSettingsDockEntry.Create(eventBus).Build(config);

            var state = definition.ResolveChoiceState!("en");
            Assert.Equal(new[] { "CN", "TW" }, state.Options);
            Assert.Equal(0, state.SelectedIndex);
            Assert.False(definition.IsActive());

            definition.SelectStandardChoice!(1);

            Assert.Equal(BppChineseLocaleMode.Taiwan, config.ChineseLocaleModeConfig!.Value);
            Assert.True(definition.IsActive());

            definition.SelectStandardChoice(0);

            Assert.Equal(BppChineseLocaleMode.Mainland, config.ChineseLocaleModeConfig!.Value);
            Assert.False(definition.IsActive());

            config.ChineseLocaleModeConfig.Value = (BppChineseLocaleMode)2;

            state = definition.ResolveChoiceState("en");
            Assert.Equal("TW", state.Options[state.SelectedIndex]);
            Assert.True(definition.IsActive());

            definition.SelectStandardChoice(0);

            Assert.Equal(BppChineseLocaleMode.Mainland, config.ChineseLocaleModeConfig.Value);
            Assert.Equal(3, changedCount);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void EnchantPreviewDockEntry_recovers_from_unknown_mode_via_choice_selection()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-enchant-preview-invalid-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            config.EnchantPreviewModeConfig!.Value = (PreviewVisibilityMode)99;
            var definition = ItemEnchantPreviewSettingsDockEntry.Create().Build(config);

            var state = definition.ResolveChoiceState!("en");
            Assert.True(state.HasSyntheticCurrentOption);

            definition.SelectStandardChoice!(1);

            Assert.Equal(
                PreviewVisibilityMode.AutoOnPedestalChoice,
                config.EnchantPreviewModeConfig.Value
            );
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void EndOfRunScreenshotDockEntry_defaults_on_and_toggles_config()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-end-of-run-screenshot-settings-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var entry = new EndOfRunScreenshotSettingsDockEntry();

            var definition = entry.Build(config);

            Assert.Equal(BppSettingsDockOrder.EndOfRunScreenshot, entry.Order);
            Assert.Equal("EndOfRunScreenshot", definition.Key);
            Assert.Equal("End-of-run Screenshot", definition.ResolveLabel("en"));
            Assert.Equal("终局截图", definition.ResolveLabel("zh-CN"));
            Assert.True(definition.IsActive());
            Assert.True(definition.ReadToggle!());

            definition.WriteToggle!(false);

            Assert.False(config.EndOfRunScreenshotEnabledConfig!.Value);
            Assert.False(definition.IsActive());
            Assert.False(definition.ReadToggle());
            Assert.False(definition.CollapseAfterActivate);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Theory]
    [InlineData("BazaarDbUpload")]
    [InlineData("StreamMode")]
    public void EnablingUploadOrStreamMode_forcesEndOfRunScreenshotOn(string dependencyKey)
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-screenshot-dependency-settings-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            config.EndOfRunScreenshotEnabledConfig!.Value = false;

            var screenshotDefinition = new EndOfRunScreenshotSettingsDockEntry().Build(config);
            var dependencyDefinition =
                dependencyKey == "BazaarDbUpload"
                    ? BazaarDbBundleSettingsDockEntry.Create().Build(config)
                    : FixedSupporterListSettingsDockEntry.Create().Build(config);

            Assert.False(screenshotDefinition.IsActive());
            Assert.False(screenshotDefinition.ReadToggle!());

            dependencyDefinition.WriteToggle!(true);

            Assert.True(config.EndOfRunScreenshotEnabledConfig.Value);
            Assert.True(screenshotDefinition.IsActive());
            Assert.True(screenshotDefinition.ReadToggle());

            dependencyDefinition.WriteToggle(false);

            Assert.True(config.EndOfRunScreenshotEnabledConfig.Value);
            Assert.True(screenshotDefinition.IsActive());
            Assert.True(screenshotDefinition.ReadToggle());
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Theory]
    [InlineData("BazaarDbUpload")]
    [InlineData("StreamMode")]
    public void EndOfRunScreenshotDockEntry_doesNotTurnOffWhileUploadOrStreamModeIsOn(
        string dependencyKey
    )
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-screenshot-forced-on-settings-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            config.EndOfRunScreenshotEnabledConfig!.Value = true;

            if (dependencyKey == "BazaarDbUpload")
                config.BazaarDbUploadEnabled!.Value = true;
            else
                config.UseFixedSupporterListConfig!.Value = true;

            var screenshotDefinition = new EndOfRunScreenshotSettingsDockEntry().Build(config);

            Assert.True(screenshotDefinition.IsActive());
            Assert.True(screenshotDefinition.ReadToggle!());
            Assert.False(screenshotDefinition.IsInteractable!());
            Assert.True(config.EndOfRunScreenshotEnabledConfig.Value);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void FixedSupporterListDockEntry_uses_stream_mode_key_and_toggles_config()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-fixed-supporters-settings-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var entry = FixedSupporterListSettingsDockEntry.Create();

            var definition = entry.Build(config);

            Assert.Equal(BppSettingsDockOrder.FixedSupporterList, entry.Order);
            Assert.Equal("StreamMode", definition.Key);
            Assert.Equal("Stream Mode", definition.ResolveLabel("en"));
            Assert.Equal("直播模式", definition.ResolveLabel("zh-CN"));
            Assert.False(definition.IsActive());
            Assert.False(definition.ReadToggle!());

            definition.WriteToggle!(true);

            Assert.True(config.UseFixedSupporterListConfig!.Value);
            Assert.True(definition.IsActive());
            Assert.True(definition.ReadToggle());
            Assert.False(definition.CollapseAfterActivate);

            configFile.Save();

            var reloadedConfigFile = new ConfigFile(configPath, saveOnInit: false);
            var reloadedConfig = new BppConfig();
            reloadedConfig.Initialize(reloadedConfigFile);

            Assert.True(reloadedConfig.UseFixedSupporterListConfig!.Value);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void VoiceSubtitlesDockEntry_selects_subtitle_mode_from_off_both_chinese_english()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-voice-subtitles-settings-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var entry = VoiceSubtitlesSettingsDockEntry.Create();

            var definition = entry.Build(config);

            Assert.Equal(BppSettingsDockOrder.VoiceSubtitles, entry.Order);
            Assert.Equal("VoiceSubtitles", definition.Key);
            Assert.Equal("Subtitle Mode", definition.ResolveLabel("en"));
            Assert.Equal("字幕模式", definition.ResolveLabel("zh-CN"));
            Assert.False(config.EnableVoiceSubtitlesConfig!.Value);
            Assert.Equal(SubtitleLanguageMode.Both, config.VoiceSubtitlesLanguageModeConfig!.Value);
            Assert.False(definition.IsActive());
            var state = definition.ResolveChoiceState!("en");
            Assert.Equal(new[] { "OFF", "BOTH", "ZH", "EN" }, state.Options);
            Assert.Equal(0, state.SelectedIndex);
            Assert.Equal(
                new[] { "关闭", "双语", "中文", "英文" },
                definition.ResolveChoiceState("zh-CN").Options
            );

            definition.SelectStandardChoice!(1);

            Assert.True(config.EnableVoiceSubtitlesConfig.Value);
            Assert.Equal(SubtitleLanguageMode.Both, config.VoiceSubtitlesLanguageModeConfig.Value);
            Assert.True(definition.IsActive());

            definition.SelectStandardChoice(2);

            Assert.True(config.EnableVoiceSubtitlesConfig.Value);
            Assert.Equal(
                SubtitleLanguageMode.ChineseOnly,
                config.VoiceSubtitlesLanguageModeConfig.Value
            );
            Assert.True(definition.IsActive());

            definition.SelectStandardChoice(3);

            Assert.True(config.EnableVoiceSubtitlesConfig.Value);
            Assert.Equal(
                SubtitleLanguageMode.EnglishOnly,
                config.VoiceSubtitlesLanguageModeConfig.Value
            );
            Assert.True(definition.IsActive());

            definition.SelectStandardChoice(0);

            Assert.False(config.EnableVoiceSubtitlesConfig.Value);
            Assert.Equal(SubtitleLanguageMode.Both, config.VoiceSubtitlesLanguageModeConfig.Value);
            Assert.False(definition.IsActive());
            Assert.False(definition.CollapseAfterActivate);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void VoiceSubtitlesPositionDockEntry_defaults_top_center_and_selects_top_left()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-voice-subtitles-position-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var definition = VoiceSubtitlesPositionSettingsDockEntry.Create().Build(config);

            Assert.Equal(SubtitlePosition.TopCenter, config.VoiceSubtitlesPositionConfig!.Value);
            var state = definition.ResolveChoiceState!("en");
            Assert.Equal("Top Center", state.Options[state.SelectedIndex]);
            var chineseState = definition.ResolveChoiceState("zh-CN");
            Assert.Equal("顶部居中", chineseState.Options[chineseState.SelectedIndex]);
            Assert.False(definition.IsActive());

            definition.SelectStandardChoice!(0);

            Assert.Equal(SubtitlePosition.TopLeft, config.VoiceSubtitlesPositionConfig.Value);
            state = definition.ResolveChoiceState("en");
            Assert.Equal("Top Left", state.Options[state.SelectedIndex]);
            Assert.True(definition.IsActive());
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void VoiceSubtitlesDockEntries_register_master_and_setting_rows()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-voice-subtitles-dock-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var registry = new SettingsDockEntryRegistry();

            VoiceSubtitlesSettingsDockEntry.RegisterAll(registry);

            var definitions = registry.MaterializeWithOrder(config);

            Assert.Equal(
                new[]
                {
                    "VoiceSubtitles",
                    "VoiceSubtitlesPosition",
                    "VoiceSubtitlesEnglishFontScale",
                    "VoiceSubtitlesChineseFontScale",
                },
                definitions.Select(d => d.Definition.Key)
            );
            Assert.Equal(
                new[]
                {
                    BppSettingsDockOrder.VoiceSubtitles,
                    BppSettingsDockOrder.VoiceSubtitlesPosition,
                    BppSettingsDockOrder.VoiceSubtitlesEnglishFontScale,
                    BppSettingsDockOrder.VoiceSubtitlesChineseFontScale,
                },
                definitions.Select(d => d.Order)
            );
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void VoiceSubtitlesDockEntry_defaults_chinese_scale_to_one_and_selects_next_ladder_value()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-voice-subtitles-scale-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            var definition = VoiceSubtitlesChineseFontScaleSettingsDockEntry.Create().Build(config);

            Assert.Equal("Chinese Size", definition.ResolveLabel("en"));
            Assert.Equal("中文字号", definition.ResolveLabel("zh-CN"));

            L.Install(
                new TestLanguageProvider(),
                new TestLocaleModeProvider(BppChineseLocaleMode.Taiwan)
            );
            Assert.Equal("中文字號", definition.ResolveLabel("zh-Hant"));

            Assert.Equal(1.0f, config.VoiceSubtitlesChineseFontScaleConfig!.Value, precision: 2);
            var state = definition.ResolveChoiceState!("en");
            Assert.Equal("1x", state.Options[state.SelectedIndex]);
            Assert.False(definition.IsActive());

            definition.SelectStandardChoice!(1);

            state = definition.ResolveChoiceState("en");
            Assert.Equal("1.25x", state.Options[state.SelectedIndex]);
            Assert.True(definition.IsActive());
            Assert.Equal(1.25f, config.VoiceSubtitlesChineseFontScaleConfig.Value, precision: 2);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void VoiceSubtitlesFontScaleDockEntry_preserves_off_ladder_value_as_synthetic_choice()
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-voice-subtitles-scale-between-steps-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);
            config.VoiceSubtitlesEnglishFontScaleConfig!.Value = 1.3f;
            var definition = VoiceSubtitlesEnglishFontScaleSettingsDockEntry.Create().Build(config);

            var state = definition.ResolveChoiceState!("en");
            Assert.True(state.HasSyntheticCurrentOption);
            Assert.Equal("1.3x", state.Options[0]);

            definition.SelectStandardChoice!(2);

            Assert.Equal(1.5f, config.VoiceSubtitlesEnglishFontScaleConfig.Value, precision: 2);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    [Fact]
    public void HistoryPanelDockEntry_uses_game_history_order()
    {
        Assert.Equal(BppSettingsDockOrder.GameHistory, new HistoryPanelSettingsDockEntry().Order);
    }

    [Theory]
    [InlineData(
        "NameOverride",
        BppSettingsDockOrder.NameOverride,
        "Anonymous Mode",
        "匿名模式",
        "OFF>ON>OFF",
        "false>true>false"
    )]
    [InlineData(
        "LegendaryPositionDisplay",
        BppSettingsDockOrder.LegendaryPosition,
        "Legendary Position",
        "传奇名次",
        "DEF>BLANK>999999>P|R>DEF",
        "false>true>true>true>false"
    )]
    [InlineData(
        "EnchantPreview",
        BppSettingsDockOrder.EnchantPreview,
        "Enchant Preview",
        "附魔预览",
        "ON>OFF>AUTO>ON",
        "true>false>true>true"
    )]
    [InlineData(
        "UpgradePreviewActivation",
        BppSettingsDockOrder.UpgradePreviewActivation,
        "Shift Mode",
        "Shift 模式",
        "HOLD SHIFT>TOGGLE SHIFT>HOLD SHIFT",
        "false>true>false"
    )]
    [InlineData(
        "EventPreview",
        BppSettingsDockOrder.EventPreview,
        "Event Preview",
        "事件预览",
        "ON>OFF>ON",
        "true>false>true"
    )]
    [InlineData(
        "QuestPreview",
        BppSettingsDockOrder.QuestPreview,
        "Quest Preview",
        "任务预览",
        "OFF>ON>OFF",
        "false>true>false"
    )]
    [InlineData(
        "BilingualItemNames",
        BppSettingsDockOrder.BilingualItemNames,
        "Bilingual Names",
        "双语名称",
        "OFF>ON>OFF",
        "false>true>false"
    )]
    [InlineData(
        "ChineseLocaleMode",
        BppSettingsDockOrder.ChineseLocaleMode,
        "Chinese Locale",
        "中文模式",
        "CN>TW>CN",
        "false>true>false"
    )]
    [InlineData(
        "StreamMode",
        BppSettingsDockOrder.FixedSupporterList,
        "Stream Mode",
        "直播模式",
        "OFF>ON>OFF",
        "false>true>false"
    )]
    [InlineData(
        "VoiceSubtitles",
        BppSettingsDockOrder.VoiceSubtitles,
        "Subtitle Mode",
        "字幕模式",
        "OFF>BOTH>ZH>EN>OFF",
        "false>true>true>true>false"
    )]
    [InlineData(
        "VoiceSubtitlesPosition",
        BppSettingsDockOrder.VoiceSubtitlesPosition,
        "Subtitle Position",
        "字幕位置",
        "Top Center>Top Left>Top Right>Top Center",
        "false>true>true>false"
    )]
    [InlineData(
        "VoiceSubtitlesEnglishFontScale",
        BppSettingsDockOrder.VoiceSubtitlesEnglishFontScale,
        "English Size",
        "英文字号",
        "1x>1.25x>1.5x>1.75x>2x>2.25x>2.5x>1x",
        "false>true>true>true>true>true>true>false"
    )]
    [InlineData(
        "VoiceSubtitlesChineseFontScale",
        BppSettingsDockOrder.VoiceSubtitlesChineseFontScale,
        "Chinese Size",
        "中文字号",
        "1x>1.25x>1.5x>1.75x>2x>2.25x>2.5x>1x",
        "false>true>true>true>true>true>true>false"
    )]
    [InlineData(
        "BazaarDbUpload",
        BppSettingsDockOrder.BazaarDbUpload,
        "BazaarDB upload",
        "BazaarDB 数据共建",
        "OFF>ON>OFF",
        "false>true>false"
    )]
    public void CyclingSettingsDockEntries_preserve_contracts(
        string key,
        int expectedOrder,
        string expectedEnglishLabel,
        string expectedChineseLabel,
        string expectedStatusSequence,
        string expectedHighlightSequence
    )
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-cycling-dock-contract-{key}-{Guid.NewGuid():N}.cfg"
        );
        try
        {
            L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
            var configFile = new ConfigFile(configPath, saveOnInit: false);
            var config = new BppConfig();
            config.Initialize(configFile);

            var entry = CreateCyclingEntry(key);
            var definition = entry.Build(config);
            var expectedStatuses = expectedStatusSequence.Split('>');
            var expectedHighlights = expectedHighlightSequence
                .Split('>')
                .Select(bool.Parse)
                .ToArray();
            var actualStatuses = new List<string>();
            var actualHighlights = new List<bool>();

            for (var i = 0; i < expectedStatuses.Length; i++)
            {
                actualStatuses.Add(ResolveDisplayStatus(definition));
                actualHighlights.Add(definition.IsActive());
                if (i + 1 < expectedStatuses.Length)
                    SelectNextValue(definition);
            }

            Assert.Equal(expectedOrder, entry.Order);
            Assert.Equal(key, definition.Key);
            L.Install(new TestLanguageProvider("en"), new TestLocaleModeProvider());
            Assert.Equal(expectedEnglishLabel, definition.ResolveLabel("en"));
            L.Install(new TestLanguageProvider("zh-CN"), new TestLocaleModeProvider());
            Assert.Equal(expectedChineseLabel, definition.ResolveLabel("zh-CN"));
            Assert.Equal(expectedStatuses, actualStatuses);
            Assert.Equal(expectedHighlights, actualHighlights);
            Assert.False(definition.CollapseAfterActivate);
        }
        finally
        {
            if (File.Exists(configPath))
                File.Delete(configPath);
        }
    }

    private static string ResolveDisplayStatus(BppSettingsDockDefinition definition)
    {
        if (definition.ControlKind == BppSettingsControlKind.Toggle)
            return definition.ReadToggle!() ? "ON" : "OFF";

        var state = definition.ResolveChoiceState!("en");
        return state.Options[state.SelectedIndex];
    }

    private static void SelectNextValue(BppSettingsDockDefinition definition)
    {
        if (definition.ControlKind == BppSettingsControlKind.Toggle)
        {
            definition.WriteToggle!(!definition.ReadToggle!());
            return;
        }

        var state = definition.ResolveChoiceState!("en");
        var standardCount = state.Options.Count - (state.HasSyntheticCurrentOption ? 1 : 0);
        var currentStandardIndex = state.HasSyntheticCurrentOption ? -1 : state.SelectedIndex;
        definition.SelectStandardChoice!((currentStandardIndex + 1) % standardCount);
    }

    private static ISettingsDockEntry CreateCyclingEntry(string key) =>
        key switch
        {
            "NameOverride" => NameOverrideSettingsDockEntry.Create(() => { }),
            "LegendaryPositionDisplay" => LegendaryPositionSettingsDockEntry.Create(() => { }),
            "EnchantPreview" => ItemEnchantPreviewSettingsDockEntry.Create(),
            "UpgradePreviewActivation" => UpgradePreviewActivationSettingsDockEntry.Create(),
            "EventPreview" => EventPreviewSettingsDockEntry.Create(),
            "QuestPreview" => QuestPreviewSettingsDockEntry.Create(() => { }),
            "BilingualItemNames" => BilingualItemNamesSettingsDockEntry.Create(),
            "ChineseLocaleMode" => ChineseLocaleModeSettingsDockEntry.Create(
                new InMemoryBppEventBus()
            ),
            "StreamMode" => FixedSupporterListSettingsDockEntry.Create(),
            "VoiceSubtitles" => VoiceSubtitlesSettingsDockEntry.Create(),
            "VoiceSubtitlesPosition" => VoiceSubtitlesPositionSettingsDockEntry.Create(),
            "VoiceSubtitlesEnglishFontScale" =>
                VoiceSubtitlesEnglishFontScaleSettingsDockEntry.Create(),
            "VoiceSubtitlesChineseFontScale" =>
                VoiceSubtitlesChineseFontScaleSettingsDockEntry.Create(),
            "BazaarDbUpload" => BazaarDbBundleSettingsDockEntry.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };

    [Fact]
    public void SettingsDockCatalog_sorts_partial_registry_by_global_semantic_order()
    {
        L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
        var registry = new SettingsDockEntryRegistry();
        registry.Register(BazaarDbBundleSettingsDockEntry.Create());
        registry.Register(FixedSupporterListSettingsDockEntry.Create());
        VoiceSubtitlesSettingsDockEntry.RegisterAll(registry);
        registry.Register(new EndOfRunScreenshotSettingsDockEntry());
        registry.Register(new HistoryPanelSettingsDockEntry());
        registry.Register(ChineseLocaleModeSettingsDockEntry.Create(new InMemoryBppEventBus()));

        try
        {
            BppSettingsDockCatalog.Install(new BppConfig(), registry);

            Assert.Equal(
                new[]
                {
                    "StreamMode",
                    "EndOfRunScreenshot",
                    "BazaarDbUpload",
                    "ChineseLocaleMode",
                    "VoiceSubtitles",
                    "VoiceSubtitlesPosition",
                    "VoiceSubtitlesEnglishFontScale",
                    "VoiceSubtitlesChineseFontScale",
                    "GameHistory",
                },
                BppSettingsDockCatalog.Definitions.Select(d => d.Key)
            );
        }
        finally
        {
            BppSettingsDockCatalog.Reset();
        }
    }

    [Theory]
    [InlineData(0, "zh-CN", "按键显示")]
    [InlineData(1, "zh-CN", "智能切换")]
    [InlineData(2, "zh-CN", "常驻显示")]
    [InlineData(0, "en", "OFF")]
    [InlineData(1, "en", "AUTO")]
    [InlineData(2, "en", "ON")]
    public void ResolvePreviewVisibilityModeStatus_returns_localized_dock_status(
        int modeValue,
        string languageCode,
        string expected
    )
    {
        var mode = (PreviewVisibilityMode)modeValue;
        var result = BppSettingsDockCatalog.ResolvePreviewVisibilityModeStatus(mode, languageCode);

        Assert.Equal(expected, result);
    }
}
