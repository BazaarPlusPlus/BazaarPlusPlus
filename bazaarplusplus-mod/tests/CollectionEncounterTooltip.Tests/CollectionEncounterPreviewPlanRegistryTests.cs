using BazaarGameShared.Domain.Core.Types;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class EncounterPreviewPlanRegistryTests
{
    [Fact]
    public void Late_generation_cannot_publish_over_the_current_manager()
    {
        var registry = new EncounterPreviewPlanRegistry();
        var sourceA = new object();
        var sourceB = new object();
        var snapshotA = Snapshot(Guid.Parse("40000000-0000-0000-0000-000000000001"));
        var snapshotB = Snapshot(Guid.Parse("40000000-0000-0000-0000-000000000002"));

        var generationA = registry.BeginGeneration(sourceA);
        var generationB = registry.BeginGeneration(sourceB);

        Assert.False(registry.TryPublish(sourceA, generationA, snapshotA));
        Assert.False(registry.TryGet(sourceA, out _));
        Assert.True(registry.TryPublish(sourceB, generationB, snapshotB));
        Assert.True(registry.TryGet(sourceB, out var current));
        Assert.Same(snapshotB, current);
    }

    [Fact]
    public void Beginning_a_new_generation_immediately_unpublishes_the_old_snapshot()
    {
        var registry = new EncounterPreviewPlanRegistry();
        var sourceA = new object();
        var sourceB = new object();
        var generationA = registry.BeginGeneration(sourceA);
        Assert.True(registry.TryPublish(sourceA, generationA, Snapshot(Guid.NewGuid())));

        registry.BeginGeneration(sourceB);

        Assert.False(registry.TryGet(sourceA, out _));
        Assert.False(registry.TryGet(sourceB, out _));
    }

    [Fact]
    public void Commit_callback_only_runs_for_the_current_generation()
    {
        var registry = new EncounterPreviewPlanRegistry();
        var sourceA = new object();
        var sourceB = new object();
        var generationA = registry.BeginGeneration(sourceA);
        var generationB = registry.BeginGeneration(sourceB);
        var commits = 0;

        Assert.False(
            registry.TryCommitAndPublish(
                sourceA,
                generationA,
                Snapshot(Guid.NewGuid()),
                () => commits++
            )
        );
        Assert.True(
            registry.TryCommitAndPublish(
                sourceB,
                generationB,
                Snapshot(Guid.NewGuid()),
                () => commits++
            )
        );
        Assert.Equal(1, commits);
    }

    [Fact]
    public void Terminal_callback_cannot_run_after_its_generation_is_superseded()
    {
        var registry = new EncounterPreviewPlanRegistry();
        var sourceA = new object();
        var sourceB = new object();
        var generationA = registry.BeginGeneration(sourceA);
        registry.BeginGeneration(sourceB);
        var terminalCommits = 0;

        Assert.False(registry.TryRunIfCurrent(sourceA, generationA, () => terminalCommits++));
        Assert.Equal(0, terminalCommits);
    }

    [Fact]
    public void Beginning_a_generation_waits_for_an_inflight_terminal_callback()
    {
        AssertGenerationAdvanceWaitsForTerminal(registry => registry.BeginGeneration(new object()));
    }

    [Fact]
    public void Reset_waits_for_an_inflight_terminal_callback()
    {
        AssertGenerationAdvanceWaitsForTerminal(registry => registry.Reset());
    }

    [Fact]
    public void Published_snapshot_exposes_compiled_encounter_step_templates()
    {
        var registry = new EncounterPreviewPlanRegistry();
        var source = new object();
        var eventId = Guid.Parse("40000000-0000-0000-0000-000000000003");
        var stepId = Guid.Parse("40000000-0000-0000-0000-000000000004");
        var snapshot = Snapshot(eventId, stepId);
        var generation = registry.BeginGeneration(source);
        Assert.True(registry.TryPublish(source, generation, snapshot));

        Assert.True(registry.TryGet(source, out var published));
        Assert.True(published.TryGetTemplate(stepId, out var step));
        Assert.Equal(EncounterPreviewTemplateKind.EncounterStep, step.Kind);
    }

    private static EncounterPreviewSnapshot Snapshot(Guid eventId)
    {
        var template = new EncounterPreviewTemplatePlan(
            eventId,
            EncounterPreviewTemplateKind.Event,
            Array.Empty<EHero>(),
            "Event",
            new EncounterPreviewLocalizedText(string.Empty, "Event"),
            new EncounterPreviewLocalizedText(string.Empty, "Event"),
            new Dictionary<string, EncounterPreviewAbilityValue>(),
            rewardFilter: null
        );
        var plan = new EncounterPreviewEventPlan(
            eventId,
            isRandomSelectionEvent: false,
            suppressRandomOutcome: false,
            choiceLimit: 1,
            Array.Empty<EncounterOutcomeGroupData>(),
            Array.Empty<EncounterChoiceGroupData>()
        );
        return new EncounterPreviewSnapshot(new[] { plan }, new[] { template });
    }

    private static void AssertGenerationAdvanceWaitsForTerminal(
        Action<EncounterPreviewPlanRegistry> advanceGeneration
    )
    {
        var registry = new EncounterPreviewPlanRegistry();
        var source = new object();
        var generation = registry.BeginGeneration(source);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var advanceStarted = new ManualResetEventSlim();
        var terminal = Task.Run(() =>
            registry.TryRunIfCurrent(
                source,
                generation,
                () =>
                {
                    entered.Set();
                    release.Wait();
                }
            )
        );
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var advance = Task.Run(() =>
        {
            advanceStarted.Set();
            advanceGeneration(registry);
        });
        try
        {
            Assert.True(advanceStarted.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(advance.Wait(TimeSpan.FromMilliseconds(100)));
        }
        finally
        {
            release.Set();
        }

        Assert.True(terminal.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(terminal.Result);
        Assert.True(advance.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(registry.TryRunIfCurrent(source, generation, () => { }));
    }

    private static EncounterPreviewSnapshot Snapshot(Guid eventId, Guid stepId)
    {
        var eventTemplate = new EncounterPreviewTemplatePlan(
            eventId,
            EncounterPreviewTemplateKind.Event,
            Array.Empty<EHero>(),
            "Event",
            new EncounterPreviewLocalizedText(string.Empty, "Event"),
            new EncounterPreviewLocalizedText(string.Empty, "Event"),
            new Dictionary<string, EncounterPreviewAbilityValue>(),
            rewardFilter: null
        );
        var stepTemplate = new EncounterPreviewTemplatePlan(
            stepId,
            EncounterPreviewTemplateKind.EncounterStep,
            Array.Empty<EHero>(),
            "Step",
            new EncounterPreviewLocalizedText(string.Empty, "Step"),
            new EncounterPreviewLocalizedText(string.Empty, "Get an item"),
            new Dictionary<string, EncounterPreviewAbilityValue>(),
            rewardFilter: null
        );
        var plan = new EncounterPreviewEventPlan(
            eventId,
            isRandomSelectionEvent: false,
            suppressRandomOutcome: false,
            choiceLimit: 1,
            Array.Empty<EncounterOutcomeGroupData>(),
            new[]
            {
                new EncounterChoiceGroupData(
                    isRandomPool: false,
                    new[] { new EncounterStepReference(stepId) }
                ),
            }
        );
        return new EncounterPreviewSnapshot(new[] { plan }, new[] { eventTemplate, stepTemplate });
    }
}
