#nullable enable
using System.Diagnostics;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.PvpBattles;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.Warmup;

internal static class PresentationWarmer
{
    internal static readonly ECardSize[] WarmupCardSizes =
    {
        ECardSize.Small,
        ECardSize.Medium,
        ECardSize.Large,
    };

    internal static async Task WarmPresentationAssetsAsync(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var stats = new ReplayWarmupStats();
        try
        {
            await WarmAssetLoaderAsync(manifest, sequence, stats, outcome);
            await CombatVfxWarmer.WarmCombatVfxAsync(sequence, stats, outcome);
        }
        finally
        {
            stopwatch.Stop();
            ReplayWarmupLogging.PresentationCompleted(
                outcome.BattleId,
                stopwatch.ElapsedMilliseconds,
                stats
            );
        }
    }

    private static async Task WarmAssetLoaderAsync(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        ReplayWarmupStats stats,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        Services.TryGet<AssetLoader>(out var assetLoader);
        if (assetLoader == null)
        {
            outcome.ReportDegradation(ReplayPlaybackReasonCode.PresentationWarmupFailed);
            return;
        }

        if (WarmupCache.TryReserveSharedAssetsPreload())
        {
            try
            {
                await assetLoader.PreloadAssets();
                stats.SharedAssetsPreloaded++;
            }
            catch (Exception ex)
            {
                WarmupCache.ReleaseSharedAssetsPreload();
                outcome.ReportDegradation(ReplayPlaybackReasonCode.PresentationWarmupFailed, ex);
            }
        }
        else
        {
            stats.SharedAssetsSkipped++;
        }

        var preloadRequests = new Dictionary<string, (Guid TemplateId, ECardSize Size)>(
            StringComparer.Ordinal
        );

        foreach (var snapshot in EnumerateItemSnapshots(manifest))
        {
            if (!Guid.TryParse(snapshot.TemplateId, out var templateId))
                continue;

            var key = $"{templateId:N}:{snapshot.Size}";
            preloadRequests.TryAdd(key, (templateId, snapshot.Size));
        }

        var cardSemaphore = new SemaphoreSlim(WarmupConstants.ReplayWarmupConcurrency);
        var cardWarmupTasks = preloadRequests.Select(request =>
            WarmCardAsync(assetLoader, request.Key, request.Value, cardSemaphore, stats, outcome)
        );
        await Task.WhenAll(cardWarmupTasks);

        var overrideSemaphore = new SemaphoreSlim(WarmupConstants.ReplayWarmupConcurrency);
        var overrideWarmupTasks = sequence
            .CombatMessage.Data.VfxKeys.Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .Select(overrideKey =>
                WarmOverrideAssetAsync(assetLoader, overrideKey, overrideSemaphore, stats, outcome)
            );
        await Task.WhenAll(overrideWarmupTasks);
    }

    private static async Task WarmCardAsync(
        AssetLoader assetLoader,
        string cacheKey,
        (Guid TemplateId, ECardSize Size) request,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        if (!WarmupCache.TryReserveCacheKey(WarmupCache.PreloadedCardKeys, cacheKey))
        {
            stats.CardsSkipped++;
            return;
        }

        await semaphore.WaitAsync();
        try
        {
            await assetLoader.PreloadCard(request.TemplateId, request.Size);
            stats.CardsPreloaded++;
        }
        catch (Exception ex)
        {
            WarmupCache.ReleaseCacheKey(WarmupCache.PreloadedCardKeys, cacheKey);
            stats.CardsFailed++;
            outcome.ReportDegradation(ReplayPlaybackReasonCode.PresentationWarmupFailed, ex);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task WarmOverrideAssetAsync(
        AssetLoader assetLoader,
        string overrideKey,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        if (!WarmupCache.TryReserveCacheKey(WarmupCache.PreloadedOverrideKeys, overrideKey))
        {
            stats.OverrideAssetsSkipped++;
            return;
        }

        await semaphore.WaitAsync();
        try
        {
            _ = await assetLoader.LoadAssetAsyncByAddress<GameObject>(overrideKey);
            stats.OverrideAssetsPreloaded++;
        }
        catch (Exception ex)
        {
            WarmupCache.ReleaseCacheKey(WarmupCache.PreloadedOverrideKeys, overrideKey);
            stats.OverrideAssetsFailed++;
            outcome.ReportDegradation(ReplayPlaybackReasonCode.PresentationWarmupFailed, ex);
            ReplayWarmupLogging.AssetSkipped(
                ReplayWarmupStage.Presentation,
                overrideKey,
                ReplayWarmupAssetReasonCode.AssetLoadFailed,
                ex
            );
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static IEnumerable<PvpBattleCardSnapshot> EnumerateItemSnapshots(
        PvpBattleManifest manifest
    )
    {
        foreach (
            var capture in new[] { manifest.Snapshots.PlayerHand, manifest.Snapshots.OpponentHand }
        )
        {
            if (capture.Status == PvpBattleCaptureStatus.Missing || capture.Items == null)
                continue;

            foreach (var snapshot in capture.Items)
            {
                if (snapshot?.Type == ECardType.Item)
                    yield return snapshot;
            }
        }
    }
}
