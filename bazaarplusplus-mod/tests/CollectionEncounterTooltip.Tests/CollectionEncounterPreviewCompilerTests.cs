using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Event;
using BazaarGameShared.Domain.Cards.Encounter.Step;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Runs;
using BazaarGameShared.Domain.Spawning.SpawnFilters;
using BazaarGameShared.Domain.Spawning.SpawnFilters.Constraints;
using BazaarGameShared.Domain.Spawning.SpawnGroups;
using BazaarGameShared.Domain.Spawning.SpawningContexts;
using BazaarGameShared.Domain.Values;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class EncounterPreviewCompilerTests
{
    [Fact]
    public void Compiles_event_structure_and_only_scans_event_steps_for_rewards()
    {
        var eventId = Guid.Parse("50000000-0000-0000-0000-000000000001");
        var stepId = Guid.Parse("50000000-0000-0000-0000-000000000002");
        var itemId = Guid.Parse("50000000-0000-0000-0000-000000000003");
        var eventTemplate = new TCardEncounterEvent
        {
            Id = eventId,
            InternalName = "Test Event",
            Localization = Localization("Test Event", "Pick one"),
            SelectionContext = new TSelectionContext
            {
                SpawnContext = new TSpawnContextQuery
                {
                    Limit = new TFixedValue { Value = 1 },
                    Groups = new List<TSpawnGroup>
                    {
                        new()
                        {
                            Filters = new List<ITSpawnFilter>
                            {
                                new TSpawnFilterIdList
                                {
                                    Ids = new List<Guid> { stepId, itemId },
                                },
                            },
                        },
                    },
                },
            },
        };
        var step = new TCardEncounterStep
        {
            Id = stepId,
            InternalName = "Step",
            Localization = Localization("Step", "Get an item"),
            Abilities = RewardAbilities(),
        };
        var item = new TCardItem
        {
            Id = itemId,
            Type = ECardType.Item,
            InternalName = "Item",
            Localization = Localization("Item", "An item ability must not be a choice reward"),
            Abilities = RewardAbilities(),
        };
        var map = new Dictionary<Guid, ITCard>
        {
            [eventId] = eventTemplate,
            [stepId] = step,
            [itemId] = item,
        };

        var preparedIds = new List<Guid>();
        var compiler = new EncounterPreviewPlanCompiler(source =>
        {
            var template = Assert.IsAssignableFrom<TCardBase>(source);
            preparedIds.Add(template.Id);
            if (template.Id != eventId)
                return new JObject();
            return JObject.Parse(
                $$"""
                {
                  "SelectionContext": {
                    "SpawnContext": {
                      "Limit": { "Value": 1 },
                      "Groups": [
                        {
                          "Filters": [
                            { "Ids": ["{{stepId:D}}", "{{itemId:D}}"] }
                          ]
                        }
                      ]
                    }
                  }
                }
                """
            );
        });

        var result = compiler.Compile(map);

        Assert.True(
            result.FailureCount == 0,
            $"Failed templates: {string.Join(", ", result.FailedTemplateIds)}"
        );
        var plan = Assert.Single(result.Snapshot.Events);
        Assert.Equal(1, plan.ChoiceLimit);
        Assert.Equal(
            new[] { stepId, itemId },
            Assert.Single(plan.ChoiceGroups).Members.Select(x => x.TemplateId)
        );
        Assert.True(result.Snapshot.TryGetTemplate(stepId, out var stepPlan));
        Assert.NotNull(stepPlan.RewardFilter);
        Assert.True(result.Snapshot.TryGetTemplate(itemId, out var itemPlan));
        Assert.Null(itemPlan.RewardFilter);
        // The step's reward resolves through the runtime action route, so the compiler never
        // materializes its JToken; the item template is still never scanned for rewards.
        Assert.Equal(new[] { eventId }, preparedIds);
    }

    private static TCardLocalization Localization(string title, string description) =>
        new()
        {
            Title = new TLocalizableText { Key = $"{title}-key", Text = title },
            Description = new TLocalizableText { Key = $"{description}-key", Text = description },
        };

    private static Dictionary<string, TCardAbility> RewardAbilities() =>
        new()
        {
            ["0"] = new TCardAbility
            {
                Action = new TActionGameDealCards
                {
                    SpawnContext = new TSpawnContextQuery
                    {
                        Limit = new TFixedValue { Value = 1 },
                        Groups = new List<TSpawnGroup>
                        {
                            new()
                            {
                                Filters = new List<ITSpawnFilter>
                                {
                                    new TSpawnFilterQuery
                                    {
                                        Constraints = new ConstraintCardType
                                        {
                                            Types = new HashSet<ECardType> { ECardType.Item },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };
}
