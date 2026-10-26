#nullable enable
using System.Diagnostics;
using System.Reflection;
using FMOD.Studio;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;

namespace BazaarPlusPlus.Game.CombatReplay.Warmup;

internal static class AudioBankWarmer
{
    // Unity's main thread only, like every caller below.
    private static readonly ReplayAudioBankOwnership OwnedBoardBanks = new();

    // A release while a warmup is suspended in a bank load would drain the record before the
    // warmup adds that bank to it; the release then leaves the record for the next one.
    private static int WarmupsInFlight;

    internal static async Task WarmAudioBanksAsync(IReplayPlaybackOutcomeSink outcome)
    {
        var stopwatch = Stopwatch.StartNew();
        var stats = new ReplayAudioWarmupStats();
        WarmupsInFlight++;
        try
        {
            var soundManager = Services.Get<SoundManager>();
            if (soundManager == null)
            {
                outcome.ReportDegradation(ReplayPlaybackReasonCode.AudioWarmupFailed);
                return;
            }

            var collectionManager = Services.Get<CollectionManager>();
            if (collectionManager == null)
                outcome.ReportDegradation(ReplayPlaybackReasonCode.AudioWarmupFailed);

            var boardAssets = UnityEngine
                .Object.FindObjectsOfType<HeroBoardController>(true)
                .Where(controller =>
                    controller != null && controller.gameObject.scene.rootCount > 0
                )
                .Select(controller => controller.AssociatedDataSO)
                .Where(asset => asset != null)
                .Distinct()
                .ToList();

            // The board the bootstrap's BoardBuilder loaded, its default-board fallback included,
            // is the one the game unloads on RunInterrupted. Only when it is unknown does the
            // equipped board stand in; a second lookup could name a board the game never loaded.
            var playerBoard = TryGetLoadedPlayerBoard();
            if (playerBoard == null)
            {
                playerBoard = await SoundtrackWarmer.TryGetPlayerBoardAsync(
                    collectionManager,
                    outcome
                );
                SoundtrackWarmer.AddBoardAsset(boardAssets, playerBoard);
            }

            var opponentBoard = await SoundtrackWarmer.TryGetOpponentBoardAsync(
                collectionManager,
                outcome
            );
            SoundtrackWarmer.AddBoardAsset(boardAssets, opponentBoard);

            if (boardAssets.Count == 0)
                outcome.ReportDegradation(ReplayPlaybackReasonCode.AudioWarmupFailed);

            foreach (var boardAsset in boardAssets)
            {
                await SoundtrackWarmer.WarmBoardAudioAsync(
                    soundManager,
                    boardAsset!,
                    isPlayerBoard: playerBoard != null
                        && (
                            ReferenceEquals(boardAsset, playerBoard)
                            || string.Equals(
                                boardAsset!.boardBank,
                                playerBoard.boardBank,
                                StringComparison.Ordinal
                            )
                        ),
                    OwnedBoardBanks,
                    stats,
                    outcome
                );
            }

            await SoundtrackWarmer.WarmPlayingSoundtrackAsync(
                soundManager,
                collectionManager,
                playerBoard,
                stats,
                outcome
            );
        }
        catch (Exception ex)
        {
            outcome.ReportDegradation(ReplayPlaybackReasonCode.AudioWarmupFailed, ex);
        }
        finally
        {
            WarmupsInFlight--;
            stopwatch.Stop();
            ReplayWarmupLogging.AudioCompleted(
                outcome.BattleId,
                stopwatch.ElapsedMilliseconds,
                stats
            );
        }
    }

    /// <summary>
    /// Unloads the board SFX banks the warmup loaded and nothing in the game will unload.
    /// <c>DesktopSavedReplayHost.Notify</c> calls it on the main thread once a saved replay's
    /// menu return settles or its operation completes (a failed start included, after its
    /// rollback), outside <c>ReplayState</c>; on the Exit button that is after the menu return's
    /// <c>RunInterrupted</c> has stopped the music. <c>SoundManager.UnloadBank</c> only warns on
    /// a bank already gone.
    /// </summary>
    internal static void ReleaseOwnedBanks(string? battleId)
    {
        // No battle id: not a saved replay, so nothing was warmed for it.
        if (battleId == null || WarmupsInFlight > 0)
            return;

        try
        {
            var soundManager = Services.Get<SoundManager>();
            if (soundManager == null)
                return;

            var owned = OwnedBoardBanks.Drain();
            foreach (var bank in owned)
                soundManager.UnloadBank(bank.MetadataBank, bank.AssetBank);

            ReplayWarmupLogging.AudioReleased(battleId, owned);
        }
        catch (Exception ex)
        {
            // Called from a Saved Replay Playback notice, which must not throw.
            ReplayWarmupLogging.AudioReleaseFailed(battleId, ex);
        }
    }

    private static BoardAssetDataSO? TryGetLoadedPlayerBoard()
    {
        var boardManager = Singleton<BoardManager>.Instance;
        if (boardManager == null)
            return null;

        var heroBoard = boardManager.HeroBoardController;
        if (heroBoard == null)
            return null;

        var board = heroBoard.AssociatedDataSO;
        return board != null ? board : null;
    }

    internal static void EnsureAudioReadyForPlayback(IReplayPlaybackOutcomeSink outcome)
    {
        try
        {
            var gameServiceManager = Singleton<GameServiceManager>.Instance;
            if (gameServiceManager?.GamePaused == true)
            {
                gameServiceManager.PauseOrUnpauseGame(toPauseOrUnpause: false);
            }

            var soundManager = Services.Get<SoundManager>();
            if (soundManager == null)
            {
                outcome.ReportDegradation(ReplayPlaybackReasonCode.AudioWarmupFailed);
                return;
            }

            soundManager.PauseBusses(isPausing: false);

            StopAllTrackedSfxEventInstances(soundManager, outcome);
            ReassertSfxVolumeFromPreferences(outcome);
        }
        catch (Exception ex)
        {
            outcome.ReportDegradation(ReplayPlaybackReasonCode.AudioWarmupFailed, ex);
        }
    }

    private static Dictionary<string, EventInstance>? GetSfxEventInstancesDict(
        SoundManager soundManager
    )
    {
        var sfxPlayer = soundManager.SFXPlayer;
        if (sfxPlayer == null)
            return null;

        var dictField = sfxPlayer
            .GetType()
            .GetField(
                "sfxEventInstances",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            );
        return dictField?.GetValue(sfxPlayer) as Dictionary<string, EventInstance>;
    }

    private static void StopAllTrackedSfxEventInstances(
        SoundManager soundManager,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        try
        {
            var dict = GetSfxEventInstancesDict(soundManager);
            if (dict == null)
            {
                outcome.ReportDegradation(ReplayPlaybackReasonCode.AudioWarmupFailed);
                return;
            }

            if (dict.Count == 0)
                return;

            var keys = dict.Keys.ToList();

            foreach (var key in keys)
            {
                try
                {
                    var instance = dict[key];
                    if (instance.isValid())
                    {
                        instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                        instance.release();
                    }
                }
                catch (Exception ex)
                {
                    outcome.ReportDegradation(ReplayPlaybackReasonCode.AudioWarmupFailed, ex);
                }
            }

            dict.Clear();
        }
        catch (Exception ex)
        {
            outcome.ReportDegradation(ReplayPlaybackReasonCode.AudioWarmupFailed, ex);
        }
    }

    private static void ReassertSfxVolumeFromPreferences(IReplayPlaybackOutcomeSink outcome)
    {
        try
        {
            var prefs = PlayerPreferences.Data;
            if (prefs == null)
                return;

            var setVolume = typeof(SoundManager).GetMethod(
                "SetVolume",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
            );
            var volumeTypeEnum = typeof(SoundManager).GetNestedType(
                "VolumeType",
                BindingFlags.NonPublic | BindingFlags.Public
            );
            if (setVolume == null || volumeTypeEnum == null)
            {
                outcome.ReportDegradation(ReplayPlaybackReasonCode.AudioWarmupFailed);
                return;
            }

            var sfxValue = Enum.Parse(volumeTypeEnum, "SFX");
            setVolume.Invoke(null, new[] { sfxValue, (object)prefs.VolumeSfx });
        }
        catch (Exception ex)
        {
            outcome.ReportDegradation(ReplayPlaybackReasonCode.AudioWarmupFailed, ex);
        }
    }
}
