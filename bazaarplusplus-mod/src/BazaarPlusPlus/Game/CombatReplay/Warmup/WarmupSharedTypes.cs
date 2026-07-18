#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay.Warmup;

internal sealed class ReplayWarmupStats
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

internal sealed class ReplayAudioWarmupStats
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

internal static class WarmupConstants
{
    internal const int ReplayWarmupConcurrency = 4;
}

internal static class ReplayWarmupLogging
{
    [Conditional("DEBUG")]
    internal static void PresentationCompleted(
        string? battleId,
        long durationMilliseconds,
        ReplayWarmupStats stats
    )
    {
        BppLog.DebugEvent(
            CombatReplayLogEvents.WarmupCompleted,
            () =>
                BuildCompletedFields(
                    ReplayWarmupStage.Presentation,
                    battleId,
                    durationMilliseconds,
                    presentation: stats,
                    audio: null
                )
        );
    }

    [Conditional("DEBUG")]
    internal static void AudioCompleted(
        string? battleId,
        long durationMilliseconds,
        ReplayAudioWarmupStats stats
    )
    {
        BppLog.DebugEvent(
            CombatReplayLogEvents.WarmupCompleted,
            () =>
                BuildCompletedFields(
                    ReplayWarmupStage.AudioBanks,
                    battleId,
                    durationMilliseconds,
                    presentation: null,
                    audio: stats
                )
        );
    }

    [Conditional("DEBUG")]
    internal static void AssetSkipped(
        ReplayWarmupStage stage,
        string? assetKey,
        ReplayWarmupAssetReasonCode reasonCode,
        Exception? exception = null
    )
    {
        if (exception == null)
        {
            BppLog.DebugEvent(
                CombatReplayLogEvents.WarmupAssetSkipped,
                () =>
                    [
                        CombatReplayLogEvents.WarmupAssetStage.Bind(stage),
                        CombatReplayLogEvents.WarmupAssetKey.Bind(assetKey),
                        CombatReplayLogEvents.WarmupAssetReasonCode.Bind(reasonCode),
                    ]
            );
            return;
        }

        BppLog.DebugEvent(
            CombatReplayLogEvents.WarmupAssetSkipped,
            exception,
            () =>
                [
                    CombatReplayLogEvents.WarmupAssetStage.Bind(stage),
                    CombatReplayLogEvents.WarmupAssetKey.Bind(assetKey),
                    CombatReplayLogEvents.WarmupAssetReasonCode.Bind(reasonCode),
                ]
        );
    }

    private static Infrastructure.Logging.BppLogFieldValue[] BuildCompletedFields(
        ReplayWarmupStage stage,
        string? battleId,
        long durationMilliseconds,
        ReplayWarmupStats? presentation,
        ReplayAudioWarmupStats? audio
    ) =>
        [
            CombatReplayLogEvents.WarmupCompletedStage.Bind(stage),
            CombatReplayLogEvents.WarmupCompletedBattleId.Bind(battleId),
            CombatReplayLogEvents.WarmupCompletedDurationMs.Bind(durationMilliseconds),
            CombatReplayLogEvents.WarmupBoardBankLoadedCount.Bind(audio?.BoardBanksLoaded ?? 0),
            CombatReplayLogEvents.WarmupBoardBankAlreadyLoadedCount.Bind(
                audio?.BoardBanksAlreadyLoaded ?? 0
            ),
            CombatReplayLogEvents.WarmupBoardBankFailedCount.Bind(audio?.BoardBanksFailed ?? 0),
            CombatReplayLogEvents.WarmupBoardBankSkippedCount.Bind(audio?.BoardBanksSkipped ?? 0),
            CombatReplayLogEvents.WarmupSoundtrackBankLoadedCount.Bind(
                audio?.SoundtrackBanksLoaded ?? 0
            ),
            CombatReplayLogEvents.WarmupSoundtrackBankAlreadyLoadedCount.Bind(
                audio?.SoundtrackBanksAlreadyLoaded ?? 0
            ),
            CombatReplayLogEvents.WarmupSoundtrackBankFailedCount.Bind(
                audio?.SoundtrackBanksFailed ?? 0
            ),
            CombatReplayLogEvents.WarmupSoundtrackBankSkippedCount.Bind(
                audio?.SoundtrackBanksSkipped ?? 0
            ),
            CombatReplayLogEvents.WarmupSharedAssetPreloadedCount.Bind(
                presentation?.SharedAssetsPreloaded ?? 0
            ),
            CombatReplayLogEvents.WarmupSharedAssetSkippedCount.Bind(
                presentation?.SharedAssetsSkipped ?? 0
            ),
            CombatReplayLogEvents.WarmupCardPreloadedCount.Bind(presentation?.CardsPreloaded ?? 0),
            CombatReplayLogEvents.WarmupCardSkippedCount.Bind(presentation?.CardsSkipped ?? 0),
            CombatReplayLogEvents.WarmupCardFailedCount.Bind(presentation?.CardsFailed ?? 0),
            CombatReplayLogEvents.WarmupOverrideAssetPreloadedCount.Bind(
                presentation?.OverrideAssetsPreloaded ?? 0
            ),
            CombatReplayLogEvents.WarmupOverrideAssetSkippedCount.Bind(
                presentation?.OverrideAssetsSkipped ?? 0
            ),
            CombatReplayLogEvents.WarmupOverrideAssetFailedCount.Bind(
                presentation?.OverrideAssetsFailed ?? 0
            ),
            CombatReplayLogEvents.WarmupVfxPrewarmedCount.Bind(presentation?.VfxPrewarmed ?? 0),
            CombatReplayLogEvents.WarmupVfxSkippedCount.Bind(presentation?.VfxSkipped ?? 0),
            CombatReplayLogEvents.WarmupVfxFailedCount.Bind(presentation?.VfxFailed ?? 0),
        ];
}
