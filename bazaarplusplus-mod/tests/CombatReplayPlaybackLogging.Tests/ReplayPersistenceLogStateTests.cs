#nullable enable
using System.Collections.Concurrent;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Infrastructure;
using Xunit;

namespace CombatReplayPlaybackLogging.Tests;

public sealed class ReplayPersistenceLogStateTests : IDisposable
{
    public ReplayPersistenceLogStateTests() => BppLog.Reset();

    public void Dispose() => BppLog.Reset();

    [Fact]
    public void Persistence_completion_is_one_shot_under_worker_shutdown_races()
    {
        var gate = new ReplayPersistenceCompletionGate();
        var winners = new ConcurrentBag<int>();

        Parallel.For(
            0,
            128,
            index =>
            {
                if (gate.TryComplete())
                    winners.Add(index);
            }
        );

        Assert.Single(winners);
    }
}
