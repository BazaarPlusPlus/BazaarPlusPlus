#nullable enable
using System.Reflection;
using Assets.Scripts.Audio;
using TheBazaar;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine.AddressableAssets;

namespace BazaarPlusPlus.Game.CombatReplay.Warmup;

internal static class SoundtrackWarmer
{
    /// <summary>
    /// Warms only the soundtrack the game plays, so its banks stay on the one path the game
    /// unloads: <c>SoundManager.UnloadSoundTrack</c> on <c>RunInterrupted</c> releases
    /// <c>PlayingSoundTrackSO</c> and nothing else. The bootstrap's own
    /// <c>BoardBuilder.LoadBoardAudio</c> normally set it already; otherwise the warmup picks it by
    /// the same rule (equipped soundtrack, else the player board's) and sets it.
    /// </summary>
    internal static async Task WarmPlayingSoundtrackAsync(
        SoundManager soundManager,
        CollectionManager? collectionManager,
        BoardAssetDataSO? playerBoard,
        ReplayAudioWarmupStats stats,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        var playing = soundManager.PlayingSoundTrackSO;
        bool warmed;
        if (playing != null)
        {
            stats.SoundtrackSource = ReplaySoundtrackSource.GamePlaying;
            warmed = await WarmSoundtrackTracksAsync(soundManager, playing, stats, outcome);
        }
        else
        {
            var target = await ResolveGameSoundtrackAsync(collectionManager, playerBoard, outcome);
            stats.SoundtrackSource =
                target == null ? ReplaySoundtrackSource.None : ReplaySoundtrackSource.WarmupSet;
            warmed = await WarmSoundtrackAsync(soundManager, target, stats, outcome);
        }

        if (!warmed)
        {
            stats.SoundtrackBanksSkipped++;
            outcome.ReportDegradation(ReplayPlaybackReasonCode.SoundtrackWarmupFailed);
        }
    }

    internal static async Task WarmBoardAudioAsync(
        SoundManager soundManager,
        BoardAssetDataSO boardAsset,
        bool isPlayerBoard,
        ReplayAudioBankOwnership ownership,
        ReplayAudioWarmupStats stats,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        if (string.IsNullOrWhiteSpace(boardAsset.boardBank))
        {
            outcome.ReportDegradation(ReplayPlaybackReasonCode.SoundtrackWarmupFailed);
            stats.BoardBanksSkipped++;
            return;
        }

        if (string.IsNullOrWhiteSpace(boardAsset.boardAssetBank))
        {
            outcome.ReportDegradation(ReplayPlaybackReasonCode.SoundtrackWarmupFailed);
            stats.BoardBanksSkipped++;
            return;
        }

        var wasMetadataLoaded = soundManager.IsBankLoaded(boardAsset.boardBank, isMetadata: false);
        var wasAssetLoaded = soundManager.IsBankLoaded(
            boardAsset.boardAssetBank,
            isMetadata: false
        );
        var loaded = await soundManager.LoadBankAsync(
            FModBank.EBankType.SFX,
            boardAsset.boardBank,
            boardAsset.boardAssetBank
        );
        if (!loaded)
        {
            stats.BoardBanksFailed++;
            outcome.ReportDegradation(ReplayPlaybackReasonCode.SoundtrackWarmupFailed);
            return;
        }

        if (wasMetadataLoaded && wasAssetLoaded)
            stats.BoardBanksAlreadyLoaded++;
        else
            stats.BoardBanksLoaded++;

        if (
            ownership.RecordWarmed(
                boardAsset.boardBank,
                boardAsset.boardAssetBank,
                wasMetadataLoaded,
                wasAssetLoaded,
                isPlayerBoard
            )
        )
            stats.BoardBanksOwned++;
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
        CollectionManager? collectionManager,
        IReplayPlaybackOutcomeSink outcome
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
            outcome.ReportDegradation(ReplayPlaybackReasonCode.SoundtrackWarmupFailed, ex);
            return null;
        }
    }

    internal static async Task<BoardAssetDataSO?> TryGetOpponentBoardAsync(
        CollectionManager? collectionManager,
        IReplayPlaybackOutcomeSink outcome
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
            outcome.ReportDegradation(ReplayPlaybackReasonCode.SoundtrackWarmupFailed, ex);
            return null;
        }
    }

    private static async Task<bool> WarmSoundtrackAsync(
        SoundManager soundManager,
        SoundtrackSO? soundtrack,
        ReplayAudioWarmupStats stats,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        if (soundtrack == null)
            return false;

        var loadedSoundtrack = await TryLoadSoundtrackAssetAsync(soundtrack, outcome);
        if (loadedSoundtrack == null)
        {
            stats.SoundtrackBanksFailed++;
            return false;
        }

        // An empty soundtrack is never handed to the game, which nulls one itself; the caller
        // counts and reports it once.
        if (!HasTracks(loadedSoundtrack))
            return false;

        // Re-checked after the await: the game's own LoadSoundtrack may have set it meanwhile.
        // Never release this handle: PlayingSoundTrackSO keeps the asset, as the game's own
        // LoadSoundtrack never releases it either.
        if (soundManager.PlayingSoundTrackSO == null)
            soundManager.PlayingSoundTrackSO = loadedSoundtrack;

        return await WarmSoundtrackTracksAsync(
            soundManager,
            soundManager.PlayingSoundTrackSO,
            stats,
            outcome
        );
    }

    private static async Task<bool> WarmSoundtrackTracksAsync(
        SoundManager soundManager,
        SoundtrackSO soundtrack,
        ReplayAudioWarmupStats stats,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        if (!HasTracks(soundtrack))
            return false;

        for (uint trackIndex = 0; trackIndex < soundtrack.MusicTracks.Length; trackIndex++)
        {
            await WarmSoundtrackTrackAsync(soundManager, soundtrack, trackIndex, stats, outcome);
        }

        return true;
    }

    private static bool HasTracks(SoundtrackSO soundtrack) =>
        soundtrack.MusicTracks != null && soundtrack.MusicTracks.Length > 0;

    // BoardBuilder.LoadBoardAudio's rule: an equipped soundtrack wins even when its
    // SoundtrackObject is missing; only no equipped soundtrack falls back to the board's.
    private static async Task<SoundtrackSO?> ResolveGameSoundtrackAsync(
        CollectionManager? collectionManager,
        BoardAssetDataSO? playerBoard,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        if (collectionManager != null)
        {
            try
            {
                var equipped = await collectionManager.GetEquippedSoundtrack();
                if (equipped != null)
                    return equipped.SoundtrackObject;
            }
            catch (Exception ex)
            {
                outcome.ReportDegradation(ReplayPlaybackReasonCode.SoundtrackWarmupFailed, ex);
            }
        }

        return playerBoard != null ? playerBoard.soundtrack : null;
    }

    private static async Task<SoundtrackSO?> TryLoadSoundtrackAssetAsync(
        SoundtrackSO soundtrack,
        IReplayPlaybackOutcomeSink outcome
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

            outcome.ReportDegradation(ReplayPlaybackReasonCode.SoundtrackWarmupFailed);
            return soundtrack;
        }
        catch (Exception ex)
        {
            outcome.ReportDegradation(ReplayPlaybackReasonCode.SoundtrackWarmupFailed, ex);
            return soundtrack;
        }
    }

    private static bool TryGetSoundtrackTrackBanks(
        SoundtrackSO soundtrack,
        uint trackIndex,
        out string? metadataBank,
        out string? assetBank,
        IReplayPlaybackOutcomeSink outcome
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
            outcome.ReportDegradation(ReplayPlaybackReasonCode.SoundtrackWarmupFailed, ex);
            return false;
        }
    }

    private static async Task WarmSoundtrackTrackAsync(
        SoundManager soundManager,
        SoundtrackSO soundtrack,
        uint trackIndex,
        ReplayAudioWarmupStats stats,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        if (
            !TryGetSoundtrackTrackBanks(
                soundtrack,
                trackIndex,
                out var metadataBank,
                out var assetBank,
                outcome
            )
        )
        {
            stats.SoundtrackBanksSkipped++;
            outcome.ReportDegradation(ReplayPlaybackReasonCode.SoundtrackWarmupFailed);
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
            outcome.ReportDegradation(ReplayPlaybackReasonCode.SoundtrackWarmupFailed);
            return;
        }

        if (wasMetadataLoaded && wasAssetLoaded)
            stats.SoundtrackBanksAlreadyLoaded++;
        else
            stats.SoundtrackBanksLoaded++;
    }
}
