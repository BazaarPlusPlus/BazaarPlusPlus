#nullable enable
using System.Reflection;
using Assets.Scripts.Audio;
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
        ReplayAudioWarmupStats stats,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        var warmedAny = false;
        var warmedSoundtracks = new HashSet<string>(StringComparer.Ordinal);
        warmedAny |= await WarmSoundtrackAsync(
            soundManager,
            await TryGetSoundtrackAsync(collectionManager, outcome),
            stats,
            warmedSoundtracks,
            setPlayingSoundtrack: true,
            outcome: outcome
        );

        foreach (var boardAsset in boardAssets)
        {
            warmedAny |= await WarmSoundtrackAsync(
                soundManager,
                boardAsset.soundtrack,
                stats,
                warmedSoundtracks,
                setPlayingSoundtrack: soundManager.PlayingSoundTrackSO == null,
                outcome: outcome
            );
        }

        if (!warmedAny)
        {
            stats.SoundtrackBanksSkipped++;
            outcome.ReportDegradation(ReplayPlaybackReasonCode.SoundtrackWarmupFailed);
        }
    }

    internal static async Task WarmBoardAudioAsync(
        SoundManager soundManager,
        BoardAssetDataSO boardAsset,
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
        ISet<string> warmedSoundtracks,
        bool setPlayingSoundtrack,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        if (soundtrack == null)
            return false;

        var key = !string.IsNullOrWhiteSpace(soundtrack.SoundtrackPath)
            ? soundtrack.SoundtrackPath
            : soundtrack.name;
        if (!string.IsNullOrWhiteSpace(key) && warmedSoundtracks.Contains(key))
            return true;

        var loadedSoundtrack = await TryLoadSoundtrackAssetAsync(soundtrack, outcome);
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
            outcome.ReportDegradation(ReplayPlaybackReasonCode.SoundtrackWarmupFailed);
            return false;
        }

        if (setPlayingSoundtrack)
            soundManager.PlayingSoundTrackSO = loadedSoundtrack;

        for (uint trackIndex = 0; trackIndex < loadedSoundtrack.MusicTracks.Length; trackIndex++)
        {
            await WarmSoundtrackTrackAsync(
                soundManager,
                loadedSoundtrack,
                trackIndex,
                stats,
                outcome
            );
        }

        return true;
    }

    private static async Task<SoundtrackSO?> TryGetSoundtrackAsync(
        CollectionManager? collectionManager,
        IReplayPlaybackOutcomeSink outcome
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
            outcome.ReportDegradation(ReplayPlaybackReasonCode.SoundtrackWarmupFailed, ex);
            return null;
        }
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
