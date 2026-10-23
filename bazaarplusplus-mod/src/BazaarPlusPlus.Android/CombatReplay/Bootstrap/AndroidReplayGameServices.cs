using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using Il2Cpp;
using Il2CppBazaarGameClient.Domain.Models.Cards;
using Il2CppBazaarGameShared.Infra.Messages;
using Il2CppNetworking;
using Il2CppTheBazaar;
using Il2CppTheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.Android.CombatReplay.Bootstrap;

/// <summary>
/// The game services a saved replay drives, the Android forms of the desktop's
/// <c>SocketBehaviorBridge</c>, <c>AppStateHandlerInstaller</c> and
/// <c>BootstrapManagerInitializer</c>. The desktop reaches them by reflection; IL2CPP surfaces
/// private fields as interop properties, so here they are plain reads. The network host is
/// <c>Il2CppNetworking.NetworkManager</c>: the online Android build has no <c>SocketBehavior</c>.
/// Every game task is awaited through <see cref="Il2CppTasks"/> and every wait polls on the main
/// thread. Main thread only.
/// </summary>
internal static class AndroidReplayGameServices
{
    private static readonly TimeSpan PresentationReadyTimeout = TimeSpan.FromSeconds(5);

    public static NetworkManager RequireNetworkManager() =>
        NetworkManager.GetInstance()
        ?? throw new InvalidOperationException("NetworkManager is unavailable.");

    public static NetMessageProcessor RequireProcessor(NetworkManager network) =>
        network.GetProcessor()
        ?? throw new InvalidOperationException(
            "NetworkManager did not expose a NetMessageProcessor."
        );

    /// <summary>Disposes the network host after a failed lobby bootstrap, as the desktop does.</summary>
    public static (bool Succeeded, Exception? Exception) DisposeNetwork()
    {
        try
        {
            NetworkManager.GetInstance()?.Dispose();
            return (true, null);
        }
        catch (Exception exception)
        {
            return (false, exception);
        }
    }

    public static void EnsureAppStateHandlersInitialized(NetMessageProcessor? processor = null)
    {
        if (AppState._gameSimHandler != null)
            return;
        processor ??= AppState._messageProcessor;
        processor ??= NetworkManager.GetInstance()?.GetProcessor();
        var shared = AppState._sharedVariablesSo;
        if (shared == null)
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<SharedVariablesSO>())
            {
                shared = candidate;
                break;
            }
        }
        if (shared == null)
            throw new InvalidOperationException("SharedVariablesSO is unavailable.");
        AppState.Initialize(shared, processor);
    }

    public static GameSimHandler RequireGameSimHandler() =>
        AppState._gameSimHandler
        ?? throw new InvalidOperationException("GameSimHandler is unavailable.");

    // The injected spawn is handled directly, bypassing ReceiveOrQueue, as on the desktop; the
    // handler then records it as handled so the live intake never replays it.
    public static void HandleSpawnMessage(
        NetMessageProcessor processor,
        GameSimHandler gameSimHandler,
        NetMessageGameSim spawnMessage
    )
    {
        if (!processor.Handle(spawnMessage))
            throw new InvalidOperationException(
                "NetMessageProcessor rejected the replay spawn message."
            );
        // Void in the game despite its name: nothing to await.
        Data.UpdateFromGameSimAsync(spawnMessage);
        var messageId = spawnMessage.MessageId;
        if (string.IsNullOrWhiteSpace(messageId))
            return;
        var handled =
            gameSimHandler._handledMessages
            ?? throw new MissingFieldException("GameMessageHandler`1", "_handledMessages");
        if (!handled.Contains(messageId))
            handled.Add(messageId);
    }

    public static void TriggerCombatSequenceCreated(NetMessageProcessor processor) =>
        processor.CombatSequenceCreated?.Invoke();

    public static async Task BootstrapManagersAsync()
    {
        var runManager =
            Services.Get<RunManager>()
            ?? throw new InvalidOperationException("RunManager is unavailable.");
        var gameServiceManager =
            GameServiceManager.Instance
            ?? throw new InvalidOperationException("GameServiceManager is unavailable.");
        if (BoardManager.Instance != null && gameServiceManager.IsInitialized)
            return;

        var boardReference =
            runManager._baseBoardReference
            ?? throw new MissingFieldException("RunManager", "_baseBoardReference");
        var boardBuilder = new BoardBuilder();
        runManager.BoardBuilder = boardBuilder;
        var boardManager = await Il2CppTasks.AwaitAsync(
            boardBuilder.SetUpBoard(boardReference),
            "BoardBuilder.SetUpBoard"
        );
        await Il2CppTasks.AwaitAsync(
            gameServiceManager.Init(boardManager),
            "GameServiceManager.Init"
        );
    }

    public static Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout) =>
        AndroidMainThread.Host.WhenAsync(condition, timeout);

    public static async Task RebuildSkillPresentationAsync()
    {
        var playerManager = Data.PlayerSkillPresentationManager;
        if (playerManager != null)
            await Il2CppTasks.AwaitAsync(
                playerManager.Initialize(Skills(Data.Run?.Player)),
                "PlayerSkillPresentationManager.Initialize"
            );
        var opponentManager = Data.OpponentSkillPresenationManager;
        if (opponentManager != null)
            await Il2CppTasks.AwaitAsync(
                opponentManager.Initialize(Skills(Data.Run?.Opponent)),
                "OpponentSkillPresentationManager.Initialize"
            );
    }

    public static async Task WaitForPresentationReadyAsync()
    {
        await WaitUntilAsync(
            () =>
            {
                var boardManager = BoardManager.Instance;
                if (boardManager == null || !boardManager.IsInitialized)
                    return false;
                var player = Data.PlayerSkillPresentationManager;
                var opponent = Data.OpponentSkillPresenationManager;
                return !boardManager.StorageMoving
                    && !boardManager.IsUpdatingBoard
                    && !boardManager.IsUpdatingSkillBoard
                    && (player == null || !player.IsUpdatingSkillBoard)
                    && (opponent == null || !opponent.IsUpdatingSkillBoard)
                    && !boardManager.isUpdatingPresentation
                    && !boardManager.IsCarpetUnrolling
                    && !boardManager.HasCardsToReveal();
            },
            PresentationReadyTimeout
        );
        await AndroidReplayPresentation.Tasks.WaitAsync(
            AndroidMainThread.Host,
            PresentationReadyTimeout
        );
    }

    // The game's skill presentation takes an IL2CPP list of cards; a managed list is rejected.
    private static Il2CppSystem.Collections.Generic.List<Card> Skills(
        Il2CppBazaarGameClient.Domain.Models.Player? owner
    )
    {
        var cards = new Il2CppSystem.Collections.Generic.List<Card>();
        var skills = owner?.Skills;
        if (skills == null)
            return cards;
        foreach (var skill in skills)
        {
            if (skill != null)
                cards.Add(skill.Cast<Card>());
        }
        return cards;
    }
}
