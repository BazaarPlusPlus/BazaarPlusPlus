using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarPlusPlus.GameInterop.CombatSimulation;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace CombatImpactAdapter.Tests;

[CollectionDefinition(nameof(BppLogCollection), DisableParallelization = true)]
public sealed class BppLogCollection;

// The vocabulary pin covers the configured snapshot. This warning covers the game build the player actually runs.
[Collection(nameof(BppLogCollection))]
public sealed class DesktopCombatTimelineUnmappedMemberTests
{
    private const string EventId = "post_combat_impact.timeline.unmapped_member";

    [Fact]
    public void Each_unmapped_game_enum_is_logged_once_and_its_values_are_dropped()
    {
        var lines = new List<string>();
        BppLog.Install(new ListLogSink(lines));
        var simulation = new CombatSim();
        simulation.Frames[0].Events.Add(Executed((EActionCommandType)9999));
        simulation.Frames[0].Events.Add(Executed((EActionCommandType)9998));
        simulation
            .Frames[0]
            .Events.Add(new CombatSimEventCombatantDied { CombatantId = (ECombatantId)99 });

        var first = DesktopCombatTimeline.From(simulation);
        var second = DesktopCombatTimeline.From(simulation);

        Assert.Empty(Assert.Single(first.Frames).Events);
        Assert.Empty(Assert.Single(second.Frames).Events);
        var logged = lines
            .Where(line => line.Contains("event=" + EventId, StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, logged.Length);
        var action = Assert.Single(
            logged,
            line => line.Contains(nameof(EActionCommandType), StringComparison.Ordinal)
        );
        Assert.Contains("9999", action, StringComparison.Ordinal);
        var combatant = Assert.Single(
            logged,
            line => line.Contains(nameof(ECombatantId), StringComparison.Ordinal)
        );
        Assert.Contains("99", combatant, StringComparison.Ordinal);
    }

    private static CombatSimEventEffectExecuted Executed(EActionCommandType actionType) =>
        new()
        {
            ExecutionContextId = "context",
            EffectId = "effect",
            ActionType = actionType,
        };

    private sealed class ListLogSink(List<string> lines) : IBppLogSink
    {
        public void Write(BppLogSeverity severity, string message)
        {
            lock (lines)
                lines.Add(message);
        }
    }
}
