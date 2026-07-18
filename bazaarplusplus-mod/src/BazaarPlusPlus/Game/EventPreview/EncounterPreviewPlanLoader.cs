#nullable enable
using System.Diagnostics;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Game;

namespace BazaarPlusPlus.Game.EventPreview;

internal sealed class EncounterPreviewPlanLoadResult
{
    public EncounterPreviewPlanLoadResult(
        EncounterPreviewSnapshot snapshot,
        bool wasCacheHit,
        string cacheMissReason,
        EncounterPreviewCompileResult? compileResult,
        double cacheReadMilliseconds,
        double compileMilliseconds
    )
    {
        Snapshot = snapshot;
        WasCacheHit = wasCacheHit;
        CacheMissReason = cacheMissReason;
        CompileResult = compileResult;
        CacheReadMilliseconds = cacheReadMilliseconds;
        CompileMilliseconds = compileMilliseconds;
    }

    public EncounterPreviewSnapshot Snapshot { get; }

    public bool WasCacheHit { get; }

    public string CacheMissReason { get; }

    public EncounterPreviewCompileResult? CompileResult { get; }

    public double CacheReadMilliseconds { get; }

    public double CompileMilliseconds { get; }
}

internal sealed class EncounterPreviewPlanLoader
{
    private readonly EncounterPreviewCacheStore _cacheStore;
    private readonly Func<
        IReadOnlyDictionary<Guid, ITCard>,
        IReadOnlyDictionary<int, TLevelUp>,
        EncounterPreviewCompileResult
    > _compile;

    public EncounterPreviewPlanLoader(
        EncounterPreviewCacheStore cacheStore,
        Func<
            IReadOnlyDictionary<Guid, ITCard>,
            IReadOnlyDictionary<int, TLevelUp>,
            EncounterPreviewCompileResult
        > compile
    )
    {
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _compile = compile ?? throw new ArgumentNullException(nameof(compile));
    }

    public async Task<EncounterPreviewPlanLoadResult> LoadAsync(
        EncounterPreviewCacheIdentity identity,
        Func<Task<Dictionary<Guid, ITCard>?>> loadCardMap,
        Func<Dictionary<int, TLevelUp>?> loadLevelUps,
        CancellationToken cancellationToken
    )
    {
        if (identity == null)
            throw new ArgumentNullException(nameof(identity));
        if (loadCardMap == null)
            throw new ArgumentNullException(nameof(loadCardMap));
        if (loadLevelUps == null)
            throw new ArgumentNullException(nameof(loadLevelUps));

        var cacheStarted = Stopwatch.GetTimestamp();
        if (_cacheStore.TryLoad(identity, out var cached, out var missReason))
        {
            return new EncounterPreviewPlanLoadResult(
                cached!,
                wasCacheHit: true,
                cacheMissReason: string.Empty,
                compileResult: null,
                ElapsedMilliseconds(cacheStarted),
                compileMilliseconds: 0
            );
        }

        var cacheReadMilliseconds = ElapsedMilliseconds(cacheStarted);
        cancellationToken.ThrowIfCancellationRequested();
        var map = await loadCardMap().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (map == null)
            throw new InvalidOperationException("The shared static card map load returned null.");
        var levelUps = loadLevelUps();
        if (levelUps == null)
            throw new InvalidOperationException("The static level-up snapshot returned null.");

        var compileStarted = Stopwatch.GetTimestamp();
        var compileResult = _compile(map, levelUps);
        return new EncounterPreviewPlanLoadResult(
            compileResult.Snapshot,
            wasCacheHit: false,
            missReason,
            compileResult,
            cacheReadMilliseconds,
            ElapsedMilliseconds(compileStarted)
        );
    }

    private static double ElapsedMilliseconds(long startedAt) =>
        (Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency;
}
