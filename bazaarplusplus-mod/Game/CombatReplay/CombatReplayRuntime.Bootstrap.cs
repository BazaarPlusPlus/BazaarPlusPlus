#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Players;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.TempoNet.Enums;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PvpBattles;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed partial class CombatReplayRuntime
{
    private static async Task<bool> EnsureReplayBootstrapReadyAsync()
    {
        if (IsReplayBootstrapReady())
            return false;

        BppLog.Info("CombatReplayRuntime", "Bootstrapping gameplay scene for lobby replay.");
        Data.ResetRunData();
        if (!SceneLoader.IsSceneLoaded(SceneID.GameScene))
        {
            await SceneLoader.LoadScene(
                SceneID.GameScene,
                shouldUnloadCurrentScene: true,
                showLoadingScene: false
            );
        }

        if (!SceneLoader.IsSceneLoaded(SceneID.GameplayLoading))
            await SceneLoader.LoadSceneAdditive(SceneID.GameplayLoading);

        await WaitUntilAsync(
            () => Singleton<GameServiceManager>.Instance != null,
            timeout: TimeSpan.FromSeconds(20)
        );
        await BootstrapReplayManagersAsync();
        EnsureReplayAppStateHandlersInitialized();
        await WaitUntilAsync(IsReplayBootstrapReady, timeout: TimeSpan.FromSeconds(20));

        await SceneLoader.SetActiveScene(SceneID.GameScene);
        SceneLoader.LoadingComplete();
        if (SceneLoader.IsSceneLoaded(SceneID.GameplayLoading))
            await SceneLoader.UnloadScene(SceneID.GameplayLoading);

        BppLog.Info("CombatReplayRuntime", "Replay bootstrap scene environment is ready.");
        return true;
    }

    private static async Task BootstrapReplayManagersAsync()
    {
        var runManager = Services.Get<RunManager>();
        if (runManager == null)
            throw new InvalidOperationException("RunManager is unavailable.");

        var gameServiceManager = Singleton<GameServiceManager>.Instance;
        if (gameServiceManager == null)
            throw new InvalidOperationException("GameServiceManager is unavailable.");

        if (Singleton<BoardManager>.Instance != null && gameServiceManager.IsInitialized)
            return;

        var boardReferenceField = typeof(RunManager).GetField(
            "_baseBoardReference",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
        var boardReference =
            boardReferenceField?.GetValue(runManager) as AssetReference
            ?? throw new MissingFieldException(typeof(RunManager).FullName, "_baseBoardReference");

        var boardBuilder = new BoardBuilder();
        runManager.BoardBuilder = boardBuilder;
        var boardManager = await boardBuilder.SetUpBoard(boardReference);
        await gameServiceManager.Init(boardManager);
    }

    private static ReplayBootstrapContext ResolveReplayDependencies()
    {
        var socketBehavior = EnsureSocketBehavior();
        var processor = GetProcessor(socketBehavior);
        EnsureReplayAppStateHandlersInitialized(processor);

        var gameSimHandler = GetGameSimHandler();
        var bootstrapContext = new ReplayBootstrapContext(
            socketBehavior,
            processor,
            gameSimHandler,
            CreateSetLastCombatSequence(processor),
            CreateHandleSpawnMessageAsync(processor, gameSimHandler),
            CreateTriggerCombatSequenceCreated(processor)
        );
        BppLog.Info("CombatReplayRuntime", "Replay bootstrap dependencies resolved.");
        return bootstrapContext;
    }

    private static async Task TryInjectSavedReplayAsync(
        ReplayBootstrapContext bootstrapContext,
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        string battleId
    )
    {
        bootstrapContext.SetLastCombatSequence(sequence);
        await bootstrapContext.HandleSpawnMessageAsync(sequence.SpawnMessage);
        RehydrateSavedReplayPlayerCards(manifest, sequence.SpawnMessage);
        RehydrateSavedReplayOpponentCards(manifest, sequence.SpawnMessage);
        RehydrateSavedReplayPlayerSkills(manifest, sequence.SpawnMessage);
        RehydrateSavedReplayOpponentSkills(manifest, sequence.SpawnMessage);
        await RebuildSavedReplaySkillPresentationAsync();
        bootstrapContext.TriggerCombatSequenceCreated();
        await Task.Delay(50);
        await AppState.TryPushState<ReplayState>();
        if (AppState.CurrentState is not ReplayState replayState)
            throw new InvalidOperationException("ReplayState did not become active.");
        HideEncounterPickerOverlays();
        EnsureOpponentPortraitVisible();
        await PrepareReplayHealthBarsAsync();
        Singleton<BoardManager>.Instance.ToggleOpponentPortrait(isVisible: true);
        await WaitForReplayPresentationReadyAsync();
        await WarmReplayPresentationAssetsAsync(manifest, sequence);
        await WarmReplayAudioBanksAsync();
        EnsureReplayAudioUnpaused();
        HideEncounterPickerOverlays();
        EnsureOpponentPortraitVisible();
        RefillReplayOpponentHealthBar();
        replayState.Replay();
        EnsureOpponentPortraitVisible();
        Singleton<BoardManager>.Instance.ShowReplayAndRecapButtons(show: false, deactivate: true);

        BppLog.Info("CombatReplayRuntime", $"Saved replay injection completed for {battleId}.");
    }

    private static void RehydrateSavedReplayPlayerCards(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage
    )
    {
        var capture = manifest.Snapshots.PlayerHand;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay {manifest.BattleId} does not contain player-hand snapshots; player cards may be missing. Re-capture this fight with the current mod build."
            );
            return;
        }

        RehydrateSavedReplayCards(capture.Items, spawnMessage, Data.Run?.Player);
    }

    private static void RehydrateSavedReplayOpponentCards(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage
    )
    {
        var capture = manifest.Snapshots.OpponentHand;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay {manifest.BattleId} does not contain opponent-hand snapshots; opponent cards may be missing. Re-capture this fight with the current mod build."
            );
            return;
        }

        RehydrateSavedReplayCards(capture.Items, spawnMessage, Data.Run?.Opponent);
    }

    private static void RehydrateSavedReplayPlayerSkills(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage
    )
    {
        var capture = manifest.Snapshots.PlayerSkills;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay {manifest.BattleId} does not contain player-skill snapshots; player skills may be missing. Re-capture this fight with the current mod build."
            );
            return;
        }

        var skills = RehydrateSavedReplaySkillCards(capture.Items, spawnMessage, Data.Run?.Player);
        ReplaceSkillCollection(Data.Run?.Player, skills);
    }

    private static void RehydrateSavedReplayOpponentSkills(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage
    )
    {
        var capture = manifest.Snapshots.OpponentSkills;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay {manifest.BattleId} does not contain opponent-skill snapshots; opponent skills may be missing. Re-capture this fight with the current mod build."
            );
            return;
        }

        var skills = RehydrateSavedReplaySkillCards(
            capture.Items,
            spawnMessage,
            Data.Run?.Opponent
        );
        ReplaceSkillCollection(Data.Run?.Opponent, skills);
    }

    private static void RehydrateSavedReplayCards(
        IEnumerable<CombatReplayCardSnapshot> snapshots,
        NetMessageGameSim spawnMessage,
        IPlayer? owner
    )
    {
        foreach (var snapshot in snapshots.Where(snapshot => snapshot != null))
        {
            if (string.IsNullOrWhiteSpace(snapshot.InstanceId))
                continue;

            var card = Data.GetOrCreateCard(
                snapshot.InstanceId,
                snapshot.TemplateId,
                snapshot.Type
            );
            if (spawnMessage.Data.Cards.TryGetValue(snapshot.InstanceId, out var simUpdate))
                card.Update(simUpdate);

            ApplySnapshotFallback(card, snapshot);
            card.Size = snapshot.Size;
            card.Owner = owner;
            card.Section = snapshot.Section;
            card.LeftSocketId = snapshot.Socket;
        }
    }

    private static List<SkillCard> RehydrateSavedReplaySkillCards(
        IEnumerable<CombatReplayCardSnapshot> snapshots,
        NetMessageGameSim spawnMessage,
        IPlayer? owner
    )
    {
        var skills = new List<SkillCard>();
        foreach (var snapshot in snapshots.Where(snapshot => snapshot != null))
        {
            if (string.IsNullOrWhiteSpace(snapshot.InstanceId))
                continue;

            var card = Data.GetOrCreateCard(
                snapshot.InstanceId,
                snapshot.TemplateId,
                snapshot.Type
            );
            if (spawnMessage.Data.Cards.TryGetValue(snapshot.InstanceId, out var simUpdate))
                card.Update(simUpdate);

            ApplySnapshotFallback(card, snapshot);
            card.Size = snapshot.Size;
            card.Owner = owner;
            card.Section = snapshot.Section;
            card.LeftSocketId = snapshot.Socket;

            if (card is SkillCard skillCard)
                skills.Add(skillCard);
        }

        return skills;
    }

    private static void ApplySnapshotFallback(Card card, CombatReplayCardSnapshot snapshot)
    {
        if (snapshot.Attributes != null && snapshot.Attributes.Count > 0)
        {
            foreach (var entry in snapshot.Attributes)
            {
                if (
                    Enum.TryParse<ECardAttributeType>(
                        entry.Key,
                        ignoreCase: false,
                        out var attributeType
                    )
                )
                    card.Attributes[attributeType] = entry.Value;
            }
        }

        if (snapshot.Tags != null && snapshot.Tags.Count > 0)
        {
            card.Tags = snapshot
                .Tags.Select(tag =>
                    Enum.TryParse<ECardTag>(tag, ignoreCase: false, out var parsedTag)
                        ? (ECardTag?)parsedTag
                        : null
                )
                .Where(tag => tag.HasValue)
                .Select(tag => tag!.Value)
                .ToHashSet();
        }

        if (
            !string.IsNullOrWhiteSpace(snapshot.Tier)
            && Enum.TryParse<ETier>(snapshot.Tier, ignoreCase: false, out var tier)
        )
            card.Tier = tier;

        if (
            card is ItemCard itemCard
            && !string.IsNullOrWhiteSpace(snapshot.Enchant)
            && Enum.TryParse<EEnchantmentType>(
                snapshot.Enchant,
                ignoreCase: false,
                out var enchantment
            )
        )
            itemCard.Enchantment = enchantment;
    }

    private static void ReplaceSkillCollection(object? combatant, IReadOnlyList<SkillCard> skills)
    {
        if (combatant == null)
            return;

        var skillsProperty = combatant
            .GetType()
            .GetProperty(
                "Skills",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (skillsProperty == null)
            return;

        if (skillsProperty.CanWrite)
        {
            skillsProperty.SetValue(combatant, skills.ToList());
            return;
        }

        if (skillsProperty.GetValue(combatant) is System.Collections.IList list)
        {
            list.Clear();
            foreach (var skill in skills)
                list.Add(skill);
        }
    }

    private static async Task RollbackReplayBootstrapAsync()
    {
        try
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                "Replay bootstrap failed. Resetting replay state and returning to lobby."
            );
            AppState.Reset();
            Data.ResetRunData();
            DisposeSocketBehavior();

            if (Singleton<GameServiceManager>.Instance != null)
                Singleton<GameServiceManager>.Instance.PauseOrUnpauseGame(toPauseOrUnpause: false);

            if (SceneLoader.IsSceneLoaded(SceneID.GameplayLoading))
                await SceneLoader.UnloadScene(SceneID.GameplayLoading);

            await SceneLoader.LoadScene(
                SceneID.HeroSelectScene,
                shouldUnloadCurrentScene: true,
                showLoadingScene: false
            );
        }
        catch (Exception ex)
        {
            BppLog.Error("CombatReplayRuntime", $"Failed to roll back replay bootstrap: {ex}");
        }
    }

    private static bool IsReplayBootstrapReady()
    {
        return SceneLoader.IsSceneLoaded(SceneID.GameScene)
            && Singleton<BoardManager>.Instance != null
            && Singleton<BoardManager>.Instance.IsInitialized
            && Singleton<GameServiceManager>.Instance != null
            && Singleton<GameServiceManager>.Instance.IsInitialized
            && TryGetAppStateField<GameSimHandler>("_gameSimHandler") != null;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Timed out while bootstrapping replay environment.");

            await Task.Delay(100);
        }
    }

    private static async Task WaitForReplayPresentationReadyAsync()
    {
        await WaitUntilAsync(
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

    private static async Task RebuildSavedReplaySkillPresentationAsync()
    {
        var playerSkills = Data.Run?.Player?.Skills?.Cast<Card>().ToList() ?? new List<Card>();
        var opponentSkills = Data.Run?.Opponent?.Skills?.Cast<Card>().ToList() ?? new List<Card>();

        if (Data.PlayerSkillPresentationManager != null)
            await Data.PlayerSkillPresentationManager.Initialize(playerSkills);

        if (Data.OpponentSkillPresenationManager != null)
            await Data.OpponentSkillPresenationManager.Initialize(opponentSkills);
    }
}
