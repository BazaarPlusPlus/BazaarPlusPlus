#nullable enable
using System.Diagnostics;
using System.Reflection;
using FMOD.Studio;
using TheBazaar;
using TheBazaar.AppFramework;

namespace BazaarPlusPlus.Game.CombatReplay.Warmup;

internal static class AudioBankWarmer
{
    internal static async Task WarmAudioBanksAsync(IReplayPlaybackOutcomeSink outcome)
    {
        var stopwatch = Stopwatch.StartNew();
        var stats = new ReplayAudioWarmupStats();
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

            var playerBoard = await SoundtrackWarmer.TryGetPlayerBoardAsync(
                collectionManager,
                outcome
            );
            SoundtrackWarmer.AddBoardAsset(boardAssets, playerBoard);

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
                    stats,
                    outcome
                );
            }

            await SoundtrackWarmer.WarmSoundtracksAsync(
                soundManager,
                collectionManager,
                boardAssets,
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
            stopwatch.Stop();
            ReplayWarmupLogging.AudioCompleted(
                outcome.BattleId,
                stopwatch.ElapsedMilliseconds,
                stats
            );
        }
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
