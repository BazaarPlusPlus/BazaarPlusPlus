#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.Bootstrap;

internal static class AppStateHandlerInstaller
{
    internal static void EnsureAppStateHandlersInitialized(NetMessageProcessor? processor = null)
    {
        if (ReplayBootstrap.TryGetAppStateField<GameSimHandler>("_gameSimHandler") != null)
            return;

        processor ??= ReplayBootstrap.TryGetAppStateField<NetMessageProcessor>("_messageProcessor");
        processor ??= SocketBehaviorBridge.GetProcessor(
            SocketBehaviorBridge.TryGetSocketBehavior()
        );

        var sharedVariables = ReplayBootstrap.TryGetAppStateField<SharedVariablesSO>(
            "_sharedVariablesSo"
        );
        if (sharedVariables == null)
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<SharedVariablesSO>())
            {
                sharedVariables = candidate;
                break;
            }
        }

        if (sharedVariables == null)
            throw new InvalidOperationException("SharedVariablesSO is unavailable.");

        AppState.Initialize(sharedVariables, processor);
    }

    internal static GameSimHandler GetGameSimHandler()
    {
        return ReplayBootstrap.TryGetAppStateField<GameSimHandler>("_gameSimHandler")
            ?? throw new InvalidOperationException("GameSimHandler is unavailable.");
    }

    internal static async Task RebuildSkillPresentationAsync()
    {
        var playerSkills = Data.Run?.Player?.Skills?.Cast<Card>().ToList() ?? new List<Card>();
        var opponentSkills = Data.Run?.Opponent?.Skills?.Cast<Card>().ToList() ?? new List<Card>();

        if (Data.PlayerSkillPresentationManager != null)
            await Data.PlayerSkillPresentationManager.Initialize(playerSkills);

        if (Data.OpponentSkillPresenationManager != null)
            await Data.OpponentSkillPresenationManager.Initialize(opponentSkills);
    }

    internal static async Task WaitForPresentationReadyAsync()
    {
        await BootstrapManagerInitializer.WaitUntilAsync(
            () =>
            {
                var boardManager = Singleton<BoardManager>.Instance;
                if (boardManager == null || !boardManager.IsInitialized)
                    return false;

                var playerSkillPresentationReady =
                    Data.PlayerSkillPresentationManager == null
                    || !Data.PlayerSkillPresentationManager.IsUpdatingSkillBoard;
                var opponentSkillPresentationReady =
                    Data.OpponentSkillPresenationManager == null
                    || !Data.OpponentSkillPresenationManager.IsUpdatingSkillBoard;

                return !boardManager.StorageMoving
                    && !boardManager.IsUpdatingBoard
                    && !boardManager.IsUpdatingSkillBoard
                    && playerSkillPresentationReady
                    && opponentSkillPresentationReady
                    && !boardManager.isUpdatingPresentation
                    && !boardManager.IsCarpetUnrolling
                    && !boardManager.HasCardsToReveal();
            },
            timeout: TimeSpan.FromSeconds(5)
        );

        // Let one more frame pass so ReplayState.OnEnter fire-and-forget spawn work can settle
        // before combat sim playback starts.
        await Task.Delay(100);
    }
}
