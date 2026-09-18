using BazaarGameShared.Domain.Core.Types;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class EncounterPreviewEvaluatorTests
{
    private static readonly Guid EventId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid StepId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid RequiredItemId = Guid.Parse(
        "30000000-0000-0000-0000-000000000003"
    );

    [Fact]
    public void Same_static_plan_re_evaluates_hero_day_and_inventory()
    {
        var requirement = new EncounterCardRequirement(
            new[] { RequiredItemId },
            Array.Empty<IReadOnlyList<string>>(),
            "Any",
            "GreaterThanOrEqual",
            1
        );
        var group = new EncounterChoiceGroupData(
            isRandomPool: false,
            new[] { new EncounterStepReference(StepId, new[] { requirement }) },
            new EncounterDayCondition(day: 3, comparison: "GreaterThanOrEqual")
        );
        var (snapshot, plan) = Plan(group);

        var wrongHero = EncounterEventDetailResolver.TryResolve(
            plan,
            snapshot,
            EHero.Vanessa,
            Inventory(RequiredItemId),
            currentDay: 3
        );
        Assert.NotNull(wrongHero);
        Assert.Empty(wrongHero.ChoiceDetails);

        var wrongDay = EncounterEventDetailResolver.TryResolve(
            plan,
            snapshot,
            EHero.Jules,
            Inventory(RequiredItemId),
            currentDay: 2
        );
        Assert.NotNull(wrongDay);
        Assert.Empty(wrongDay.ChoiceDetails);

        var missingItem = EncounterEventDetailResolver.TryResolve(
            plan,
            snapshot,
            EHero.Jules,
            Inventory(),
            currentDay: 3
        );
        var dimmed = Assert.Single(Assert.IsType<EncounterOption>(missingItem).ChoiceDetails);
        Assert.False(dimmed.IsEligible);

        var eligible = EncounterEventDetailResolver.TryResolve(
            plan,
            snapshot,
            EHero.Jules,
            Inventory(RequiredItemId),
            currentDay: 3
        );
        var shown = Assert.Single(Assert.IsType<EncounterOption>(eligible).ChoiceDetails);
        Assert.True(shown.IsEligible);
        Assert.Equal("Jules Step", shown.DisplayName);
        Assert.Equal("Take it", shown.ResultText);
    }

    [Fact]
    public void Outcome_percentages_are_recomputed_after_dynamic_requirements()
    {
        var requirement = new EncounterCardRequirement(
            new[] { RequiredItemId },
            Array.Empty<IReadOnlyList<string>>(),
            "Any",
            "GreaterThanOrEqual",
            1
        );
        var root = Template(
            EventId,
            EncounterPreviewTemplateKind.Event,
            Array.Empty<EHero>(),
            "Event",
            "Event result"
        );
        var rewardA = Template(
            StepId,
            EncounterPreviewTemplateKind.EncounterStep,
            new[] { EHero.Common },
            "A",
            "Gain A"
        );
        var rewardBId = Guid.Parse("30000000-0000-0000-0000-000000000004");
        var rewardB = Template(
            rewardBId,
            EncounterPreviewTemplateKind.EncounterStep,
            new[] { EHero.Common },
            "B",
            "Gain B"
        );
        var plan = new EncounterPreviewEventPlan(
            EventId,
            isRandomSelectionEvent: true,
            suppressRandomOutcome: false,
            choiceLimit: 1,
            outcomeGroups: new[]
            {
                new EncounterOutcomeGroupData(
                    50,
                    new[] { StepId },
                    Array.Empty<EncounterOutcomeQueryPool>(),
                    Array.Empty<EncounterCardRequirement>(),
                    dayCondition: null
                ),
                new EncounterOutcomeGroupData(
                    50,
                    new[] { rewardBId },
                    Array.Empty<EncounterOutcomeQueryPool>(),
                    new[] { requirement },
                    dayCondition: null
                ),
            },
            choiceGroups: Array.Empty<EncounterChoiceGroupData>()
        );
        var snapshot = new EncounterPreviewSnapshot(
            new[] { plan },
            new[] { root, rewardA, rewardB }
        );

        var withoutItem = EncounterEventDetailResolver.TryResolve(
            plan,
            snapshot,
            EHero.Jules,
            Inventory(),
            currentDay: 1
        );
        Assert.Equal(100, withoutItem!.OutcomeGroups![0].Percent);
        Assert.Null(withoutItem.OutcomeGroups[1].Percent);
        Assert.False(withoutItem.OutcomeGroups[1].IsEligible);

        var withItem = EncounterEventDetailResolver.TryResolve(
            plan,
            snapshot,
            EHero.Jules,
            Inventory(RequiredItemId),
            currentDay: 1
        );
        Assert.Equal(new int?[] { 50, 50 }, withItem!.OutcomeGroups!.Select(x => x.Percent));
    }

    private static (EncounterPreviewSnapshot Snapshot, EncounterPreviewEventPlan Plan) Plan(
        EncounterChoiceGroupData group
    )
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
        var plan = new EncounterPreviewEventPlan(
            EventId,
            isRandomSelectionEvent: false,
            suppressRandomOutcome: false,
            choiceLimit: 1,
            outcomeGroups: Array.Empty<EncounterOutcomeGroupData>(),
            choiceGroups: new[] { group }
        );
        return (new EncounterPreviewSnapshot(new[] { plan }, new[] { root, step }), plan);
    }

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

    private static EncounterInventory Inventory(params Guid[] ids) =>
        new(ids.Select(id => new EncounterInventoryCard(id, Array.Empty<string>())).ToArray());
}
