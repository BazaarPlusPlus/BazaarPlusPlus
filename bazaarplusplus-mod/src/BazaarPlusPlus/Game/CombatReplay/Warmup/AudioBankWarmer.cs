#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Assets.Scripts.Audio;
using BazaarPlusPlus.Infrastructure;
using FMOD.Studio;
using FMODUnity;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.Warmup;

internal static class AudioBankWarmer
{
    internal static async Task WarmAudioBanksAsync()
    {
        try
        {
            var soundManager = Services.Get<SoundManager>();
            if (soundManager == null)
            {
                BppLog.Warn(
                    "AudioBankWarmer",
                    "Saved replay audio warmup skipped because SoundManager is unavailable."
                );
                return;
            }

            var stats = new ReplayAudioWarmupStats();
            var collectionManager = Services.Get<CollectionManager>();
            if (collectionManager == null)
            {
                BppLog.Warn(
                    "AudioBankWarmer",
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

            var playerBoard = await SoundtrackWarmer.TryGetPlayerBoardAsync(collectionManager);
            SoundtrackWarmer.AddBoardAsset(boardAssets, playerBoard);

            var opponentBoard = await SoundtrackWarmer.TryGetOpponentBoardAsync(collectionManager);
            SoundtrackWarmer.AddBoardAsset(boardAssets, opponentBoard);

            if (boardAssets.Count == 0)
            {
                BppLog.Warn(
                    "AudioBankWarmer",
                    "Saved replay audio warmup found no player or opponent board assets."
                );
            }

            foreach (var boardAsset in boardAssets)
            {
                await SoundtrackWarmer.WarmBoardAudioAsync(soundManager, boardAsset!, stats);
            }

            await SoundtrackWarmer.WarmSoundtracksAsync(
                soundManager,
                collectionManager,
                boardAssets,
                stats
            );

            BppLog.Info(
                "AudioBankWarmer",
                "Saved replay audio warmup finished: "
                    + $"boardBanks(loaded={stats.BoardBanksLoaded}, alreadyLoaded={stats.BoardBanksAlreadyLoaded}, failed={stats.BoardBanksFailed}, skipped={stats.BoardBanksSkipped}) "
                    + $"soundtrackBanks(loaded={stats.SoundtrackBanksLoaded}, alreadyLoaded={stats.SoundtrackBanksAlreadyLoaded}, failed={stats.SoundtrackBanksFailed}, skipped={stats.SoundtrackBanksSkipped})"
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn("AudioBankWarmer", $"Saved replay audio warmup failed: {ex.Message}");
        }
    }

    internal static void EnsureAudioReadyForPlayback()
    {
        try
        {
            var gameServiceManager = Singleton<GameServiceManager>.Instance;
            if (gameServiceManager?.GamePaused == true)
            {
                BppLog.Info(
                    "AudioBankWarmer",
                    "Replay audio readiness layer-1: GamePaused=true, calling PauseOrUnpauseGame(false)."
                );
                gameServiceManager.PauseOrUnpauseGame(toPauseOrUnpause: false);
            }

            var soundManager = Services.Get<SoundManager>();
            if (soundManager == null)
            {
                BppLog.Warn(
                    "AudioBankWarmer",
                    "Replay audio readiness aborted: SoundManager unavailable."
                );
                return;
            }

            BppLog.Info(
                "AudioBankWarmer",
                "Replay audio readiness layer-2: SoundManager.PauseBusses(false)."
            );
            soundManager.PauseBusses(isPausing: false);

            StopAllTrackedSfxEventInstances(soundManager);
            ReassertSfxVolumeFromPreferences();
        }
        catch (Exception ex)
        {
            BppLog.Warn("AudioBankWarmer", $"Replay audio readiness step failed: {ex.Message}");
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

    private static void StopAllTrackedSfxEventInstances(SoundManager soundManager)
    {
        try
        {
            var dict = GetSfxEventInstancesDict(soundManager);
            if (dict == null)
            {
                BppLog.Warn(
                    "AudioBankWarmer",
                    "Replay audio readiness layer-3: sfxEventInstances dictionary not accessible; skipping."
                );
                return;
            }

            if (dict.Count == 0)
            {
                BppLog.Info(
                    "AudioBankWarmer",
                    "Replay audio readiness layer-3: no tracked SFX EventInstances to stop."
                );
                return;
            }

            var keys = dict.Keys.ToList();
            BppLog.Info(
                "AudioBankWarmer",
                $"Replay audio readiness layer-3: stopping {keys.Count} tracked SFX EventInstance(s): [{string.Join(",", keys)}]"
            );

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
                    BppLog.Warn(
                        "AudioBankWarmer",
                        $"Replay audio readiness layer-3: stop/release for key={key} failed: {ex.Message}"
                    );
                }
            }

            dict.Clear();
        }
        catch (Exception ex)
        {
            BppLog.Warn("AudioBankWarmer", $"Replay audio readiness layer-3 failed: {ex.Message}");
        }
    }

    private static void ReassertSfxVolumeFromPreferences()
    {
        try
        {
            var prefs = PlayerPreferences.Data;
            if (prefs == null)
            {
                BppLog.Info(
                    "AudioBankWarmer",
                    "Replay audio readiness layer-4: PlayerPreferences.Data is null; skipping."
                );
                return;
            }

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
                BppLog.Warn(
                    "AudioBankWarmer",
                    "Replay audio readiness layer-4: SoundManager.SetVolume/VolumeType not found; skipping."
                );
                return;
            }

            var sfxValue = Enum.Parse(volumeTypeEnum, "SFX");
            setVolume.Invoke(null, new[] { sfxValue, (object)prefs.VolumeSfx });
            BppLog.Info(
                "AudioBankWarmer",
                $"Replay audio readiness layer-4: re-asserted SFX volume to {prefs.VolumeSfx:F3} from PlayerPreferences."
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn("AudioBankWarmer", $"Replay audio readiness layer-4 failed: {ex.Message}");
        }
    }
}
