#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
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
        CombatSequenceMessages sequence
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var stats = new ReplayWarmupStats();
        await WarmAssetLoaderAsync(manifest, sequence, stats);
        await CombatVfxWarmer.WarmCombatVfxAsync(sequence, stats);
        stopwatch.Stop();
        BppLog.Info(
            "PresentationWarmer",
            $"Saved replay warmup finished in {stopwatch.ElapsedMilliseconds}ms "
                + $"sharedAssets(preloaded={stats.SharedAssetsPreloaded}, skipped={stats.SharedAssetsSkipped}) "
                + $"cards(preloaded={stats.CardsPreloaded}, skipped={stats.CardsSkipped}, failed={stats.CardsFailed}) "
                + $"overrideAssets(preloaded={stats.OverrideAssetsPreloaded}, skipped={stats.OverrideAssetsSkipped}, failed={stats.OverrideAssetsFailed}) "
                + $"combatVfx(prewarmed={stats.VfxPrewarmed}, skipped={stats.VfxSkipped}, failed={stats.VfxFailed})"
        );
    }

    private static async Task WarmAssetLoaderAsync(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        ReplayWarmupStats stats
    )
    {
        Services.TryGet<AssetLoader>(out var assetLoader);
        if (assetLoader == null)
        {
            BppLog.Warn(
                "PresentationWarmer",
                "Saved replay visual warmup skipped because AssetLoader is unavailable."
            );
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
                BppLog.Warn(
                    "PresentationWarmer",
                    $"Saved replay asset preload failed: {ex.Message}"
                );
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
            WarmCardAsync(assetLoader, request.Key, request.Value, cardSemaphore, stats)
        );
        await Task.WhenAll(cardWarmupTasks);

        var overrideSemaphore = new SemaphoreSlim(WarmupConstants.ReplayWarmupConcurrency);
        var overrideWarmupTasks = sequence
            .CombatMessage.Data.VfxKeys.Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .Select(overrideKey =>
                WarmOverrideAssetAsync(assetLoader, overrideKey, overrideSemaphore, stats)
            );
        await Task.WhenAll(overrideWarmupTasks);
    }

    private static async Task WarmCardAsync(
        AssetLoader assetLoader,
        string cacheKey,
        (Guid TemplateId, ECardSize Size) request,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
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
            BppLog.Warn(
                "PresentationWarmer",
                $"Saved replay card preload failed for template={request.TemplateId} size={request.Size}: {ex.Message}"
            );
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
        ReplayWarmupStats stats
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
            BppLog.Debug(
                "PresentationWarmer",
                $"Saved replay override VFX preload skipped for '{overrideKey}': {ex.Message}"
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
