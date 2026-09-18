using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Step;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Game;
using BazaarGameShared.Domain.Prerequisites;
using BazaarGameShared.Domain.Prerequisites.Conditionals;
using BazaarGameShared.Domain.Spawning;
using BazaarGameShared.Domain.Spawning.SpawnFilters;
using BazaarGameShared.Domain.Spawning.SpawnGroups;
using BazaarGameShared.Domain.Spawning.SpawningContexts;
using BazaarGameShared.Domain.Values;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class LevelUpPreviewCompilerTests
{
    [Fact]
    public void Compiles_level_ups_with_current_level_key_and_shared_templates()
    {
        var rewardId = Guid.Parse("91000000-0000-0000-0000-000000000001");
        var missingId = Guid.Parse("91000000-0000-0000-0000-000000000002");
        var boardOnlyId = Guid.Parse("91000000-0000-0000-0000-000000000003");
        var cards = new Dictionary<Guid, ITCard>
        {
            [rewardId] = Step(rewardId, "Reward"),
            [boardOnlyId] = Step(boardOnlyId, "Board reward"),
        };
        var levelUp = new TLevelUp
        {
            Level = 2,
            HealthIncrease = 150,
            Rewards = new TSpawnContextQuery
            {
                SelectionMethod = ESpawnSelectionMethod.Random,
                Groups =
                {
                    Group(
                        new[] { rewardId },
                        limit: 2,
                        new TPrerequisiteRun
                        {
                            Conditions = new TRunConditionalPlayerHero
                            {
                                Heroes = { EHero.Vanessa },
                                Operator = EListComparisonOperator.Any,
                            },
                        }
                    ),
                    Group(new[] { boardOnlyId }, 1, new TPrerequisiteCardCount()),
                    Group(new[] { missingId }, 1),
                },
            },
        };

        var result = new EncounterPreviewPlanCompiler().Compile(
            cards,
            new Dictionary<int, TLevelUp> { [2] = levelUp }
        );

        Assert.Equal(1, result.Snapshot.LevelUpCount);
        Assert.True(result.Snapshot.TryGetLevelUp(2, out var plan));
        Assert.False(result.Snapshot.TryGetLevelUp(3, out _));
        Assert.Equal(150, plan.HealthIncrease);
        Assert.True(plan.IsRandomSelection);
        Assert.Equal(2, plan.Groups.Count);
        Assert.Equal(2, plan.Groups[0].Limit);
        Assert.Equal("Any", Assert.Single(plan.Groups[0].HeroConditions).ComparisonOperator);
        Assert.True(result.Snapshot.TryGetTemplate(rewardId, out _));
        Assert.False(result.Snapshot.TryGetTemplate(boardOnlyId, out _));
        Assert.Equal(1, result.Snapshot.Coverage.UnsupportedLevelUpPartCount);
        Assert.Equal(1, result.Snapshot.Coverage.MissingReferencedTemplateCount);
        Assert.Equal(0, result.Snapshot.Coverage.LevelUpFailureCount);
    }

    [Fact]
    public void Rejects_dictionary_key_that_disagrees_with_level_data()
    {
        var levelUp = new TLevelUp { Level = 7, Rewards = new TSpawnContextQuery() };

        var result = new EncounterPreviewPlanCompiler().Compile(
            new Dictionary<Guid, ITCard>(),
            new Dictionary<int, TLevelUp> { [6] = levelUp }
        );

        Assert.Equal(0, result.Snapshot.LevelUpCount);
        Assert.Equal(1, result.Snapshot.Coverage.LevelUpFailureCount);
    }

    private static TCardEncounterStep Step(Guid id, string title) =>
        new()
        {
            Id = id,
            Type = ECardType.EncounterStep,
            InternalName = title,
            Localization = new TCardLocalization { Title = new TLocalizableText { Text = title } },
        };

    private static TSpawnGroup Group(
        IEnumerable<Guid> ids,
        int limit,
        params BazaarGameShared.Domain.Prerequisites.ITPrerequisite[] prerequisites
    ) =>
        new()
        {
            Filters = { new TSpawnFilterIdList { Ids = ids.ToList() } },
            Limit = new TFixedValue { Value = limit },
            Prerequisites = prerequisites.Length == 0 ? null : prerequisites.ToList(),
        };
}
