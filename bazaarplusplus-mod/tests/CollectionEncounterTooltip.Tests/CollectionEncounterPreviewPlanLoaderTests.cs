using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Game;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class EncounterPreviewPlanLoaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"bpp-encounter-loader-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task Cache_hit_does_not_load_the_card_map_or_invoke_the_compiler()
    {
        var store = Store();
        var identity = Identity("etag-a");
        var snapshot = Snapshot();
        store.Save(identity, snapshot);
        var compileCalls = 0;
        var mapCalls = 0;
        var loader = new EncounterPreviewPlanLoader(
            store,
            (_, _) =>
            {
                compileCalls++;
                throw new InvalidOperationException("compiler must not run");
            }
        );

        var result = await loader.LoadAsync(
            identity,
            () =>
            {
                mapCalls++;
                throw new InvalidOperationException("card map must not load");
            },
            () => throw new InvalidOperationException("level ups must not load"),
            CancellationToken.None
        );

        Assert.True(result.WasCacheHit);
        Assert.Equal(0, compileCalls);
        Assert.Equal(0, mapCalls);
        Assert.Equal(snapshot.EventCount, result.Snapshot.EventCount);
    }

    [Fact]
    public async Task Cache_miss_loads_and_compiles_exactly_once()
    {
        var store = Store();
        var compileCalls = 0;
        var mapCalls = 0;
        var snapshot = Snapshot();
        var loader = new EncounterPreviewPlanLoader(
            store,
            (_, _) =>
            {
                compileCalls++;
                return new EncounterPreviewCompileResult(snapshot, Array.Empty<Guid>());
            }
        );

        var result = await loader.LoadAsync(
            Identity("etag-a"),
            () =>
            {
                mapCalls++;
                return Task.FromResult<Dictionary<Guid, ITCard>?>(new());
            },
            () => new Dictionary<int, TLevelUp>(),
            CancellationToken.None
        );

        Assert.False(result.WasCacheHit);
        Assert.Equal("not-found", result.CacheMissReason);
        Assert.Equal(1, compileCalls);
        Assert.Equal(1, mapCalls);
        Assert.NotNull(result.CompileResult);
    }

    private EncounterPreviewCacheStore Store() =>
        new(Path.Combine(_directory, "preview-plans.json"));

    private static EncounterPreviewCacheIdentity Identity(string etag) =>
        new("etag", "https://example.invalid/GameData.db.zip", etag, "build", "Online");

    private static EncounterPreviewSnapshot Snapshot()
    {
        var id = Guid.Parse("60000000-0000-0000-0000-000000000001");
        return new EncounterPreviewSnapshot(
            new[]
            {
                new EncounterPreviewEventPlan(
                    id,
                    isRandomSelectionEvent: false,
                    suppressRandomOutcome: false,
                    choiceLimit: 1,
                    Array.Empty<EncounterOutcomeGroupData>(),
                    Array.Empty<EncounterChoiceGroupData>()
                ),
            },
            new[]
            {
                new EncounterPreviewTemplatePlan(
                    id,
                    EncounterPreviewTemplateKind.Event,
                    Array.Empty<EHero>(),
                    "Event",
                    new EncounterPreviewLocalizedText(string.Empty, "Event"),
                    new EncounterPreviewLocalizedText(string.Empty, "Event"),
                    new Dictionary<string, EncounterPreviewAbilityValue>(),
                    rewardFilter: null
                ),
            }
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
