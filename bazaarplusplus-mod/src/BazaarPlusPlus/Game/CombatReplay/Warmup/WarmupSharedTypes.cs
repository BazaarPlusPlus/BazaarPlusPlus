#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

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
            new BppLogEvent(BppLogFeatureScope.CombatReplay, "combat_replay.warmup.completed"),
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
            new BppLogEvent(BppLogFeatureScope.CombatReplay, "combat_replay.warmup.completed"),
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
                new BppLogEvent(
                    BppLogFeatureScope.CombatReplay,
                    "combat_replay.warmup.asset_skipped"
                ),
                () => [("stage", stage), ("asset_key", assetKey), ("reason_code", reasonCode)]
            );
            return;
        }

        BppLog.DebugEvent(
            new BppLogEvent(BppLogFeatureScope.CombatReplay, "combat_replay.warmup.asset_skipped"),
            exception,
            () => [("stage", stage), ("asset_key", assetKey), ("reason_code", reasonCode)]
        );
    }

    private static Infrastructure.Logging.BppLogField[] BuildCompletedFields(
        ReplayWarmupStage stage,
        string? battleId,
        long durationMilliseconds,
        ReplayWarmupStats? presentation,
        ReplayAudioWarmupStats? audio
    ) =>
        [
            ("stage", stage),
            ("battle_id", battleId, BppLogCorrelationPolicy.Short),
            ("duration_ms", durationMilliseconds),
            ("board_bank_loaded_count", audio?.BoardBanksLoaded ?? 0),
            ("board_bank_already_loaded_count", audio?.BoardBanksAlreadyLoaded ?? 0),
            ("board_bank_failed_count", audio?.BoardBanksFailed ?? 0),
            ("board_bank_skipped_count", audio?.BoardBanksSkipped ?? 0),
            ("soundtrack_bank_loaded_count", audio?.SoundtrackBanksLoaded ?? 0),
            ("soundtrack_bank_already_loaded_count", audio?.SoundtrackBanksAlreadyLoaded ?? 0),
            ("soundtrack_bank_failed_count", audio?.SoundtrackBanksFailed ?? 0),
            ("soundtrack_bank_skipped_count", audio?.SoundtrackBanksSkipped ?? 0),
            ("shared_asset_preloaded_count", presentation?.SharedAssetsPreloaded ?? 0),
            ("shared_asset_skipped_count", presentation?.SharedAssetsSkipped ?? 0),
            ("card_preloaded_count", presentation?.CardsPreloaded ?? 0),
            ("card_skipped_count", presentation?.CardsSkipped ?? 0),
            ("card_failed_count", presentation?.CardsFailed ?? 0),
            ("override_asset_preloaded_count", presentation?.OverrideAssetsPreloaded ?? 0),
            ("override_asset_skipped_count", presentation?.OverrideAssetsSkipped ?? 0),
            ("override_asset_failed_count", presentation?.OverrideAssetsFailed ?? 0),
            ("vfx_prewarmed_count", presentation?.VfxPrewarmed ?? 0),
            ("vfx_skipped_count", presentation?.VfxSkipped ?? 0),
            ("vfx_failed_count", presentation?.VfxFailed ?? 0),
        ];
}
