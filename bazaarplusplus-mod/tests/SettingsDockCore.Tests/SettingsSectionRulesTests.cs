#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.ChineseTranslations;
using BazaarPlusPlus.Game.Settings;
using Xunit;

namespace SettingsDockCore.Tests;

/// <summary>
/// Core's rules for the BazaarPlusPlus section of the game's settings dialog, which both hosts
/// call: the row order, each row's state, change detection, the dropdown index and the action
/// tap. Each group pins what the two hosts decided for themselves before the rules moved here
/// (the Desktop Host's <c>BppNativeSettingsSectionController</c> and the Android Host's
/// <c>AndroidNativeSettingsSection</c>).
/// </summary>
public sealed class SettingsSectionRulesTests
{
    // BuildSection in both hosts: one pass per control kind (toggle, choice, action), each in
    // catalog order.
    [Fact]
    public void Rows_are_ordered_by_control_kind_and_keep_catalog_order_inside_a_kind()
    {
        var rows = new[]
        {
            Action("A1"),
            Toggle("T1"),
            Choice("C1"),
            Toggle("T2"),
            Action("A2"),
            Choice("C2"),
            Toggle("T3"),
        };

        Assert.Equal(
            new[] { "T1", "T2", "T3", "C1", "C2", "A1", "A2" },
            BppSettingsSectionRules.BuildOrder(rows).Select(row => row.Key)
        );
    }

    [Fact]
    public void An_empty_catalog_orders_no_rows() =>
        Assert.Empty(BppSettingsSectionRules.BuildOrder(Array.Empty<BppSettingsDockDefinition>()));

    // The dropdown listener: desktop ConfigureRow (Choice case), Android SelectChoice.
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    public void A_synthetic_current_option_shifts_the_others_by_one(int shown, int standard) =>
        Assert.Equal(standard, BppSettingsSectionRules.StandardIndex(Synthetic(), shown));

    [Fact]
    public void Picking_the_synthetic_current_option_is_ignored() =>
        Assert.Null(BppSettingsSectionRules.StandardIndex(Synthetic(), 0));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Without_a_synthetic_option_the_shown_index_is_the_standard_index(int shown) =>
        Assert.Equal(
            shown,
            BppSettingsSectionRules.StandardIndex(
                new BppSettingsChoiceState(new[] { "a", "b", "c" }, 0, false),
                shown
            )
        );

    [Fact]
    public void A_pick_on_an_unresolved_choice_is_ignored() =>
        Assert.Null(BppSettingsSectionRules.StandardIndex(null, 1));

    // Neither host checks the range: an index past the options reaches the row, and the row's
    // own SelectStandardChoice ignores it (CyclingSettingsDockEntry).
    [Fact]
    public void An_out_of_range_shown_index_reaches_the_row_which_ignores_it()
    {
        var value = 2;
        var row = new CyclingSettingsDockEntry<int>(
            1,
            "Choice",
            _ => "Choice",
            new[] { 1, 2, 3 },
            _ => value,
            (_, next) => value = next,
            _ => true,
            (next, _) => next.ToString()
        ).Build(Settings());
        var state = row.ResolveChoiceState!("en");

        var standard = BppSettingsSectionRules.StandardIndex(state, 7);

        Assert.Equal(7, standard);
        row.SelectStandardChoice!(standard!.Value);
        Assert.Equal(2, value);
    }

    // ApplyRow, Toggle case, in both hosts: on when the read says true, interactable unless the
    // row says false.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_toggle_shows_its_value_and_is_interactable_by_default(bool on)
    {
        var state = BppSettingsSectionRules.Resolve(Toggle("T", read: () => on), "en");

        Assert.Equal(BppSettingsControlKind.Toggle, state.Kind);
        Assert.Equal(on, state.ToggleOn);
        Assert.True(state.Interactable);
        Assert.True(state.Resolved);
        Assert.Empty(state.Options);
    }

    // The end-of-run screenshot toggle is forced on while another setting needs it.
    [Fact]
    public void A_forced_on_toggle_is_on_and_not_interactable()
    {
        var state = BppSettingsSectionRules.Resolve(
            Toggle("T", read: () => true, isInteractable: () => false),
            "en"
        );

        Assert.True(state.ToggleOn);
        Assert.False(state.Interactable);
    }

    // ApplyRow, Choice case, in both hosts: the dropdown is always interactable, whatever the
    // row's highlight says.
    [Fact]
    public void A_choice_is_interactable_even_when_its_row_is_not_active()
    {
        var state = BppSettingsSectionRules.Resolve(
            Choice(
                "C",
                () => new BppSettingsChoiceState(new[] { "a", "b" }, 1, false),
                () => false
            ),
            "en"
        );

        Assert.Equal(BppSettingsControlKind.Choice, state.Kind);
        Assert.True(state.Resolved);
        Assert.True(state.Interactable);
        Assert.Equal(new[] { "a", "b" }, state.Options);
        Assert.Equal(1, state.SelectedIndex);
        Assert.False(state.ToggleOn);
    }

    // ApplyRow, Choice case: a null choice state leaves the dropdown as it was; the label is
    // written before the switch, so it still resolves.
    [Fact]
    public void An_unresolved_choice_leaves_its_control_alone_but_keeps_its_label()
    {
        var state = BppSettingsSectionRules.Resolve(Choice("C", () => null!), "en");

        Assert.False(state.Resolved);
        Assert.Equal("C label", state.Label);
        Assert.Empty(state.Options);
    }

    [Theory]
    [InlineData(5, 3, 2)]
    [InlineData(-1, 3, 0)]
    [InlineData(1, 3, 1)]
    [InlineData(3, 0, 0)]
    public void The_selected_index_is_clamped_to_the_options(int selected, int count, int shown)
    {
        var options = Enumerable.Range(0, count).Select(index => $"o{index}").ToArray();
        var state = BppSettingsSectionRules.Resolve(
            Choice("C", () => new BppSettingsChoiceState(options, selected, false)),
            "en"
        );

        Assert.Equal(shown, state.SelectedIndex);
    }

    // ApplyRow, Action case: the button is interactable while the row is active. ActivateAction
    // (desktop) and Activate (Android): an inactive row ignores the tap; an active one closes the
    // dialog first when the row asks for it, then runs.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_action_is_interactable_while_its_row_is_active(bool active) =>
        Assert.Equal(
            active,
            BppSettingsSectionRules.Resolve(Action("A", isActive: () => active), "en").Interactable
        );

    [Theory]
    [InlineData(false, false, BppSettingsActionStep.Ignore)]
    [InlineData(false, true, BppSettingsActionStep.Ignore)]
    [InlineData(true, false, BppSettingsActionStep.Run)]
    [InlineData(true, true, BppSettingsActionStep.CloseThenRun)]
    public void An_action_tap_is_ignored_while_inactive_and_closes_first_when_asked(
        bool active,
        bool collapse,
        BppSettingsActionStep step
    ) =>
        Assert.Equal(
            step,
            BppSettingsSectionRules.OnActionTap(
                Action("A", isActive: () => active, collapse: collapse)
            )
        );

    // RefreshView (desktop) and Refresh (Android): the presentation hash covered every row, the
    // invisible ones too, and only a visible row was then written.
    [Fact]
    public void An_invisible_row_still_takes_part_in_change_detection()
    {
        var label = "before";
        var row = Toggle("T", label: _ => label, isVisible: _ => false);
        var before = new[] { BppSettingsSectionRules.Resolve(row, "en") };
        label = "after";
        var after = new[] { BppSettingsSectionRules.Resolve(row, "en") };

        Assert.False(before[0].Visible);
        Assert.Equal("after", after[0].Label);
        Assert.False(BppSettingsSectionRules.SameState(before, after));
    }

    [Fact]
    public void States_with_equal_fields_and_equal_option_content_are_the_same()
    {
        var a = new[] { Base(), Base() with { Key = "Other" } };
        var b = new[]
        {
            Base() with
            {
                Options = new List<string> { "x", "y" },
            },
            Base() with
            {
                Key = "Other",
            },
        };

        Assert.True(BppSettingsSectionRules.SameState(a, b));
    }

    public static TheoryData<string> Changes() =>
        new()
        {
            "key",
            "kind",
            "visible",
            "label",
            "toggle_on",
            "interactable",
            "option_text",
            "option_count",
            "selected",
            "resolved",
        };

    [Theory]
    [MemberData(nameof(Changes))]
    public void Any_changed_field_or_option_is_a_change(string field)
    {
        var changed = field switch
        {
            "key" => Base() with { Key = "Other" },
            "kind" => Base() with { Kind = BppSettingsControlKind.Toggle },
            "visible" => Base() with { Visible = false },
            "label" => Base() with { Label = "Other" },
            "toggle_on" => Base() with { ToggleOn = true },
            "interactable" => Base() with { Interactable = false },
            "option_text" => Base() with { Options = new[] { "x", "z" } },
            "option_count" => Base() with { Options = new[] { "x", "y", "z" } },
            "selected" => Base() with { SelectedIndex = 0 },
            "resolved" => Base() with { Resolved = false },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Assert.False(BppSettingsSectionRules.SameState(new[] { Base() }, new[] { changed }));
    }

    [Fact]
    public void A_different_row_count_is_a_change() =>
        Assert.False(BppSettingsSectionRules.SameState(new[] { Base() }, new[] { Base(), Base() }));

    // The hash re-read the option text on every refresh, so a definition that changes one shared
    // list in place still counts as a change.
    [Fact]
    public void Options_changed_in_place_by_the_definition_are_a_change()
    {
        var options = new List<string> { "a", "b" };
        var row = Choice("C", () => new BppSettingsChoiceState(options, 0, false));
        var before = new[] { BppSettingsSectionRules.Resolve(row, "en") };
        options[1] = "c";
        var after = new[] { BppSettingsSectionRules.Resolve(row, "en") };

        Assert.False(BppSettingsSectionRules.SameState(before, after));
    }

    // Each host keeps its own failure handling: Android drops the section in Tick, the desktop
    // does not catch. Core passes the exception on.
    [Fact]
    public void A_throwing_delegate_propagates()
    {
        var failure = new InvalidOperationException("read failed");
        var row = Toggle("T", read: () => throw failure);

        Assert.Same(
            failure,
            Assert.Throws<InvalidOperationException>(() =>
                BppSettingsSectionRules.Resolve(row, "en")
            )
        );
    }

    // The hash and then ApplyRow each called a row's delegates, so every refresh called them
    // twice; a toggle's IsActive is its ReadToggle. One resolve calls each once.
    [Fact]
    public void One_resolve_calls_each_delegate_of_a_row_once()
    {
        var calls = new Dictionary<string, int>();
        Func<T> Count<T>(string name, T value) =>
            () =>
            {
                calls[name] = calls.GetValueOrDefault(name) + 1;
                return value;
            };
        var label = Count("label", "L");
        var visible = Count("visible", true);
        var toggle = BppSettingsDockDefinition.Toggle(
            "T",
            _ => label(),
            Count("read", true),
            _ => { },
            Count("interactable", true),
            _ => visible()
        );
        var choice = BppSettingsDockDefinition.Choice(
            "C",
            _ => "C",
            Count("choice_active", true),
            _ => Count("choice_state", new BppSettingsChoiceState(new[] { "a" }, 0, false))(),
            _ => { }
        );
        var action = new BppSettingsDockDefinition(
            "A",
            _ => "A",
            Count("action_active", true),
            () => { },
            false
        );

        BppSettingsSectionRules.Resolve(toggle, "en");
        BppSettingsSectionRules.Resolve(choice, "en");
        BppSettingsSectionRules.Resolve(action, "en");

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["label"] = 1,
                ["visible"] = 1,
                ["read"] = 1,
                ["interactable"] = 1,
                ["choice_state"] = 1,
                ["action_active"] = 1,
            },
            calls
        );
    }

    // ChineseTranslationOverrides.AppliesTo: the game's own zh-CN, not BPP's Chinese mode.
    [Theory]
    [InlineData("zh-CN", true)]
    [InlineData("zh-cn", true)]
    [InlineData("en", false)]
    [InlineData("zh-Hans", false)]
    [InlineData("zh-TW", false)]
    [InlineData("", false)]
    public void Chinese_translation_improvements_show_only_in_zh_cn(string language, bool visible)
    {
        var row = ChineseTranslationSettingsDockEntry.Create().Build(Settings());

        var state = BppSettingsSectionRules.Resolve(row, language);

        Assert.Equal(visible, state.Visible);
        Assert.Equal("ChineseTranslationImprovements", state.Key);
    }

    [Fact]
    public void The_label_resolves_in_the_language_the_host_passes()
    {
        var row = Toggle("T", label: language => $"label:{language}");

        Assert.Equal("label:de", BppSettingsSectionRules.Resolve(row, "de").Label);
    }

    private static BppSettingsRowState Base() =>
        new(
            "Key",
            BppSettingsControlKind.Choice,
            Visible: true,
            Label: "Label",
            ToggleOn: false,
            Interactable: true,
            Options: new[] { "x", "y" },
            SelectedIndex: 1,
            Resolved: true
        );

    private static BppSettingsChoiceState Synthetic() =>
        new(new[] { "current", "a", "b", "c" }, 0, true);

    private static BppSettingsDockDefinition Toggle(
        string key,
        Func<bool>? read = null,
        Func<bool>? isInteractable = null,
        Func<string, string>? label = null,
        Func<string, bool>? isVisible = null
    ) =>
        BppSettingsDockDefinition.Toggle(
            key,
            label ?? (_ => $"{key} label"),
            read ?? (() => false),
            _ => { },
            isInteractable,
            isVisible
        );

    private static BppSettingsDockDefinition Choice(
        string key,
        Func<BppSettingsChoiceState>? state = null,
        Func<bool>? isActive = null
    ) =>
        BppSettingsDockDefinition.Choice(
            key,
            _ => $"{key} label",
            isActive ?? (() => true),
            _ => (state ?? (() => new BppSettingsChoiceState(new[] { "a" }, 0, false)))(),
            _ => { }
        );

    private static BppSettingsDockDefinition Action(
        string key,
        Func<bool>? isActive = null,
        bool collapse = false
    ) => new(key, _ => $"{key} label", isActive ?? (() => true), () => { }, collapse);

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
