#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Assets.Scripts.Audio;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarGameShared.TempoNet.Enums;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.Game.PvpBattles;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed partial class CombatReplayRuntime
{
    private static readonly ECardSize[] ReplayWarmupCardSizes =
    {
        ECardSize.Small,
        ECardSize.Medium,
        ECardSize.Large,
    };

    private static async Task WarmReplayPresentationAssetsAsync(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var stats = new ReplayWarmupStats();
        await WarmReplayAssetLoaderAsync(manifest, sequence, stats);
        await WarmReplayCombatVfxAsync(sequence, stats);
        stopwatch.Stop();
        BppLog.Info(
            "CombatReplayRuntime",
            $"Saved replay warmup finished in {stopwatch.ElapsedMilliseconds}ms "
                + $"sharedAssets(preloaded={stats.SharedAssetsPreloaded}, skipped={stats.SharedAssetsSkipped}) "
                + $"cards(preloaded={stats.CardsPreloaded}, skipped={stats.CardsSkipped}, failed={stats.CardsFailed}) "
                + $"overrideAssets(preloaded={stats.OverrideAssetsPreloaded}, skipped={stats.OverrideAssetsSkipped}, failed={stats.OverrideAssetsFailed}) "
                + $"combatVfx(prewarmed={stats.VfxPrewarmed}, skipped={stats.VfxSkipped}, failed={stats.VfxFailed})"
        );
    }

    private static async Task WarmReplayAssetLoaderAsync(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        ReplayWarmupStats stats
    )
    {
        Services.TryGet<AssetLoader>(out var assetLoader);
        if (assetLoader == null)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                "Saved replay visual warmup skipped because AssetLoader is unavailable."
            );
            return;
        }

        if (TryReserveReplaySharedAssetsPreload())
        {
            try
            {
                await assetLoader.PreloadAssets();
                stats.SharedAssetsPreloaded++;
            }
            catch (Exception ex)
            {
                ReleaseReplaySharedAssetsPreload();
                BppLog.Warn(
                    "CombatReplayRuntime",
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

        foreach (var snapshot in EnumerateReplayItemSnapshots(manifest))
        {
            if (!Guid.TryParse(snapshot.TemplateId, out var templateId))
                continue;

            var key = $"{templateId:N}:{snapshot.Size}";
            preloadRequests.TryAdd(key, (templateId, snapshot.Size));
        }

        var cardSemaphore = new SemaphoreSlim(ReplayWarmupConcurrency);
        var cardWarmupTasks = preloadRequests.Select(request =>
            WarmReplayCardAsync(assetLoader, request.Key, request.Value, cardSemaphore, stats)
        );
        await Task.WhenAll(cardWarmupTasks);

        var overrideSemaphore = new SemaphoreSlim(ReplayWarmupConcurrency);
        var overrideWarmupTasks = sequence
            .CombatMessage.Data.VfxKeys.Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .Select(overrideKey =>
                WarmReplayOverrideAssetAsync(assetLoader, overrideKey, overrideSemaphore, stats)
            );
        await Task.WhenAll(overrideWarmupTasks);
    }

    private static async Task WarmReplayCardAsync(
        AssetLoader assetLoader,
        string cacheKey,
        (Guid TemplateId, ECardSize Size) request,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        if (!TryReserveReplayCacheKey(ReplayPreloadedCardKeys, cacheKey))
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
            ReleaseReplayCacheKey(ReplayPreloadedCardKeys, cacheKey);
            stats.CardsFailed++;
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay card preload failed for template={request.TemplateId} size={request.Size}: {ex.Message}"
            );
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task WarmReplayOverrideAssetAsync(
        AssetLoader assetLoader,
        string overrideKey,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        if (!TryReserveReplayCacheKey(ReplayPreloadedOverrideKeys, overrideKey))
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
            ReleaseReplayCacheKey(ReplayPreloadedOverrideKeys, overrideKey);
            stats.OverrideAssetsFailed++;
            BppLog.Debug(
                "CombatReplayRuntime",
                $"Saved replay override VFX preload skipped for '{overrideKey}': {ex.Message}"
            );
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static IEnumerable<CombatReplayCardSnapshot> EnumerateReplayItemSnapshots(
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

    private static async Task WarmReplayCombatVfxAsync(
        CombatSequenceMessages sequence,
        ReplayWarmupStats stats
    )
    {
        Services.TryGet<AssetLoader>(out var assetLoader);
        Services.TryGet<VFXManager>(out var vfxManager);
        if (assetLoader == null || vfxManager == null)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                "Saved replay combat VFX warmup skipped because replay asset services are unavailable."
            );
            return;
        }

        var actionTypes = sequence
            .CombatMessage.Data.Frames.SelectMany(frame =>
                frame?.Events ?? Enumerable.Empty<ICombatSimEvent>()
            )
            .OfType<CombatSimEventEffectExecuted>()
            .Select(evt => DTOUtils.GetActionType(evt.ActionType))
            .Where(action => action != ActionType.Unknown)
            .Distinct()
            .ToList();
        var vfxSemaphore = new SemaphoreSlim(ReplayWarmupConcurrency);
        var vfxTasks = new List<Task>();

        foreach (var action in actionTypes)
        {
            vfxTasks.Add(
                WarmReplayActionVfxAsync(assetLoader, vfxManager, action, vfxSemaphore, stats)
            );
        }

        foreach (
            var overrideKey in sequence
                .CombatMessage.Data.VfxKeys.Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
        )
        {
            vfxTasks.Add(
                WarmReplayOverrideVfxAsync(
                    assetLoader,
                    vfxManager,
                    actionTypes,
                    overrideKey,
                    vfxSemaphore,
                    stats
                )
            );
        }
        await Task.WhenAll(vfxTasks);
    }

    private static async Task WarmReplayActionVfxAsync(
        AssetLoader assetLoader,
        VFXManager vfxManager,
        ActionType action,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        var vfxConfig = GetReplayVfxConfig(vfxManager);
        if (vfxConfig == null)
        {
            await WarmReplayVfxReferenceAsync(
                assetLoader,
                vfxManager.GetVFX(action),
                semaphore,
                stats
            );
            return;
        }

        if (TryIsActionAttributeMapped(vfxConfig, action))
        {
            foreach (var size in ReplayWarmupCardSizes)
            {
                await WarmReplayVfxReferenceAsync(
                    assetLoader,
                    TryGetMappedActionVfx(vfxConfig, size, action),
                    semaphore,
                    stats
                );
            }
        }

        await WarmReplayVfxReferenceAsync(assetLoader, vfxManager.GetVFX(action), semaphore, stats);
    }

    private static async Task WarmReplayOverrideVfxAsync(
        AssetLoader assetLoader,
        VFXManager vfxManager,
        IReadOnlyCollection<ActionType> actionTypes,
        string overrideKey,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        var vfxConfig = GetReplayVfxConfig(vfxManager);
        if (vfxConfig == null)
            return;

        foreach (var action in actionTypes)
        {
            foreach (var size in ReplayWarmupCardSizes)
            {
                await WarmReplayVfxReferenceAsync(
                    assetLoader,
                    await TryGetOverrideActionVfxAsync(vfxConfig, action, size, overrideKey),
                    semaphore,
                    stats
                );
            }
        }
    }

    private static object? GetReplayVfxConfig(VFXManager vfxManager)
    {
        return vfxManager
            .GetType()
            .GetField(
                "vfxManagerSO",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )
            ?.GetValue(vfxManager);
    }

    private static bool TryIsActionAttributeMapped(object vfxConfig, ActionType action)
    {
        var method = vfxConfig
            .GetType()
            .GetMethod(
                "IsActionAttributeMapped",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        return method?.Invoke(vfxConfig, new object[] { action }) as bool? == true;
    }

    private static AssetReference? TryGetMappedActionVfx(
        object vfxConfig,
        ECardSize size,
        ActionType action
    )
    {
        var method = vfxConfig
            .GetType()
            .GetMethod(
                "GetVFX",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(ECardSize), typeof(ActionType) },
                null
            );
        return method?.Invoke(vfxConfig, new object[] { size, action }) as AssetReference;
    }

    private static async Task<AssetReference?> TryGetOverrideActionVfxAsync(
        object vfxConfig,
        ActionType action,
        ECardSize size,
        string overrideKey
    )
    {
        var method = vfxConfig
            .GetType()
            .GetMethod(
                "GetActionOverrideVFX",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(ActionType), typeof(ECardSize), typeof(string) },
                null
            );
        if (method == null)
            return null;

        var taskObject = method.Invoke(vfxConfig, new object[] { action, size, overrideKey });
        if (taskObject is Task<AssetReference> typedTask)
            return await typedTask;

        if (taskObject is not Task task)
            return taskObject as AssetReference;

        await task;
        return task.GetType()
                .GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(task) as AssetReference;
    }

    private static async Task WarmReplayVfxReferenceAsync(
        AssetLoader assetLoader,
        AssetReference? assetReference,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        if (assetReference == null || !assetReference.RuntimeKeyIsValid())
            return;

        var key = !string.IsNullOrWhiteSpace(assetReference.AssetGUID)
            ? assetReference.AssetGUID
            : assetReference.ToString();
        if (!TryReserveReplayCacheKey(ReplayPrewarmedVfxKeys, key))
        {
            stats.VfxSkipped++;
            return;
        }

        await semaphore.WaitAsync();
        try
        {
            _ = await assetLoader.LoadAssetAsyncByReference<GameObject>(assetReference);
            stats.VfxPrewarmed++;
        }
        catch (Exception ex)
        {
            ReleaseReplayCacheKey(ReplayPrewarmedVfxKeys, key);
            stats.VfxFailed++;
            BppLog.Debug(
                "CombatReplayRuntime",
                $"Saved replay VFX warmup skipped for '{key}': {ex.Message}"
            );
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static bool TryReserveReplaySharedAssetsPreload()
    {
        lock (ReplayWarmupCacheLock)
        {
            if (ReplaySharedAssetsPreloaded)
                return false;

            ReplaySharedAssetsPreloaded = true;
            return true;
        }
    }

    private static void ReleaseReplaySharedAssetsPreload()
    {
        lock (ReplayWarmupCacheLock)
        {
            ReplaySharedAssetsPreloaded = false;
        }
    }

    private static bool TryReserveReplayCacheKey(HashSet<string> cache, string key)
    {
        lock (ReplayWarmupCacheLock)
        {
            return cache.Add(key);
        }
    }

    private static void ReleaseReplayCacheKey(HashSet<string> cache, string key)
    {
        lock (ReplayWarmupCacheLock)
        {
            cache.Remove(key);
        }
    }

    private sealed class ReplayWarmupStats
    {
        public int SharedAssetsPreloaded;
        public int SharedAssetsSkipped;
        public int CardsPreloaded;
        public int CardsSkipped;
        public int CardsFailed;
        public int OverrideAssetsPreloaded;
        public int OverrideAssetsSkipped;
        public int OverrideAssetsFailed;
        public int VfxPrewarmed;
        public int VfxSkipped;
        public int VfxFailed;
    }

    private static async Task WarmReplayAudioBanksAsync()
    {
        try
        {
            var soundManager = Services.Get<SoundManager>();
            if (soundManager == null)
            {
                BppLog.Warn(
                    "CombatReplayRuntime",
                    "Saved replay audio warmup skipped because SoundManager is unavailable."
                );
                return;
            }

            var stats = new ReplayAudioWarmupStats();
            var collectionManager = Services.Get<CollectionManager>();
            if (collectionManager == null)
            {
                BppLog.Warn(
                    "CombatReplayRuntime",
                    "Saved replay audio warmup cannot resolve equipped board audio because CollectionManager is unavailable."
                );
            }

            var boardAssets = UnityEngine
                .Object.FindObjectsOfType<HeroBoardController>(true)
                .Where(controller =>
                    controller != null && controller.gameObject.scene.rootCount > 0
                )
                .Select(controller => controller.AssociatedDataSO)
                .Where(asset => asset != null)
                .Distinct()
                .ToList();

            var playerBoard = await TryGetReplayPlayerBoardAsync(collectionManager);
            AddReplayBoardAsset(boardAssets, playerBoard);

            var opponentBoard = await TryGetReplayOpponentBoardAsync(collectionManager);
            AddReplayBoardAsset(boardAssets, opponentBoard);

            if (boardAssets.Count == 0)
            {
                BppLog.Warn(
                    "CombatReplayRuntime",
                    "Saved replay audio warmup found no player or opponent board assets."
                );
            }

            foreach (var boardAsset in boardAssets)
            {
                await WarmReplayBoardAudioAsync(soundManager, boardAsset!, stats);
            }

            await WarmReplaySoundtracksAsync(soundManager, collectionManager, boardAssets, stats);

            BppLog.Info(
                "CombatReplayRuntime",
                "Saved replay audio warmup finished: "
                    + $"boardBanks(loaded={stats.BoardBanksLoaded}, alreadyLoaded={stats.BoardBanksAlreadyLoaded}, failed={stats.BoardBanksFailed}, skipped={stats.BoardBanksSkipped}) "
                    + $"soundtrackBanks(loaded={stats.SoundtrackBanksLoaded}, alreadyLoaded={stats.SoundtrackBanksAlreadyLoaded}, failed={stats.SoundtrackBanksFailed}, skipped={stats.SoundtrackBanksSkipped})"
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn("CombatReplayRuntime", $"Saved replay audio warmup failed: {ex.Message}");
        }
    }

    private static async Task<BoardAssetDataSO?> TryGetReplayPlayerBoardAsync(
        CollectionManager? collectionManager
    )
    {
        if (collectionManager == null)
            return null;

        try
        {
            return await collectionManager.GetEquippedBoard();
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay audio warmup could not resolve player board audio: {ex.Message}"
            );
            return null;
        }
    }

    private static async Task<BoardAssetDataSO?> TryGetReplayOpponentBoardAsync(
        CollectionManager? collectionManager
    )
    {
        var loadout = Data.SimPvpOpponent?.PlayerLoadout;
        if (collectionManager == null || loadout == null)
            return null;

        try
        {
#pragma warning disable CS0618
            return await collectionManager.GetEquippedBoard(loadout);
#pragma warning restore CS0618
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay audio warmup could not resolve opponent board audio: {ex.Message}"
            );
            return null;
        }
    }

    private static void AddReplayBoardAsset(
        ICollection<BoardAssetDataSO> boardAssets,
        BoardAssetDataSO? boardAsset
    )
    {
        if (
            boardAsset == null
            || boardAssets.Any(existing => ReferenceEquals(existing, boardAsset))
        )
            return;

        boardAssets.Add(boardAsset);
    }

    private static async Task WarmReplayBoardAudioAsync(
        SoundManager soundManager,
        BoardAssetDataSO boardAsset,
        ReplayAudioWarmupStats stats
    )
    {
        if (string.IsNullOrWhiteSpace(boardAsset.boardBank))
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Board '{boardAsset.name}' has no boardBank; replay SFX may be incomplete."
            );
            stats.BoardBanksSkipped++;
            return;
        }

        if (string.IsNullOrWhiteSpace(boardAsset.boardAssetBank))
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Board '{boardAsset.name}' has no boardAssetBank; replay SFX may be incomplete."
            );
            stats.BoardBanksSkipped++;
            return;
        }

        var wasMetadataLoaded = soundManager.IsBankLoaded(boardAsset.boardBank, isMetadata: false);
        var wasAssetLoaded = soundManager.IsBankLoaded(boardAsset.boardAssetBank, isMetadata: false);
        BppLog.Info(
            "CombatReplayRuntime",
            $"Warm replay audio bank: board='{boardAsset.name}', metadata='{boardAsset.boardBank}', asset='{boardAsset.boardAssetBank}'"
        );
        var loaded = await soundManager.LoadBankAsync(
            FModBank.EBankType.SFX,
            boardAsset.boardBank,
            boardAsset.boardAssetBank
        );
        if (!loaded)
        {
            stats.BoardBanksFailed++;
            return;
        }

        if (wasMetadataLoaded && wasAssetLoaded)
            stats.BoardBanksAlreadyLoaded++;
        else
            stats.BoardBanksLoaded++;
    }

    private static async Task WarmReplaySoundtracksAsync(
        SoundManager soundManager,
        CollectionManager? collectionManager,
        IReadOnlyCollection<BoardAssetDataSO> boardAssets,
        ReplayAudioWarmupStats stats
    )
    {
        var warmedAny = false;
        var warmedSoundtracks = new HashSet<string>(StringComparer.Ordinal);
        warmedAny |= await WarmReplaySoundtrackAsync(
            soundManager,
            await TryGetReplaySoundtrackAsync(collectionManager),
            stats,
            warmedSoundtracks,
            setPlayingSoundtrack: true
        );

        foreach (var boardAsset in boardAssets)
        {
            warmedAny |= await WarmReplaySoundtrackAsync(
                soundManager,
                boardAsset.soundtrack,
                stats,
                warmedSoundtracks,
                setPlayingSoundtrack: soundManager.PlayingSoundTrackSO == null
            );
        }

        if (!warmedAny)
        {
            stats.SoundtrackBanksSkipped++;
            BppLog.Warn(
                "CombatReplayRuntime",
                "Saved replay audio warmup could not resolve any soundtrack; replay combat music fallback may be incomplete."
            );
        }
    }

    private static async Task<bool> WarmReplaySoundtrackAsync(
        SoundManager soundManager,
        SoundtrackSO? soundtrack,
        ReplayAudioWarmupStats stats,
        ISet<string> warmedSoundtracks,
        bool setPlayingSoundtrack
    )
    {
        if (soundtrack == null)
            return false;

        var key = !string.IsNullOrWhiteSpace(soundtrack.SoundtrackPath)
            ? soundtrack.SoundtrackPath
            : soundtrack.name;
        if (!string.IsNullOrWhiteSpace(key) && warmedSoundtracks.Contains(key))
            return true;

        var loadedSoundtrack = await TryLoadReplaySoundtrackAssetAsync(soundtrack);
        if (loadedSoundtrack == null)
        {
            stats.SoundtrackBanksFailed++;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(key))
            warmedSoundtracks.Add(key);

        if (loadedSoundtrack.MusicTracks == null || loadedSoundtrack.MusicTracks.Length == 0)
        {
            stats.SoundtrackBanksSkipped++;
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay soundtrack '{loadedSoundtrack.name}' has no music tracks to warm."
            );
            return false;
        }

        if (setPlayingSoundtrack)
            soundManager.PlayingSoundTrackSO = loadedSoundtrack;

        for (uint trackIndex = 0; trackIndex < loadedSoundtrack.MusicTracks.Length; trackIndex++)
        {
            await WarmReplaySoundtrackTrackAsync(
                soundManager,
                loadedSoundtrack,
                trackIndex,
                stats
            );
        }

        return true;
    }

    private static async Task<SoundtrackSO?> TryGetReplaySoundtrackAsync(
        CollectionManager? collectionManager
    )
    {
        if (collectionManager == null)
            return null;

        try
        {
            var soundtrack = await collectionManager.GetEquippedSoundtrack();
            return soundtrack != null ? soundtrack.SoundtrackObject : null;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay audio warmup could not resolve equipped soundtrack: {ex.Message}"
            );
            return null;
        }
    }

    private static async Task<SoundtrackSO?> TryLoadReplaySoundtrackAssetAsync(
        SoundtrackSO soundtrack
    )
    {
        if (string.IsNullOrWhiteSpace(soundtrack.SoundtrackPath))
            return soundtrack;

        try
        {
            var handle = Addressables.LoadAssetAsync<SoundtrackSO>(soundtrack.SoundtrackPath);
            await handle.Task;
            if (
                handle.Status
                == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded
            )
                return handle.Result;

            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay soundtrack load failed for path '{soundtrack.SoundtrackPath}'."
            );
            return soundtrack;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay soundtrack load failed for path '{soundtrack.SoundtrackPath}': {ex.Message}"
            );
            return soundtrack;
        }
    }

    private static bool TryGetReplaySoundtrackTrackBanks(
        SoundtrackSO soundtrack,
        uint trackIndex,
        out string? metadataBank,
        out string? assetBank
    )
    {
        metadataBank = null;
        assetBank = null;

        try
        {
            var soundtrackType = soundtrack.GetType();
            var trackBankNameMethod = soundtrackType.GetMethod(
                "TrackBankName",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(uint), typeof(bool) },
                null
            );
            if (trackBankNameMethod != null)
            {
                metadataBank = trackBankNameMethod.Invoke(soundtrack, new object[] { trackIndex, false })
                    as string;
                assetBank = trackBankNameMethod.Invoke(soundtrack, new object[] { trackIndex, true })
                    as string;
                return !string.IsNullOrWhiteSpace(metadataBank)
                    && !string.IsNullOrWhiteSpace(assetBank);
            }

            trackBankNameMethod = soundtrackType.GetMethod(
                "TrackBankName",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(uint) },
                null
            );
            if (trackBankNameMethod == null)
                return false;

            metadataBank = trackBankNameMethod.Invoke(soundtrack, new object[] { trackIndex }) as string;
            assetBank = string.IsNullOrWhiteSpace(metadataBank) ? null : metadataBank + ".assets";
            return !string.IsNullOrWhiteSpace(metadataBank) && !string.IsNullOrWhiteSpace(assetBank);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay soundtrack '{soundtrack.name}' track {trackIndex} bank metadata lookup failed: {ex.Message}"
            );
            return false;
        }
    }

    private static async Task WarmReplaySoundtrackTrackAsync(
        SoundManager soundManager,
        SoundtrackSO soundtrack,
        uint trackIndex,
        ReplayAudioWarmupStats stats
    )
    {
        if (
            !TryGetReplaySoundtrackTrackBanks(
                soundtrack,
                trackIndex,
                out var metadataBank,
                out var assetBank
            )
        )
        {
            stats.SoundtrackBanksSkipped++;
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay soundtrack '{soundtrack.name}' track {trackIndex} has incomplete bank metadata."
            );
            return;
        }

        var wasMetadataLoaded = soundManager.IsBankLoaded(metadataBank, isMetadata: false);
        var wasAssetLoaded = soundManager.IsBankLoaded(assetBank, isMetadata: false);
        var loaded = await soundManager.LoadBankAsync(
            FModBank.EBankType.Music,
            metadataBank,
            assetBank
        );
        if (!loaded)
        {
            stats.SoundtrackBanksFailed++;
            return;
        }

        if (wasMetadataLoaded && wasAssetLoaded)
            stats.SoundtrackBanksAlreadyLoaded++;
        else
            stats.SoundtrackBanksLoaded++;
    }

    private static void EnsureReplayAudioUnpaused()
    {
        var gameServiceManager = Singleton<GameServiceManager>.Instance;
        if (gameServiceManager == null || !gameServiceManager.GamePaused)
            return;

        BppLog.Info(
            "CombatReplayRuntime",
            "Saved replay playback found gameplay paused; unpausing audio buses before combat simulation."
        );
        gameServiceManager.PauseOrUnpauseGame(toPauseOrUnpause: false);
    }

    private sealed class ReplayAudioWarmupStats
    {
        public int BoardBanksLoaded;
        public int BoardBanksAlreadyLoaded;
        public int BoardBanksFailed;
        public int BoardBanksSkipped;
        public int SoundtrackBanksLoaded;
        public int SoundtrackBanksAlreadyLoaded;
        public int SoundtrackBanksFailed;
        public int SoundtrackBanksSkipped;
    }
}
