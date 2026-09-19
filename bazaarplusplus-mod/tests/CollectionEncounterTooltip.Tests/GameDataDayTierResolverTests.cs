using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.DayTiers;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class GameDataDayTierResolverTests
{
    private static readonly Guid GameModeId = Guid.Parse("92000000-0000-0000-0000-000000000001");

    [Fact]
    public void Resolve_distinguishes_every_day_tier_status()
    {
        var source = new FakeSource();
        var resolver = new GameDataDayTierResolver(source);

        source.Context = GameDataDayTierSourceContext.NotApplicable();
        Assert.Equal(GameDataDayTierStatus.NotApplicable, resolver.Resolve().Status);

        source.Context = GameDataDayTierSourceContext.NotReady(day: 3);
        Assert.Equal(GameDataDayTierStatus.NotReady, resolver.Resolve().Status);

        source.Context = GameDataDayTierSourceContext.Invalid(day: 0);
        Assert.Equal(GameDataDayTierStatus.Invalid, resolver.Resolve().Status);

        source.Context = GameDataDayTierSourceContext.Available(new object(), GameModeId, day: 3);
        source.ReadStatus = GameDataDayTierStatus.Missing;
        Assert.Equal(GameDataDayTierStatus.Missing, resolver.Resolve().Status);

        source.ReadStatus = GameDataDayTierStatus.Available;
        source.Weights = new GameDataDayTierWeights(0f, -1f, float.NaN, float.PositiveInfinity);
        Assert.Equal(GameDataDayTierStatus.Invalid, resolver.Resolve().Status);

        source.Weights = new GameDataDayTierWeights(0.8f, 0.2f, 0f, 0f);
        var available = resolver.Resolve();
        Assert.Equal(GameDataDayTierStatus.Available, available.Status);
        Assert.Equal(3, available.Day);
        Assert.Equal(ETier.Silver, available.MaximumTier);
        Assert.NotNull(available.Table);
    }

    [Fact]
    public void NotReady_is_retried_and_only_same_generation_successes_are_cached()
    {
        var manager = new object();
        var source = new FakeSource
        {
            Context = GameDataDayTierSourceContext.NotReady(day: 4),
            Weights = new GameDataDayTierWeights(0.7f, 0.3f, 0f, 0f),
        };
        var resolver = new GameDataDayTierResolver(source);

        Assert.Equal(GameDataDayTierStatus.NotReady, resolver.Resolve().Status);

        source.Context = GameDataDayTierSourceContext.Available(manager, GameModeId, day: 4);
        var first = resolver.Resolve();
        var second = resolver.Resolve();

        Assert.Equal(GameDataDayTierStatus.Available, first.Status);
        Assert.Same(first, second);
        Assert.Equal(1, source.ReadCount);
    }

    [Fact]
    public void Non_successful_reads_are_not_cached()
    {
        foreach (
            var firstStatus in new[]
            {
                GameDataDayTierStatus.Missing,
                GameDataDayTierStatus.NotReady,
            }
        )
        {
            var source = new FakeSource
            {
                Context = GameDataDayTierSourceContext.Available(new object(), GameModeId, day: 4),
                ReadStatus = firstStatus,
                Weights = new GameDataDayTierWeights(0.7f, 0.3f, 0f, 0f),
            };
            var resolver = new GameDataDayTierResolver(source);

            Assert.Equal(firstStatus, resolver.Resolve().Status);
            source.ReadStatus = GameDataDayTierStatus.Available;
            Assert.Equal(GameDataDayTierStatus.Available, resolver.Resolve().Status);

            Assert.Equal(2, source.ReadCount);
        }
    }

    [Fact]
    public void Invalid_weights_are_reparsed_after_same_generation_data_becomes_usable()
    {
        var source = new FakeSource
        {
            Context = GameDataDayTierSourceContext.Available(new object(), GameModeId, day: 4),
            Weights = new GameDataDayTierWeights(0f, -1f, float.NaN, float.PositiveInfinity),
        };
        var resolver = new GameDataDayTierResolver(source);

        Assert.Equal(GameDataDayTierStatus.Invalid, resolver.Resolve().Status);
        source.Weights = new GameDataDayTierWeights(0.7f, 0.3f, 0f, 0f);
        Assert.Equal(GameDataDayTierStatus.Available, resolver.Resolve().Status);

        Assert.Equal(2, source.ReadCount);
    }

    [Fact]
    public void Manager_reference_change_discards_the_previous_generation_cache()
    {
        var firstManager = new object();
        var secondManager = new object();
        var source = new FakeSource
        {
            Context = GameDataDayTierSourceContext.Available(firstManager, GameModeId, day: 5),
            WeightsByManager = manager =>
                ReferenceEquals(manager, firstManager)
                    ? new GameDataDayTierWeights(0.9f, 0.1f, 0f, 0f)
                    : new GameDataDayTierWeights(0.9f, 0f, 0f, 0.1f),
        };
        var resolver = new GameDataDayTierResolver(source);

        Assert.Equal(ETier.Silver, resolver.Resolve().MaximumTier);
        source.Context = GameDataDayTierSourceContext.Available(secondManager, GameModeId, day: 5);
        Assert.Equal(ETier.Diamond, resolver.Resolve().MaximumTier);
        source.Context = GameDataDayTierSourceContext.Available(firstManager, GameModeId, day: 5);
        Assert.Equal(ETier.Silver, resolver.Resolve().MaximumTier);

        Assert.Equal(3, source.ReadCount);
    }

    [Fact]
    public void Expected_generation_mismatch_returns_NotReady_without_reading_weights()
    {
        var expectedManager = new object();
        var source = new FakeSource
        {
            Context = GameDataDayTierSourceContext.Available(new object(), GameModeId, day: 6),
        };
        var resolver = new GameDataDayTierResolver(source);

        var result = resolver.Resolve(expectedManager);

        Assert.Equal(GameDataDayTierStatus.NotReady, result.Status);
        Assert.Equal(0, source.ReadCount);
    }

    private sealed class FakeSource : IGameDataDayTierSource
    {
        public GameDataDayTierSourceContext Context { get; set; } =
            GameDataDayTierSourceContext.NotApplicable();
        public GameDataDayTierStatus ReadStatus { get; set; } = GameDataDayTierStatus.Available;
        public GameDataDayTierWeights Weights { get; set; } = new(0.5f, 0.5f, 0f, 0f);
        public Func<object, GameDataDayTierWeights>? WeightsByManager { get; set; }
        public int ReadCount { get; private set; }

        public GameDataDayTierSourceContext Capture() => Context;

        public GameDataDayTierStatus ReadWeights(
            object manager,
            Guid gameModeId,
            int day,
            out GameDataDayTierWeights weights
        )
        {
            ReadCount++;
            weights = WeightsByManager?.Invoke(manager) ?? Weights;
            return ReadStatus;
        }
    }
}
