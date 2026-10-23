#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.Settings;
using Xunit;

namespace SettingsDockCore.Tests;

/// <summary>
/// The settings dock's shared rows in Core, which both hosts register: the catalog's order, a
/// cycling row's toggle and choice forms, and the rows the Android host takes from the desktop.
/// </summary>
public sealed class SettingsDockRowTests
{
    [Fact]
    public void The_catalog_installs_rows_in_dock_order()
    {
        var registry = new SettingsDockEntryRegistry();
        registry.Register(new HistoryPanelSettingsDockEntry(null, () => true, () => { }));
        registry.Register(ItemEnchantPreviewSettingsDockEntry.Create());
        try
        {
            BppSettingsDockCatalog.Install(Settings(), registry);

            Assert.Equal(
                new[] { "EnchantPreview", "GameHistory" },
                BppSettingsDockCatalog.Definitions.Select(row => row.Key)
            );
        }
        finally
        {
            BppSettingsDockCatalog.Reset();
        }
    }

    [Fact]
    public void A_toggle_row_writes_and_then_reports_the_change()
    {
        var value = false;
        var changes = new List<bool>();
        var row = CyclingSettingsDockEntry<bool>
            .Toggle(3, "Switch", _ => "Switch", _ => value, (_, next) => value = next, changes.Add)
            .Build(Settings());

        Assert.Equal(BppSettingsControlKind.Toggle, row.ControlKind);
        row.WriteToggle!(true);

        Assert.True(value);
        Assert.True(row.ReadToggle!());
        Assert.Equal(new[] { true }, changes);
    }

    [Fact]
    public void A_choice_row_keeps_an_off_ladder_value_as_its_first_option()
    {
        var value = 5;
        var row = new CyclingSettingsDockEntry<int>(
            1,
            "Choice",
            _ => "Choice",
            new[] { 1, 2, 3 },
            _ => value,
            (_, next) => value = next,
            next => next > 1,
            (next, _) => next.ToString()
        ).Build(Settings());

        var state = row.ResolveChoiceState!("en");
        Assert.Equal(new[] { "5", "1", "2", "3" }, state.Options);
        Assert.Equal(0, state.SelectedIndex);
        Assert.True(state.HasSyntheticCurrentOption);

        row.SelectStandardChoice!(1);
        Assert.Equal(2, value);
        row.SelectStandardChoice(7);
        Assert.Equal(2, value);
        Assert.Equal(new[] { "1", "2", "3" }, row.ResolveChoiceState("en").Options);
        Assert.Equal(1, row.ResolveChoiceState("en").SelectedIndex);
    }

    [Fact]
    public void Enchant_preview_offers_off_auto_and_on()
    {
        var settings = Settings();
        var modes = new List<PreviewVisibilityMode>();
        var row = ItemEnchantPreviewSettingsDockEntry.Create(modes.Add).Build(settings);

        Assert.Equal(new[] { "OFF", "AUTO", "ON" }, row.ResolveChoiceState!("en").Options);
        row.SelectStandardChoice!(0);

        Assert.Equal(PreviewVisibilityMode.Off, settings.EnchantPreviewModeConfig.Value);
        Assert.Equal(new[] { PreviewVisibilityMode.Off }, modes);
        Assert.False(row.IsActive());
    }

    [Fact]
    public void Game_history_names_its_hotkey_only_where_there_is_one()
    {
        var opened = 0;
        var entry = new HistoryPanelSettingsDockEntry(null, () => true, () => opened++);
        var plain = entry.Build(Settings());
        var hotkey = new HistoryPanelSettingsDockEntry(() => "F2", () => true, () => { }).Build(
            Settings()
        );

        Assert.Equal(BppSettingsDockOrder.GameHistory, entry.Order);
        Assert.Equal("Game History", plain.ResolveLabel("en"));
        Assert.Equal(HistoryPanelSettingsMenuLabel.ResolvePlain("en"), plain.ResolveLabel("en"));
        Assert.Contains("F2", hotkey.ResolveLabel("en"));
        Assert.DoesNotContain("F2", plain.ResolveLabel("en"));
        Assert.True(plain.CollapseAfterActivate);
        plain.Activate!();
        Assert.Equal(1, opened);
    }

    private static BppSettings Settings() => new(new MemoryStore());

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
