using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Localization;
using Xunit;

namespace EncounterPreview.Tests;

/// <summary>
/// Core's Encounter Preview Module over a host's reads: the switch and combat gates, the routing
/// by card type, and that each host read reaches the text.
/// </summary>
public sealed class EncounterPreviewModuleTests : IDisposable
{
    private static readonly Guid EventId = Guid.Parse("82000000-0000-0000-0000-000000000001");
    private static readonly Guid StepId = Guid.Parse("82000000-0000-0000-0000-000000000002");
    private static readonly Guid MerchantId = Guid.Parse("82000000-0000-0000-0000-000000000003");
    private static readonly Guid DayMerchantId = Guid.Parse("82000000-0000-0000-0000-000000000004");
    private static readonly Guid ClosedMerchantId = Guid.Parse(
        "82000000-0000-0000-0000-000000000005"
    );
    private static readonly Guid EmptyEventId = Guid.Parse("82000000-0000-0000-0000-000000000006");

    public EncounterPreviewModuleTests()
    {
        L.Install(() => "en", () => BppChineseLocaleMode.Mainland);
    }

    public void Dispose() => L.Reset();

    [Fact]
    public void The_switch_and_a_fight_leave_the_native_tooltip_before_any_plan_is_read()
    {
        var reads = new FakeReads();
        var (module, settings) = Module(reads);

        settings.EnableDetailedPreviewConfig.Value = false;
        Assert.Equal(EncounterPreviewAvailability.Unsupported, Event(module, EventId).Availability);
        Assert.Equal(
            EncounterPreviewAvailability.Unsupported,
            module.ResolveLevelUp(new LevelUpPreviewQuery(2)).Availability
        );
        Assert.False(module.Enabled);

        settings.EnableDetailedPreviewConfig.Value = true;
        reads.IsInCombat = true;
        Assert.Equal(EncounterPreviewAvailability.Unsupported, Event(module, EventId).Availability);
        Assert.Equal(0, reads.PlanReads);
    }

    [Theory]
    [InlineData(EncounterPreviewAvailability.Loading)]
    [InlineData(EncounterPreviewAvailability.Unavailable)]
    public void No_plans_yet_says_why(EncounterPreviewAvailability why)
    {
        var reads = new FakeReads { Unavailable = why, HasPlans = false };

        Assert.Equal(why, Event(Module(reads).Module, EventId).Availability);
    }

    [Fact]
    public void Only_events_and_steps_with_native_text_get_a_preview()
    {
        var module = Module(new FakeReads()).Module;

        Assert.Equal(
            EncounterPreviewAvailability.Unsupported,
            module.ResolveCard(new(EventId, CardType.EventEncounter, "")).Availability
        );
        Assert.Equal(
            EncounterPreviewAvailability.Unsupported,
            module.ResolveCard(new(StepId, CardType.Item, "native")).Availability
        );
        Assert.Equal(EncounterPreviewAvailability.Missing, Event(module, Guid.Empty).Availability);
        Assert.Equal(
            EncounterPreviewAvailability.Missing,
            Event(module, Guid.NewGuid()).Availability
        );
        Assert.Equal(
            EncounterPreviewAvailability.Unsupported,
            Event(module, EmptyEventId).Availability
        );
        Assert.Equal(
            EncounterPreviewAvailability.Unsupported,
            module.ResolveCard(new(EventId, CardType.EncounterStep, "native")).Availability
        );
    }

    [Fact]
    public void A_merchant_shows_its_pinned_tier_or_the_days_odds()
    {
        var module = Module(new FakeReads()).Module;

        var pinned = Event(module, MerchantId);
        var byDay = Event(module, DayMerchantId);

        Assert.Equal(EncounterPreviewAvailability.Available, pinned.Availability);
        Assert.Contains(">Gold</color>", pinned.Content);
        Assert.DoesNotContain("%", pinned.Content);
        Assert.Contains("Bronze 75%", byDay.Content);
        Assert.Equal(
            EncounterPreviewAvailability.Unsupported,
            Event(module, ClosedMerchantId).Availability
        );
    }

    [Fact]
    public void An_event_shows_its_choices_as_the_host_presents_and_colours_them()
    {
        var reads = new FakeReads { CurrentHero = CardHero.Jules };

        var result = Event(Module(reads).Module, EventId);

        Assert.Equal(EncounterPreviewAvailability.Available, result.Availability);
        Assert.Contains("Presented Step", result.Content);
        Assert.Contains("Gain 7 Gold.", result.Content);
        Assert.Contains(StepId, reads.Presented);
        Assert.True(reads.Coloured > 0);
    }

    [Fact]
    public void A_step_shows_the_days_odds_of_the_reward_the_table_decides()
    {
        var result = Module(new FakeReads())
            .Module.ResolveCard(new(StepId, CardType.EncounterStep, "Get an item"));

        Assert.Equal(EncounterPreviewAvailability.Available, result.Availability);
        Assert.Contains("Bronze 75%", result.Content);
    }

    [Fact]
    public void A_level_up_lists_the_rewards_of_the_queried_hero_else_the_current_one()
    {
        var reads = new FakeReads { CurrentHero = CardHero.Vanessa };
        var module = Module(reads).Module;

        var forJules = module.ResolveLevelUp(new LevelUpPreviewQuery(2, CardHero.Jules));
        var forCurrent = module.ResolveLevelUp(new LevelUpPreviewQuery(2));

        Assert.Contains("Presented Step", forJules.Content);
        Assert.DoesNotContain("Presented Step", forCurrent.Content);
        Assert.Contains("150 Max Health", forCurrent.Content);
    }

    [Fact]
    public void A_failing_read_is_unavailable()
    {
        var reads = new FakeReads { ThrowOnDayTiers = true };

        Assert.Equal(
            EncounterPreviewAvailability.Unavailable,
            Event(Module(reads).Module, EventId).Availability
        );
    }

    private static EncounterPreviewResult Event(EncounterPreviewModule module, Guid id) =>
        module.ResolveCard(new(id, CardType.EventEncounter, "native"));

    private static (EncounterPreviewModule Module, BppSettings Settings) Module(FakeReads reads)
    {
        var settings = new BppSettings(new MemorySettings());
        return (new EncounterPreviewModule(settings, reads), settings);
    }

    private static EncounterPreviewSnapshot Snapshot()
    {
        var reward = new EncounterRewardFilter(
            CardType.Item,
            quantity: 1,
            fromAnyHero: false,
            Array.Empty<CardSize>(),
            new[] { CardTier.Bronze, CardTier.Silver, CardTier.Gold, CardTier.Diamond },
            Array.Empty<CardTag>(),
            Array.Empty<CardHiddenTag>(),
            "Item",
            usesDayTierTable: true,
            usesDayTierDistribution: true
        );
        EncounterPreviewTemplatePlan Template(
            Guid id,
            EncounterPreviewTemplateKind kind,
            string description,
            EncounterRewardFilter? filter = null
        ) =>
            new(
                id,
                kind,
                kind == EncounterPreviewTemplateKind.EncounterStep
                    ? new[] { CardHero.Jules }
                    : Array.Empty<CardHero>(),
                "Template",
                new(null, "Template"),
                new(null, description),
                null,
                filter
            );
        EncounterPreviewEventPlan Merchant(Guid id, EncounterMerchantTierRule rule) =>
            new(id, isRandomSelectionEvent: true, rule, 3, null, null);

        return new EncounterPreviewSnapshot(
            new[]
            {
                new EncounterPreviewEventPlan(
                    EventId,
                    isRandomSelectionEvent: false,
                    merchant: null,
                    choiceLimit: 1,
                    outcomeGroups: null,
                    choiceGroups: new[]
                    {
                        new EncounterChoiceGroupData(
                            isRandomPool: false,
                            new[] { new EncounterStepReference(StepId) }
                        ),
                    }
                ),
                new EncounterPreviewEventPlan(EmptyEventId, false, null, null, null, null),
                Merchant(MerchantId, new EncounterMerchantTierRule(CardTier.Gold, false)),
                Merchant(DayMerchantId, EncounterMerchantTierRule.DayDistribution),
                Merchant(ClosedMerchantId, new EncounterMerchantTierRule(null, false)),
            },
            new[]
            {
                Template(EventId, EncounterPreviewTemplateKind.Event, "Pick one"),
                Template(EmptyEventId, EncounterPreviewTemplateKind.Event, "Nothing"),
                Template(
                    StepId,
                    EncounterPreviewTemplateKind.EncounterStep,
                    "Gain {ability.0} Gold.",
                    reward
                ),
            },
            new[]
            {
                new LevelUpPreviewPlan(
                    2,
                    150,
                    isRandomSelection: false,
                    new[]
                    {
                        new LevelUpPreviewGroup(
                            0,
                            1,
                            new[] { StepId },
                            new[]
                            {
                                new LevelUpPreviewHeroCondition(new[] { CardHero.Jules }, "Any"),
                            }
                        ),
                    }
                ),
            }
        );
    }

    private sealed class FakeReads : IEncounterPreviewReads
    {
        private readonly EncounterPreviewSnapshot _snapshot = Snapshot();

        public bool IsInCombat { get; set; }
        public bool HasPlans { get; init; } = true;
        public EncounterPreviewAvailability Unavailable { get; init; }
        public bool ThrowOnDayTiers { get; init; }
        public CardHero? CurrentHero { get; init; }
        public int PlanReads { get; private set; }
        public int Coloured { get; private set; }
        public List<Guid> Presented { get; } = new();

        public IEncounterPreviewPlans? TryGetPlans(out EncounterPreviewAvailability unavailable)
        {
            PlanReads++;
            unavailable = Unavailable;
            return HasPlans ? new Plans(_snapshot, ThrowOnDayTiers) : null;
        }

        public CardHero? ReadCurrentHero() => CurrentHero;

        public EncounterInventory? ReadInventory() => new(Array.Empty<EncounterInventoryCard>());

        public string ColorKeywords(string text)
        {
            Coloured++;
            return text;
        }

        public EncounterPreviewTemplatePlan Present(EncounterPreviewTemplatePlan template)
        {
            Presented.Add(template.TemplateId);
            return template.Kind == EncounterPreviewTemplateKind.EncounterStep
                ? new(
                    template.TemplateId,
                    template.Kind,
                    template.Heroes,
                    template.InternalName,
                    new(null, "Presented Step"),
                    template.Description,
                    null,
                    template.RewardFilter
                )
                : template;
        }
    }

    private sealed class Plans(EncounterPreviewSnapshot snapshot, bool throwOnDayTiers)
        : IEncounterPreviewPlans
    {
        public EncounterPreviewSnapshot Snapshot => snapshot;

        public (int? Day, GameDataDayTierTable? Table) ReadDayTiers() =>
            throwOnDayTiers
                ? throw new InvalidOperationException("day tiers unavailable")
                : (3, GameDataDayTierTable.FromWeights(0.75f, 0.25f, 0f, 0f));

        public LiveAbilityValueResolver? LiveValues =>
            (Guid _, string effectId, bool isAura, out string valueText, out string? unit) =>
            {
                valueText = effectId == "0" && !isAura ? "7" : string.Empty;
                unit = null;
                return valueText.Length > 0;
            };
    }

    private sealed class MemorySettings : IBppSettingsStore
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
