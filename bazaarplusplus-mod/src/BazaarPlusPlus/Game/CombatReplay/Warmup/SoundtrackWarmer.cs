#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Assets.Scripts.Audio;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine.AddressableAssets;

namespace BazaarPlusPlus.Game.CombatReplay.Warmup;

internal static class SoundtrackWarmer
{
    internal static async Task WarmSoundtracksAsync(
        SoundManager soundManager,
        CollectionManager? collectionManager,
        IReadOnlyCollection<BoardAssetDataSO> boardAssets,
        ReplayAudioWarmupStats stats
    )
    {
        var warmedAny = false;
        var warmedSoundtracks = new HashSet<string>(StringComparer.Ordinal);
        warmedAny |= await WarmSoundtrackAsync(
            soundManager,
            await TryGetSoundtrackAsync(collectionManager),
            stats,
            warmedSoundtracks,
            setPlayingSoundtrack: true
        );

        foreach (var boardAsset in boardAssets)
        {
            warmedAny |= await WarmSoundtrackAsync(
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
                "SoundtrackWarmer",
                "Saved replay audio warmup could not resolve any soundtrack; replay combat music fallback may be incomplete."
            );
        }
    }

    internal static async Task WarmBoardAudioAsync(
        SoundManager soundManager,
        BoardAssetDataSO boardAsset,
        ReplayAudioWarmupStats stats
    )
    {
        if (string.IsNullOrWhiteSpace(boardAsset.boardBank))
        {
            BppLog.Warn(
                "SoundtrackWarmer",
                $"Board '{boardAsset.name}' has no boardBank; replay SFX may be incomplete."
            );
            stats.BoardBanksSkipped++;
            return;
        }

        if (string.IsNullOrWhiteSpace(boardAsset.boardAssetBank))
        {
            BppLog.Warn(
                "SoundtrackWarmer",
                $"Board '{boardAsset.name}' has no boardAssetBank; replay SFX may be incomplete."
            );
            stats.BoardBanksSkipped++;
            return;
        }

        var wasMetadataLoaded = soundManager.IsBankLoaded(boardAsset.boardBank, isMetadata: false);
        var wasAssetLoaded = soundManager.IsBankLoaded(
            boardAsset.boardAssetBank,
            isMetadata: false
        );
        BppLog.Info(
            "SoundtrackWarmer",
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

    internal static void AddBoardAsset(
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

    internal static async Task<BoardAssetDataSO?> TryGetPlayerBoardAsync(
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
                "SoundtrackWarmer",
                $"Saved replay audio warmup could not resolve player board audio: {ex.Message}"
            );
            return null;
        }
    }

    internal static async Task<BoardAssetDataSO?> TryGetOpponentBoardAsync(
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
                "SoundtrackWarmer",
                $"Saved replay audio warmup could not resolve opponent board audio: {ex.Message}"
            );
            return null;
        }
    }

    private static async Task<bool> WarmSoundtrackAsync(
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

        var loadedSoundtrack = await TryLoadSoundtrackAssetAsync(soundtrack);
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
                "SoundtrackWarmer",
                $"Saved replay soundtrack '{loadedSoundtrack.name}' has no music tracks to warm."
            );
            return false;
        }

        if (setPlayingSoundtrack)
            soundManager.PlayingSoundTrackSO = loadedSoundtrack;

        for (uint trackIndex = 0; trackIndex < loadedSoundtrack.MusicTracks.Length; trackIndex++)
        {
            await WarmSoundtrackTrackAsync(soundManager, loadedSoundtrack, trackIndex, stats);
        }

        return true;
    }

    private static async Task<SoundtrackSO?> TryGetSoundtrackAsync(
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
                "SoundtrackWarmer",
                $"Saved replay audio warmup could not resolve equipped soundtrack: {ex.Message}"
            );
            return null;
        }
    }

    private static async Task<SoundtrackSO?> TryLoadSoundtrackAssetAsync(SoundtrackSO soundtrack)
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
                "SoundtrackWarmer",
                $"Saved replay soundtrack load failed for path '{soundtrack.SoundtrackPath}'."
            );
            return soundtrack;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "SoundtrackWarmer",
                $"Saved replay soundtrack load failed for path '{soundtrack.SoundtrackPath}': {ex.Message}"
            );
            return soundtrack;
        }
    }

    private static bool TryGetSoundtrackTrackBanks(
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
                metadataBank =
                    trackBankNameMethod.Invoke(soundtrack, new object[] { trackIndex, false })
                    as string;
                assetBank =
                    trackBankNameMethod.Invoke(soundtrack, new object[] { trackIndex, true })
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

            metadataBank =
                trackBankNameMethod.Invoke(soundtrack, new object[] { trackIndex }) as string;
            assetBank = string.IsNullOrWhiteSpace(metadataBank) ? null : metadataBank + ".assets";
            return !string.IsNullOrWhiteSpace(metadataBank)
                && !string.IsNullOrWhiteSpace(assetBank);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "SoundtrackWarmer",
                $"Saved replay soundtrack '{soundtrack.name}' track {trackIndex} bank metadata lookup failed: {ex.Message}"
            );
            return false;
        }
    }

    private static async Task WarmSoundtrackTrackAsync(
        SoundManager soundManager,
        SoundtrackSO soundtrack,
        uint trackIndex,
        ReplayAudioWarmupStats stats
    )
    {
        if (
            !TryGetSoundtrackTrackBanks(
                soundtrack,
                trackIndex,
                out var metadataBank,
                out var assetBank
            )
        )
        {
            stats.SoundtrackBanksSkipped++;
            BppLog.Warn(
                "SoundtrackWarmer",
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
}
