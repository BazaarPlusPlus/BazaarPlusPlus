#nullable enable
using BazaarPlusPlus.Game.HistoryPanel;
using Xunit;

namespace HistoryPanelOperationalLogging.Tests;

public sealed class HistoryPanelReplayCleanupTests
{
    [Fact]
    public void Cleanup_counts_affected_battles_once_and_attempts_both_payload_kinds()
    {
        var localAttempts = new List<string>();
        var ghostAttempts = new List<string>();

        var result = HistoryPanelReplayCleanup.Execute(
            new[] { "battle-a", "battle-b", "battle-c" },
            battleId =>
            {
                localAttempts.Add(battleId);
                if (battleId is "battle-a" or "battle-b")
                    throw new IOException($"local {battleId}");
            },
            battleId =>
            {
                ghostAttempts.Add(battleId);
                if (battleId is "battle-a" or "battle-c")
                    throw new IOException($"ghost {battleId}");
            }
        );

        Assert.Equal(3, result.FailedBattleCount);
        Assert.Equal(new[] { "battle-a", "battle-b", "battle-c" }, localAttempts);
        Assert.Equal(new[] { "battle-a", "battle-b", "battle-c" }, ghostAttempts);
        Assert.Equal("local battle-a", result.Exception?.Message);
    }

    [Fact]
    public void Cleanup_success_is_a_zero_failure_aggregate()
    {
        var result = HistoryPanelReplayCleanup.Execute(new[] { "battle-a" }, _ => { }, _ => { });

        Assert.Equal(0, result.FailedBattleCount);
        Assert.Null(result.Exception);
    }
}
