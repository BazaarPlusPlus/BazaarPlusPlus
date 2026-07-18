#nullable enable

using BazaarGameClient.Domain.Models;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarPlusPlus.Game.CombatReplay.Bootstrap;
using BazaarPlusPlus.Game.PvpBattles;
using TheBazaar;
using TheBazaar.UI.Components;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.PlaybackUi;

internal static class ReplayPresentationRestorer
{
    internal static void Refresh(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        RunStep(() => RestoreOpeningRunState(manifest, sequence), outcome, "run_state");
        RunStep(() => RefreshClock(outcome), outcome, "clock_selection");
        RunStep(() => RefreshBoardUi(outcome), outcome, "board_ui_selection");
    }

    private static void RestoreOpeningRunState(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence
    )
    {
        var run = Data.Run;
        if (run == null)
            return;

        var openingRun = sequence.SpawnMessage?.Data?.Run;
        var day = ReplaySavedStateNormalizer.ResolvePositiveUInt(
            openingRun?.Day ?? 0,
            manifest.Day
        );
        var hour = ReplaySavedStateNormalizer.ResolvePositiveUInt(
            openingRun?.Hour ?? 0,
            manifest.Hour
        );
        run.Day = day;
        run.Hour = hour;

        RestoreOpeningLevel(sequence.SpawnMessage?.Data?.Player, run.Player);
        RestoreOpeningLevel(sequence.SpawnMessage?.Data?.Opponent, run.Opponent);
        Data.AssignHoursInADay();
    }

    private static void RestoreOpeningLevel(SimUpdatePlayer? openingPlayer, Player? livePlayer)
    {
        if (openingPlayer?.Attributes == null || livePlayer?.Attributes == null)
            return;

        openingPlayer.Attributes.TryGetValue(EPlayerAttributeType.Level, out var openingLevel);
        livePlayer.Attributes.TryGetValue(EPlayerAttributeType.Level, out var currentLevel);
        livePlayer.Attributes[EPlayerAttributeType.Level] =
            ReplaySavedStateNormalizer.ResolveOpeningLevel(openingLevel, currentLevel);
    }

    private static void RefreshClock(IReplayPlaybackOutcomeSink outcome)
    {
        var run = Data.Run;
        if (run == null)
            return;

        foreach (var clock in GetSceneComponents<ClockViewController>())
        {
            RunStep(
                () =>
                {
                    clock.Init();
                    clock.UpdateClockImmediate(run);
                },
                outcome,
                "clock"
            );
        }
    }

    private static void RefreshBoardUi(IReplayPlaybackOutcomeSink outcome)
    {
        var run = Data.Run;
        if (run == null)
            return;

        var bindings = HealthBarBinder.ResolveBoardUiControllers();
        RunStep(
            () =>
                RefreshBoardUiController(bindings.PlayerController, run.Player, outcome, "player"),
            outcome,
            "board_ui_player_refresh"
        );
        RunStep(
            () =>
                RefreshBoardUiController(
                    bindings.OpponentController,
                    run.Opponent,
                    outcome,
                    "opponent"
                ),
            outcome,
            "board_ui_opponent_refresh"
        );
    }

    private static void RefreshBoardUiController(
        BoardUIController? controller,
        Player? player,
        IReplayPlaybackOutcomeSink outcome,
        string combatant
    )
    {
        if (controller == null || player == null)
            return;

        RunStep(() => controller.UpdateAllUI(player), outcome, $"board_ui_{combatant}");
        foreach (var levelPlate in controller.GetComponentsInChildren<LevelPlateController>(true))
        {
            RunStep(() => levelPlate.Init(player), outcome, $"level_plate_{combatant}");
        }
    }

    private static IEnumerable<T> GetSceneComponents<T>()
        where T : Component
    {
        return UnityEngine
            .Object.FindObjectsOfType<T>(true)
            .Where(component => component != null && component.gameObject.scene.rootCount > 0);
    }

    private static void RunStep(Action action, IReplayPlaybackOutcomeSink outcome, string stage)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            outcome.ReportDegradation(
                ReplayPlaybackReasonCode.PresentationWarmupFailed,
                new InvalidOperationException($"replay_presentation_restore:{stage}", ex)
            );
        }
    }
}
