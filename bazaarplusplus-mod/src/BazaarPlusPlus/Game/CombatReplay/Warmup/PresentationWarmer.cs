#nullable enable
using System.Diagnostics;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.GameInterop.AssetLoading;
using BazaarPlusPlus.GameInterop.Cards;
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

        // The game's preload state is reset with InRun; a process-wide success flag would
        // skip the next replay after that scope releases its pools and asset handles.
        if (NativeGlobalAssetLoader.CanRunNativeSharedPreload(assetLoader))
        {
            try
            {
                await assetLoader.PreloadAssets();
                stats.SharedAssetsPreloaded++;
            }
            catch (Exception ex)
            {
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

            var size = DesktopCardFacets.GameSize(snapshot.Size);
            var key = $"{templateId:N}:{size}";
            preloadRequests.TryAdd(key, (templateId, size));
        }

        var cardSemaphore = new SemaphoreSlim(WarmupConstants.ReplayWarmupConcurrency);
        var cardWarmupTasks = preloadRequests.Values.Select(request =>
            WarmCardAsync(assetLoader, request, cardSemaphore, stats, outcome)
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
        (Guid TemplateId, ECardSize Size) request,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        await semaphore.WaitAsync();
        try
        {
            await assetLoader.PreloadCard(request.TemplateId, request.Size);
            stats.CardsPreloaded++;
        }
        catch (Exception ex)
        {
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
        // Current scope, as VFXManager instantiates it during playback: the game releases the
        // asset with that scope and dedupes repeat loads within it, so no process-level key set.
        await semaphore.WaitAsync();
        try
        {
            // VFXManager.TryInstantiateVFXByAddress instantiates the key's /Emitters/ twin when
            // that address exists, and falls back to the key itself only when the twin loads as
            // null, so the twin is the asset playback uses.
            var emitterKey = overrideKey.Replace(ProjectilesPath, EmittersPath);
            if (
                !string.Equals(emitterKey, overrideKey, StringComparison.Ordinal)
                && await TryWarmEmitterAsync(assetLoader, emitterKey)
            )
            {
                stats.OverrideAssetsPreloaded++;
                return;
            }

            await WarmOverrideAddressAsync(assetLoader, overrideKey, stats, outcome);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private const string ProjectilesPath = "/Projectiles/";
    private const string EmittersPath = "/Emitters/";

    // A missing or failed twin is not a degradation: playback falls back to the original key,
    // which is warmed next.
    private static async Task<bool> TryWarmEmitterAsync(AssetLoader assetLoader, string emitterKey)
    {
        try
        {
            if (!assetLoader.DoesAddressExist(emitterKey))
                return false;

            var asset = await NativeGlobalAssetLoader.LoadByAddressAsync<GameObject>(
                assetLoader,
                emitterKey,
                NativeAssetScopeIntent.Current
            );
            if (asset != null)
                return true;

            ReplayWarmupLogging.AssetSkipped(
                ReplayWarmupStage.Presentation,
                emitterKey,
                ReplayWarmupAssetReasonCode.AssetLoadFailed
            );
            return false;
        }
        catch (Exception ex)
        {
            ReplayWarmupLogging.AssetSkipped(
                ReplayWarmupStage.Presentation,
                emitterKey,
                ReplayWarmupAssetReasonCode.AssetLoadFailed,
                ex
            );
            return false;
        }
    }

    private static async Task WarmOverrideAddressAsync(
        AssetLoader assetLoader,
        string overrideKey,
        ReplayWarmupStats stats,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        try
        {
            var asset = await NativeGlobalAssetLoader.LoadByAddressAsync<GameObject>(
                assetLoader,
                overrideKey,
                NativeAssetScopeIntent.Current
            );
            if (asset != null)
            {
                stats.OverrideAssetsPreloaded++;
            }
            else
            {
                // The game's loader logs the cause and returns null instead of throwing.
                stats.OverrideAssetsFailed++;
                ReplayWarmupLogging.AssetSkipped(
                    ReplayWarmupStage.Presentation,
                    overrideKey,
                    ReplayWarmupAssetReasonCode.AssetLoadFailed
                );
            }
        }
        catch (Exception ex)
        {
            stats.OverrideAssetsFailed++;
            outcome.ReportDegradation(ReplayPlaybackReasonCode.PresentationWarmupFailed, ex);
            ReplayWarmupLogging.AssetSkipped(
                ReplayWarmupStage.Presentation,
                overrideKey,
                ReplayWarmupAssetReasonCode.AssetLoadFailed,
                ex
            );
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
                if (snapshot?.Type == CardType.Item)
                    yield return snapshot;
            }
        }
    }
}
