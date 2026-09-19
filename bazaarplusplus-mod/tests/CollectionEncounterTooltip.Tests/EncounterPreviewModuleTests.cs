using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Event;
using BazaarGameShared.Domain.Cards.Encounter.Step;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Game;
using BazaarGameShared.Domain.Runs;
using BazaarGameShared.Domain.Spawning.SpawnFilters;
using BazaarGameShared.Domain.Spawning.SpawnGroups;
using BazaarGameShared.Domain.Spawning.SpawningContexts;
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Domain.Values.ReferenceValues;
using BazaarPlusPlus.GameInterop.DayTiers;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Localization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class EncounterPreviewModuleTests : IDisposable
{
    private static readonly Guid EventId = Guid.Parse("91000000-0000-0000-0000-000000000001");
    private static readonly Guid StepId = Guid.Parse("91000000-0000-0000-0000-000000000002");
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "bpp-event-preview-module-tests",
        Guid.NewGuid().ToString("N")
    );

    public EncounterPreviewModuleTests()
    {
        L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
    }

    [Fact]
    public void Typed_queries_return_final_event_and_level_presentations()
    {
        var source = new object();
        var runtime = new FakeRuntime(source)
        {
            CurrentHero = EHero.Jules,
            Inventory = new EncounterInventory(Array.Empty<EncounterInventoryCard>()),
        };
        var registry = PublishedRegistry(source, Snapshot());
        using var module = Module(runtime, registry);

        var eventResult = module.ResolveEvent(new EventPreviewQuery(EventId, "native"));
        var levelResult = module.ResolveLevelUp(new LevelUpPreviewQuery(2));

        Assert.Equal(EventPreviewAvailability.Available, eventResult.Availability);
        Assert.Contains("Jules Step", eventResult.Content);
        Assert.Contains("Take it", eventResult.Content);
        Assert.Equal(EventPreviewAvailability.Available, levelResult.Availability);
        Assert.Contains("150 Max Health", levelResult.Content);
    }

    [Fact]
    public void Dynamic_state_is_read_by_the_module_for_each_query()
    {
        var source = new object();
        var runtime = new FakeRuntime(source)
        {
            CurrentHero = EHero.Vanessa,
            Inventory = new EncounterInventory(Array.Empty<EncounterInventoryCard>()),
        };
        using var module = Module(runtime, PublishedRegistry(source, Snapshot()));

        var unavailable = module.ResolveEvent(new EventPreviewQuery(EventId, "native"));
        runtime.CurrentHero = EHero.Jules;
        var available = module.ResolveEvent(new EventPreviewQuery(EventId, "native"));

        Assert.Equal(EventPreviewAvailability.Unsupported, unavailable.Availability);
        Assert.Equal(EventPreviewAvailability.Available, available.Availability);
    }

    [Fact]
    public void Query_uses_the_snapshot_and_source_from_one_generation()
    {
        var firstSource = new object();
        var secondSource = new object();
        var runtime = new FakeRuntime(firstSource)
        {
            CurrentHero = EHero.Jules,
            Inventory = new EncounterInventory(Array.Empty<EncounterInventoryCard>()),
        };
        runtime.OnResolveDayTiers = () => runtime.Source = secondSource;
        using var module = Module(runtime, PublishedRegistry(firstSource, Snapshot()));

        var result = module.ResolveEvent(new EventPreviewQuery(EventId, "native"));

        Assert.Equal(EventPreviewAvailability.Available, result.Availability);
        Assert.Same(firstSource, runtime.LastDayTierSource);
    }

    [Fact]
    public void Step_query_reads_one_day_tier_result_for_distribution_and_maximum()
    {
        var source = new object();
        var table = GameDataDayTierTable.FromWeights(0.99f, 0f, 0f, 0.01f)!;
        var runtime = new FakeRuntime(source)
        {
            DayTierResolution = GameDataDayTierResolution.Available(day: 7, table),
        };
        using var module = Module(runtime, PublishedRegistry(source, DayTierStepSnapshot()));

        var result = module.ResolveStep(new EncounterStepPreviewQuery(StepId, "Get an item"));

        Assert.Equal(EventPreviewAvailability.Available, result.Availability);
        Assert.Equal(1, runtime.DayTierReadCount);
        Assert.Same(source, runtime.LastDayTierSource);
        Assert.Contains("Bronze 99%", result.Content);
        Assert.Contains("Diamond 1%", result.Content);
        Assert.Equal(ETier.Diamond, runtime.DayTierResolution.MaximumTier);
    }

    [Fact]
    public void Observing_a_new_source_hides_the_old_generation_before_loading()
    {
        var oldSource = new object();
        var newSource = new object();
        var runtime = new FakeRuntime(oldSource)
        {
            CurrentHero = EHero.Jules,
            Inventory = new EncounterInventory(Array.Empty<EncounterInventoryCard>()),
        };
        using var module = Module(runtime, PublishedRegistry(oldSource, Snapshot()));
        Assert.Equal(
            EventPreviewAvailability.Available,
            module.ResolveEvent(new EventPreviewQuery(EventId, "native")).Availability
        );

        runtime.Source = newSource;
        module.ObserveStaticData();

        Assert.Equal(
            EventPreviewAvailability.Unavailable,
            module.ResolveEvent(new EventPreviewQuery(EventId, "native")).Availability
        );
    }

    [Fact]
    public void Loading_missing_unsupported_and_runtime_failure_are_typed_results()
    {
        var source = new object();
        var runtime = new FakeRuntime(source)
        {
            CurrentHero = EHero.Jules,
            Inventory = new EncounterInventory(Array.Empty<EncounterInventoryCard>()),
        };
        var registry = new EncounterPreviewPlanRegistry();
        using var module = Module(runtime, registry);

        Assert.Equal(
            EventPreviewAvailability.Loading,
            module.ResolveEvent(new EventPreviewQuery(EventId, "native")).Availability
        );

        var generation = registry.BeginGeneration(source);
        Assert.True(registry.TryPublish(source, generation, Snapshot()));
        Assert.Equal(
            EventPreviewAvailability.Missing,
            module.ResolveEvent(new EventPreviewQuery(Guid.NewGuid(), "native")).Availability
        );
        Assert.Equal(
            EventPreviewAvailability.Unsupported,
            module.ResolveStep(new EncounterStepPreviewQuery(EventId, "native")).Availability
        );

        runtime.ThrowOnDayTierRead = true;
        Assert.Equal(
            EventPreviewAvailability.Unavailable,
            module.ResolveEvent(new EventPreviewQuery(EventId, "native")).Availability
        );
    }

    [Fact]
    public void Cache_write_failure_publishes_the_snapshot_as_degraded()
    {
        Directory.CreateDirectory(_directory);
        var databasePath = Path.Combine(_directory, "GameData.db");
        File.WriteAllText(databasePath, "identity");
        var source = new object();
        var runtime = new FakeRuntime(source)
        {
            SourceInfo = new BppGameDataSourceInfo(
                databasePath,
                Path.Combine(_directory, "missing-manifest.json"),
                "https://data.example.invalid"
            ),
            CardMap = new Dictionary<Guid, ITCard>(),
            LevelUps = new Dictionary<int, TLevelUp>(),
        };
        using var module = new EncounterPreviewModule(
            runtime,
            new EncounterPreviewPlanRegistry(),
            new EncounterPreviewCacheStore(_directory),
            "test-build",
            "test-channel"
        );

        module.ObserveStaticData();

        Assert.True(
            SpinWait.SpinUntil(
                () => module.Status == EncounterPreviewModuleStatus.Degraded,
                TimeSpan.FromSeconds(5)
            )
        );
        Assert.Equal(
            EventPreviewAvailability.Missing,
            module.ResolveEvent(new EventPreviewQuery(Guid.NewGuid(), "native")).Availability
        );
    }

    [Fact]
    public void Cache_hit_publishes_without_loading_the_card_map()
    {
        Directory.CreateDirectory(_directory);
        var sourceInfo = CreateSourceInfo("cache-hit");
        var cachePath = Path.Combine(_directory, "preview.json");
        var store = new EncounterPreviewCacheStore(cachePath);
        store.Save(
            EncounterPreviewIdentityResolver.Resolve(
                sourceInfo.ManifestPath,
                sourceInfo.DatabasePath,
                sourceInfo.DataBaseUrl,
                "test-build",
                "test-channel"
            ),
            Snapshot()
        );
        var source = new object();
        var runtime = new FakeRuntime(source)
        {
            SourceInfo = sourceInfo,
            CardMapLoader = _ => throw new InvalidOperationException("cache hit loaded map"),
        };
        using var module = new EncounterPreviewModule(
            runtime,
            new EncounterPreviewPlanRegistry(),
            store,
            "test-build",
            "test-channel"
        );

        module.ObserveStaticData();

        Assert.True(WaitForStatus(module, EncounterPreviewModuleStatus.Ready));
        Assert.Equal(0, runtime.CardMapLoadCount);
        Assert.Equal(
            EventPreviewAvailability.Available,
            module.ResolveEvent(new EventPreviewQuery(EventId, "native")).Availability
        );
    }

    [Fact]
    public void Corrupt_cache_rebuilds_and_replaces_the_cache()
    {
        Directory.CreateDirectory(_directory);
        var cachePath = Path.Combine(_directory, "preview.json");
        File.WriteAllText(cachePath, "not-json");
        var sourceInfo = CreateSourceInfo("corrupt-cache");
        var source = new object();
        var runtime = new FakeRuntime(source)
        {
            SourceInfo = sourceInfo,
            CardMap = new Dictionary<Guid, ITCard>(),
            LevelUps = new Dictionary<int, TLevelUp>(),
        };
        var store = new EncounterPreviewCacheStore(cachePath);
        using var module = new EncounterPreviewModule(
            runtime,
            new EncounterPreviewPlanRegistry(),
            store,
            "test-build",
            "test-channel"
        );

        module.ObserveStaticData();

        Assert.True(WaitForStatus(module, EncounterPreviewModuleStatus.Ready));
        Assert.Equal(1, runtime.CardMapLoadCount);
        var identity = EncounterPreviewIdentityResolver.Resolve(
            sourceInfo.ManifestPath,
            sourceInfo.DatabasePath,
            sourceInfo.DataBaseUrl,
            "test-build",
            "test-channel"
        );
        Assert.True(store.TryLoad(identity, out var rebuilt, out _));
        Assert.NotNull(rebuilt);
    }

    [Fact]
    public void Partial_coverage_publishes_with_degraded_status()
    {
        var source = new object();
        var brokenId = Guid.Parse("91000000-0000-0000-0000-000000000099");
        var broken = new BrokenEventCard
        {
            Id = brokenId,
            Type = ECardType.EventEncounter,
            InternalName = "Broken",
        };
        var runtime = new FakeRuntime(source)
        {
            SourceInfo = CreateSourceInfo("partial"),
            CardMap = new Dictionary<Guid, ITCard> { [brokenId] = broken },
            LevelUps = new Dictionary<int, TLevelUp>(),
        };
        using var module = Module(runtime, new EncounterPreviewPlanRegistry());

        module.ObserveStaticData();

        Assert.True(WaitForStatus(module, EncounterPreviewModuleStatus.Degraded));
        Assert.Equal(
            EventPreviewAvailability.Missing,
            module.ResolveEvent(new EventPreviewQuery(brokenId, "native")).Availability
        );
    }

    [Fact]
    public void Superseded_load_cannot_change_the_current_module_status()
    {
        var sourceA = new object();
        var sourceB = new object();
        var pendingA = new TaskCompletionSource<Dictionary<Guid, ITCard>?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var runtime = new FakeRuntime(sourceA)
        {
            SourceInfoResolver = source =>
                CreateSourceInfo(ReferenceEquals(source, sourceA) ? "source-a" : "source-b"),
            CardMapLoader = source =>
                ReferenceEquals(source, sourceA)
                    ? pendingA.Task
                    : Task.FromResult<Dictionary<Guid, ITCard>?>(new()),
            LevelUps = new Dictionary<int, TLevelUp>(),
        };
        using var module = Module(runtime, new EncounterPreviewPlanRegistry());
        module.ObserveStaticData();
        Assert.True(
            SpinWait.SpinUntil(() => runtime.CardMapLoadCount == 1, TimeSpan.FromSeconds(5))
        );

        runtime.Source = sourceB;
        module.ObserveStaticData();
        Assert.True(WaitForStatus(module, EncounterPreviewModuleStatus.Ready));
        pendingA.SetResult(new Dictionary<Guid, ITCard>());

        Assert.False(
            SpinWait.SpinUntil(
                () => module.Status != EncounterPreviewModuleStatus.Ready,
                TimeSpan.FromMilliseconds(250)
            )
        );
    }

    [Fact]
    public void Compiler_and_module_format_fixed_modifier_live_and_unit_placeholders()
    {
        var source = new object();
        var runtime = new FakeRuntime(source)
        {
            CurrentHero = EHero.Jules,
            Inventory = new EncounterInventory(Array.Empty<EncounterInventoryCard>()),
            LiveValueResolver = (_, effectId, isAura) =>
                effectId == "0" && !isAura ? (true, "6", null) : (false, string.Empty, null),
        };
        EventPreviewLocalization.AttributeUnitLocalizer = unit => unit == "Gold" ? "Coins" : null;
        using var module = Module(
            runtime,
            PublishedRegistry(source, CompiledLocalizationSnapshot())
        );

        var result = module.ResolveEvent(new EventPreviewQuery(EventId, "native"));

        Assert.Equal(EventPreviewAvailability.Available, result.Availability);
        Assert.Contains("Gain 10 Coins.", result.Content);
        Assert.Contains("Gain 1 Gold for each Food [6]", result.Content);
        Assert.Contains(
            "Gain 2 Prestige for each Player fight you have lost this run [6]",
            result.Content
        );
        Assert.DoesNotContain("{ability.", result.Content);
        Assert.Contains("Gain 5 Income.", result.Content);
    }

    public void Dispose()
    {
        EventPreviewLocalization.AttributeUnitLocalizer = null;
        L.Reset();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private EncounterPreviewModule Module(
        IEncounterPreviewGameRuntime runtime,
        EncounterPreviewPlanRegistry registry
    )
    {
        Directory.CreateDirectory(_directory);
        return new EncounterPreviewModule(
            runtime,
            registry,
            new EncounterPreviewCacheStore(Path.Combine(_directory, "preview.json")),
            "test-build",
            "test-channel"
        );
    }

    private static EncounterPreviewPlanRegistry PublishedRegistry(
        object source,
        EncounterPreviewSnapshot snapshot
    )
    {
        var registry = new EncounterPreviewPlanRegistry();
        var generation = registry.BeginGeneration(source);
        Assert.True(registry.TryPublish(source, generation, snapshot));
        return registry;
    }

    private static EncounterPreviewSnapshot Snapshot()
    {
        var root = Template(
            EventId,
            EncounterPreviewTemplateKind.Event,
            Array.Empty<EHero>(),
            "Event",
            "Pick one"
        );
        var step = Template(
            StepId,
            EncounterPreviewTemplateKind.EncounterStep,
            new[] { EHero.Jules },
            "Jules Step",
            "Take it"
        );
        var eventPlan = new EncounterPreviewEventPlan(
            EventId,
            isRandomSelectionEvent: false,
            suppressRandomOutcome: false,
            choiceLimit: 1,
            outcomeGroups: Array.Empty<EncounterOutcomeGroupData>(),
            choiceGroups: new[]
            {
                new EncounterChoiceGroupData(
                    isRandomPool: false,
                    new[] { new EncounterStepReference(StepId) }
                ),
            }
        );
        var levelPlan = new LevelUpPreviewPlan(
            level: 2,
            healthIncrease: 150,
            isRandomSelection: false,
            groups: Array.Empty<LevelUpPreviewGroup>()
        );
        return new EncounterPreviewSnapshot(
            new[] { eventPlan },
            new[] { root, step },
            new[] { levelPlan }
        );
    }

    private static EncounterPreviewSnapshot DayTierStepSnapshot()
    {
        var reward = new EncounterRewardFilter(
            ECardType.Item,
            quantity: 1,
            fromAnyHero: false,
            Array.Empty<ECardSize>(),
            new[] { ETier.Bronze, ETier.Silver, ETier.Gold, ETier.Diamond },
            Array.Empty<ECardTag>(),
            Array.Empty<EHiddenTag>(),
            "Item",
            usesDayTierTable: true,
            usesDayTierDistribution: true
        );
        var step = new EncounterPreviewTemplatePlan(
            StepId,
            EncounterPreviewTemplateKind.EncounterStep,
            Array.Empty<EHero>(),
            "Day Tier Step",
            new EncounterPreviewLocalizedText(string.Empty, "Day Tier Step"),
            new EncounterPreviewLocalizedText(string.Empty, "Get an item"),
            new Dictionary<string, EncounterPreviewAbilityValue>(),
            reward
        );
        return new EncounterPreviewSnapshot(
            Array.Empty<EncounterPreviewEventPlan>(),
            new[] { step },
            Array.Empty<LevelUpPreviewPlan>()
        );
    }

    private BppGameDataSourceInfo CreateSourceInfo(string name)
    {
        Directory.CreateDirectory(_directory);
        var databasePath = Path.Combine(_directory, $"{name}.db");
        File.WriteAllText(databasePath, name);
        return new BppGameDataSourceInfo(
            databasePath,
            Path.Combine(_directory, $"{name}.manifest.json"),
            "https://data.example.invalid"
        );
    }

    private static bool WaitForStatus(
        EncounterPreviewModule module,
        EncounterPreviewModuleStatus status
    ) => SpinWait.SpinUntil(() => module.Status == status, TimeSpan.FromSeconds(5));

    private static EncounterPreviewSnapshot CompiledLocalizationSnapshot()
    {
        var fixedId = Guid.Parse("91000000-0000-0000-0000-000000000011");
        var modifierId = Guid.Parse("91000000-0000-0000-0000-000000000012");
        var fallbackId = Guid.Parse("91000000-0000-0000-0000-000000000013");
        var playerAttributeId = Guid.Parse("91000000-0000-0000-0000-000000000014");
        var eventCard = new TCardEncounterEvent
        {
            Id = EventId,
            Type = ECardType.EventEncounter,
            InternalName = "Localization Event",
            Localization = Localization("Localization Event", "Pick one"),
            SelectionContext = new TSelectionContext
            {
                SpawnContext = new TSpawnContextQuery
                {
                    Limit = new TFixedValue { Value = 4 },
                    Groups = new List<TSpawnGroup>
                    {
                        new()
                        {
                            Filters = new List<ITSpawnFilter>
                            {
                                new TSpawnFilterIdList
                                {
                                    Ids = new List<Guid>
                                    {
                                        fixedId,
                                        modifierId,
                                        fallbackId,
                                        playerAttributeId,
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };
        var fixedStep = Step(
            fixedId,
            "Fixed",
            "Gain {ability.0}.",
            new TActionPlayerModifyAttribute
            {
                AttributeType = EPlayerAttributeType.Gold,
                Value = new TFixedValue { Value = 10f },
            }
        );
        var modifierStep = Step(
            modifierId,
            "Modifier",
            "Gain {ability.0.mod} Gold for each Food [{ability.0}]",
            new TActionPlayerModifyAttribute
            {
                Value = new TReferenceValueCardCount
                {
                    Modifier = new TValueModifier { Value = new TFixedValue { Value = 1f } },
                },
            }
        );
        var fallbackStep = Step(
            fallbackId,
            "Fallback",
            "Gain {ability.0}.",
            new TActionPlayerModifyAttribute
            {
                AttributeType = EPlayerAttributeType.Income,
                Value = new TFixedValue { Value = 5f },
            }
        );
        var playerAttributeStep = Step(
            playerAttributeId,
            "Player Attribute",
            "Gain {ability.0.mod} Prestige for each Player fight you have lost this run [{ability.0}]",
            new TActionPlayerModifyAttribute
            {
                AttributeType = EPlayerAttributeType.Prestige,
                Value = new TReferenceValuePlayerAttribute
                {
                    AttributeType = EPlayerAttributeType.Custom_8,
                    Modifier = new TValueModifier
                    {
                        ModifyMode = EValueModifierMode.Multiply,
                        Value = new TFixedValue { Value = 2f },
                        ShouldRound = true,
                    },
                },
            }
        );
        var map = new Dictionary<Guid, ITCard>
        {
            [EventId] = eventCard,
            [fixedId] = fixedStep,
            [modifierId] = modifierStep,
            [fallbackId] = fallbackStep,
            [playerAttributeId] = playerAttributeStep,
        };
        var compiler = new EncounterPreviewPlanCompiler(source =>
        {
            var template = Assert.IsAssignableFrom<TCardBase>(source);
            return template.Id == EventId
                ? JObject.Parse(
                    $$"""
                    {
                      "SelectionContext": {
                        "SpawnContext": {
                          "Limit": { "Value": 4 },
                          "Groups": [
                            {
                              "Filters": [
                                { "Ids": ["{{fixedId:D}}", "{{modifierId:D}}", "{{fallbackId:D}}", "{{playerAttributeId:D}}"] }
                              ]
                            }
                          ]
                        }
                      }
                    }
                    """
                )
                : new JObject();
        });
        var result = compiler.Compile(map);
        Assert.True(
            result.FailureCount == 0,
            $"Failed templates: {string.Join(", ", result.FailedTemplateIds)}"
        );
        return result.Snapshot;
    }

    private static TCardEncounterStep Step(
        Guid id,
        string title,
        string description,
        TActionBase action
    ) =>
        new()
        {
            Id = id,
            Type = ECardType.EncounterStep,
            InternalName = title,
            Localization = Localization(title, description),
            Abilities = new Dictionary<string, TCardAbility>
            {
                ["0"] = new TCardAbility { Action = action },
            },
        };

    private static TCardLocalization Localization(string title, string description) =>
        new()
        {
            Title = new TLocalizableText { Text = title },
            Description = new TLocalizableText { Text = description },
        };

    private static EncounterPreviewTemplatePlan Template(
        Guid id,
        EncounterPreviewTemplateKind kind,
        IReadOnlyList<EHero> heroes,
        string title,
        string description
    ) =>
        new(
            id,
            kind,
            heroes,
            title,
            new EncounterPreviewLocalizedText(string.Empty, title),
            new EncounterPreviewLocalizedText(string.Empty, description),
            new Dictionary<string, EncounterPreviewAbilityValue>(),
            rewardFilter: null
        );

    private sealed class FakeRuntime(object source) : IEncounterPreviewGameRuntime
    {
        private int _cardMapLoadCount;

        public object Source { get; set; } = source;
        public EHero? CurrentHero { get; set; }
        public Func<
            Guid,
            string,
            bool,
            (bool Success, string ValueText, string? Unit)
        >? LiveValueResolver { get; set; }
        public EncounterInventory? Inventory { get; set; }
        public Action? OnResolveDayTiers { get; set; }
        public bool ThrowOnDayTierRead { get; set; }
        public object? LastDayTierSource { get; private set; }
        public int DayTierReadCount { get; private set; }
        public GameDataDayTierResolution DayTierResolution { get; set; } =
            GameDataDayTierResolution.Available(
                day: 3,
                GameDataDayTierTable.FromWeights(0.5f, 0.5f, 0f, 0f)!
            );
        public bool IsInCombat { get; set; }
        public BppGameDataSourceInfo? SourceInfo { get; set; }
        public Dictionary<Guid, ITCard>? CardMap { get; set; }
        public Dictionary<int, TLevelUp>? LevelUps { get; set; }
        public Func<object, BppGameDataSourceInfo?>? SourceInfoResolver { get; set; }
        public Func<object, Task<Dictionary<Guid, ITCard>?>>? CardMapLoader { get; set; }
        public int CardMapLoadCount => Volatile.Read(ref _cardMapLoadCount);

        public object? TryGetReadyStaticData() => Source;

        public BppGameDataSourceInfo? TryCaptureSourceInfo(object source) =>
            SourceInfoResolver?.Invoke(source) ?? SourceInfo;

        public Task<Dictionary<Guid, ITCard>?> LoadCardMapAsync(object source)
        {
            Interlocked.Increment(ref _cardMapLoadCount);
            return CardMapLoader?.Invoke(source) ?? Task.FromResult(CardMap);
        }

        public Dictionary<int, TLevelUp>? SnapshotLevelUps(object source) => LevelUps;

        public TCardBase? GetCardTemplate(object source, Guid templateId) => null;

        public bool TryEvaluateAbilityValue(
            object source,
            Guid templateId,
            string effectId,
            bool isAura,
            out string valueText,
            out string? unit
        )
        {
            var result = LiveValueResolver?.Invoke(templateId, effectId, isAura) ?? default;
            valueText = result.ValueText ?? string.Empty;
            unit = result.Unit;
            return result.Success;
        }

        public EHero? ReadCurrentHero() => CurrentHero;

        public EncounterInventory? ReadInventory() => Inventory;

        public GameDataDayTierResolution ResolveDayTiers(object source)
        {
            OnResolveDayTiers?.Invoke();
            if (ThrowOnDayTierRead)
                throw new InvalidOperationException("day tiers unavailable");
            DayTierReadCount++;
            LastDayTierSource = source;
            return DayTierResolution;
        }

        public string ColorKeywords(string text) => text;
    }

    private sealed record BrokenEventCard : TCardBase
    {
        public override ECardType Type { get; init; } = ECardType.EventEncounter;

        public object Loop => this;
    }

    private sealed class TestLanguageProvider : ILanguageProvider
    {
        public string CurrentLanguageCode => "en";
    }

    private sealed class TestLocaleModeProvider : ILocaleModeProvider
    {
        public BppChineseLocaleMode CurrentMode => BppChineseLocaleMode.Mainland;
    }
}
