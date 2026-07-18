#nullable enable
using System.Globalization;
using BazaarGameClient.Domain.Cards;
using BazaarGameClient.Domain.Models;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Runs;
using BazaarPlusPlus.BazaarAgent;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.GameInterop;
using TheBazaar;
using TheBazaar.AppFramework;

namespace BazaarPlusPlus.BazaarAgentHost;

/// <summary>
/// Reads live game state and produces an <see cref="BazaarAgentContext"/> snapshot.
/// Main thread only. Pure read — never mutates any state.
/// </summary>
internal sealed class BazaarAgentGameContextReader : IBazaarAgentContextReader
{
    private readonly IBazaarAgentGameProbe _gameProbe;
    private readonly IBazaarAgentLogger _logger;
    private readonly BazaarAgentDegradationLogState _logState = new();

    public BazaarAgentGameContextReader(IBazaarAgentGameProbe gameProbe, IBazaarAgentLogger logger)
    {
        _gameProbe = gameProbe ?? throw new ArgumentNullException(nameof(gameProbe));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Build a context from live game state. Never throws — exceptions produce a
    /// degenerate context with <c>StateName=Unknown</c> and <c>AvailableActions=[Wait]</c>.
    /// </summary>
    public BazaarAgentContext Build(double actionCooldownRemainingSeconds)
    {
        try
        {
            var context = BuildCore(_gameProbe, _logger, actionCooldownRemainingSeconds);
            _logState.ReportRecovered(_logger, BazaarAgentLogEvents.ContextRecovered);
            return context;
        }
        catch (Exception ex)
        {
            _logState.ReportDegraded(_logger, () => BazaarAgentLogEvents.ContextDegraded(ex));
            return MakeDegenerate(actionCooldownRemainingSeconds);
        }
    }

    // -------------------------------------------------------------------------
    // Core builder
    // -------------------------------------------------------------------------

    private static BazaarAgentContext BuildCore(
        IBazaarAgentGameProbe gameProbe,
        IBazaarAgentLogger logger,
        double actionCooldownRemainingSeconds
    )
    {
        var appState = AppState.CurrentState;
        var runState = Data.CurrentState;
        var run = Data.Run;

        // Determine state name
        var stateName = ResolveStateName(appState, runState);

        // Replay phase/battleId come from the replay recorder facade. Read in every path —
        // including the lobby/unknown one — so external recording scripts never see the phase
        // flicker to "none" while a replay bootstrap is loading scenes.
        var (replayPhase, replayBattleId) = ReadReplayPhase();

        // If game isn't ready (no active AppState), the only meaningful action the
        // mod can offer is StartOrContinueRun — but only when actually at hero-select.
        if (appState == null || stateName == BazaarAgentRunStateName.Unknown)
        {
            bool canStartEarly = BazaarAgentSceneProbe.IsAtHeroSelectAndReadyForNewRun(logger);
            var lobbyActions = canStartEarly
                ? new[] { WaitOption(), StartOrContinueRunOption() }
                : new[] { WaitOption() };
            return new BazaarAgentContext
            {
                SchemaVersion = BazaarAgentSchema.Version,
                ServerTimeUtc = UtcNow(),
                StateName = BazaarAgentRunStateName.Unknown,
                CanStartOrContinueRun = canStartEarly,
                IsClientBusy = ReadClientBusy(),
                ActionCooldownRemainingSeconds = actionCooldownRemainingSeconds,
                ReplayPhase = replayPhase,
                ReplayBattleId = replayBattleId,
                AvailableActions = lobbyActions,
            };
        }

        // AllowedOps — use the public accessor
        bool canHandleOp(StateOps op) => appState.CanHandleOperation(op);

        bool isInRun = run != null && appState is RunAppState;
        bool hasActiveRun = Data.HasActiveRun;
        // CanStartOrContinueRun: true on the hero-select scene with profile loaded
        // and no active AppState — see BazaarAgentSceneProbe for the conservative check.
        bool canStartOrContinueRun = BazaarAgentSceneProbe.IsAtHeroSelectAndReadyForNewRun(logger);

        string? runId = null;
        if (run != null && run.GameModeId != default(Guid))
            runId = run.GameModeId.ToString("D");

        // Player gold via attribute system
        int playerGold = run?.Player?.GetAttributeValue(EPlayerAttributeType.Gold) ?? 0;
        int? playerIncome = run?.Player?.GetAttributeValue(EPlayerAttributeType.Income);
        int? playerHealth = run?.Player?.GetAttributeValue(EPlayerAttributeType.Health);
        int? playerMaxHealth = run?.Player?.GetAttributeValue(EPlayerAttributeType.HealthMax);
        int? playerPrestige = run?.Player?.GetAttributeValue(EPlayerAttributeType.Prestige);
        int? playerLevel = run?.Player?.GetAttributeValue(EPlayerAttributeType.Level);

        bool selectionIsFree = runState?.SelectionContextRules?.SelectionIsFree ?? false;
        bool canExit = runState?.SelectionContextRules?.CanExit ?? false;

        int rerollCost = (int)(runState?.RerollCost ?? 0u);
        int rerollsRemaining = (int)(runState?.RerollsRemaining ?? 0u);

        bool canReroll =
            canHandleOp(StateOps.Reroll) && rerollsRemaining > 0 && playerGold >= rerollCost;

        var encounterIdsOutcome = gameProbe is IBazaarAgentTypedGameProbe typedGameProbe
            ? typedGameProbe.GetEncounterIdsOutcome()
            : BazaarAgentGameProbeOutcome<EncounterIdsSnapshot>.Success(
                gameProbe.GetEncounterIds()
            );
        if (!encounterIdsOutcome.IsSuccess)
            throw new InvalidOperationException(
                "The encounter-id probe degraded while building agent context.",
                encounterIdsOutcome.Exception
            );
        var targetingOutcome = gameProbe is IBazaarAgentTypedGameProbe typedTargetingProbe
            ? typedTargetingProbe.GetTargetingStateOutcome()
            : BazaarAgentGameProbeOutcome<EncounterTargetingSnapshot>.Success(
                gameProbe.GetTargetingState()
            );
        if (!targetingOutcome.IsSuccess)
            throw new InvalidOperationException(
                "The encounter-targeting probe degraded while building agent context.",
                targetingOutcome.Exception
            );
        var encounterIds = encounterIdsOutcome.Snapshot;
        var targeting = targetingOutcome.Snapshot;

        string? currentEncounterId = encounterIds.CurrentEncounterId;
        string? currentEncounterType = gameProbe.ResolveEncounterType(currentEncounterId);

        // --- Card inventories ---
        bool canSell = canHandleOp(StateOps.SellItem);
        bool canMove = canHandleOp(StateOps.MoveItem);

        var boardItems = BuildBoardCards(run, playerGold, canSell, BazaarAgentCardLocation.Board);
        var chestItems = BuildBoardCards(run, playerGold, canSell, BazaarAgentCardLocation.Chest);
        var playerSkills = BuildSkillCards(run, canSell);

        var sellableItems = BuildSellableItems(boardItems, chestItems, canSell);

        // Compute occupied sockets once per build — placement hints, MoveItem emission,
        // and SelectItem enumeration all read the same immutable-during-build containers.
        var handContainer = (run?.Player?.Hand as CardContainer)?.Container;
        var stashContainer = (run?.Player?.Stash as CardContainer)?.Container;
        var occupiedHand = GetOccupiedAndLockedSockets(handContainer);
        var occupiedStash = GetOccupiedAndLockedSockets(stashContainer);

        // Selection set
        List<BazaarAgentCardSnapshot> selectionOptions = BuildSelectionOptions(
            runState,
            playerGold,
            selectionIsFree,
            canHandleOp(StateOps.SelectItem),
            occupiedHand,
            occupiedStash
        );

        // Target-selection mode (upgrade/enchant): when AppState._iteractionFilter
        // is non-empty, the game restricts SelectItem to owned cards whose
        // templateId is in the filter. Offer-based clicks silently no-op.
        var interactionFilterList = targeting.InteractionFilterTemplateIds;
        ISet<string>? interactionFilter =
            interactionFilterList.Count > 0 ? new HashSet<string>(interactionFilterList) : null;

        // End-of-run advance: the end screen exposes no StateOps. Mirror the native button's guard
        // (SceneLoader not transitioning) — EndOfRunScreenController.ReturnToMenuClicked.
        var sceneLoader = Services.Get<SceneLoader>();
        bool endScreenInteractable = sceneLoader != null && !sceneLoader.IsTransitioning;

        // Available actions
        var actions = BuildActions(
            stateName,
            replayPhase,
            isInRun,
            canHandleOp,
            canReroll,
            canStartOrContinueRun,
            runState,
            selectionOptions,
            boardItems,
            chestItems,
            playerSkills,
            canMove,
            canSell,
            run,
            occupiedHand,
            occupiedStash,
            targeting.PedestalEligibleInstanceIds,
            endScreenInteractable
        );

        if (interactionFilter is not null)
        {
            actions = BazaarAgentTargetSelectionActions.ApplyTargetSelectionFilter(
                actions,
                interactionFilter,
                boardItems,
                chestItems,
                playerSkills,
                selectionOptions
            );
        }

        return new BazaarAgentContext
        {
            SchemaVersion = BazaarAgentSchema.Version,
            TickId = 0,
            ServerTimeUtc = UtcNow(),
            IsInRun = isInRun,
            HasActiveRun = hasActiveRun,
            CanStartOrContinueRun = canStartOrContinueRun,
            IsClientBusy = ReadClientBusy(),
            RunId = runId,
            StateName = stateName,
            PlayerHero = run?.Player?.Hero.ToString(),
            Day = run == null ? null : unchecked((int)run.Day),
            Hour = run == null ? null : unchecked((int)run.Hour),
            Wins = run == null ? null : unchecked((int)run.Victories),
            Losses = run == null ? null : unchecked((int)run.Losses),
            PlayerGold = playerGold,
            PlayerIncome = playerIncome,
            PlayerHealth = playerHealth,
            PlayerMaxHealth = playerMaxHealth,
            PlayerPrestige = playerPrestige,
            PlayerLevel = playerLevel,
            SelectionIsFree = selectionIsFree,
            CanExit = canExit,
            CanReroll = canReroll,
            RerollCost = rerollCost,
            RerollsRemaining = rerollsRemaining,
            CurrentEncounterId = currentEncounterId,
            CurrentEncounterType = currentEncounterType,
            ActionCooldownRemainingSeconds = actionCooldownRemainingSeconds,
            ReplayPhase = replayPhase,
            ReplayBattleId = replayBattleId,
            InteractableTemplateIds = interactionFilter is not null ? interactionFilterList : null,
            BoardItems = boardItems,
            ChestItems = chestItems,
            PlayerSkills = playerSkills,
            SellableItems = sellableItems,
            SelectionOptions = selectionOptions,
            AvailableActions = actions,
        };
    }

    // -------------------------------------------------------------------------
    // Replay phase
    // -------------------------------------------------------------------------

    private static (BazaarAgentReplayPhase Phase, string? BattleId) ReadReplayPhase()
    {
        var recorder = BazaarAgentGameBridge.CurrentRecorder;
        if (recorder is null)
            return (BazaarAgentReplayPhase.None, null);

        var snapshot = recorder.GetReplayPhase();
        var phase = snapshot.Phase switch
        {
            BppReplayPhase.Starting => BazaarAgentReplayPhase.Starting,
            BppReplayPhase.Playing => BazaarAgentReplayPhase.Playing,
            BppReplayPhase.FinishedAwaitingContinue =>
                BazaarAgentReplayPhase.FinishedAwaitingContinue,
            _ => BazaarAgentReplayPhase.None,
        };
        return (phase, snapshot.BattleId);
    }

    // -------------------------------------------------------------------------
    // State name resolution
    // -------------------------------------------------------------------------

    private static BazaarAgentRunStateName ResolveStateName(AppState? appState, RunState? runState)
    {
        if (appState is StartRunAppState)
            return BazaarAgentRunStateName.StartRun;
        if (appState is ReplayState)
            return BazaarAgentRunStateName.Replay;

        if (runState == null)
            return BazaarAgentRunStateName.Unknown;

        return runState.StateName switch
        {
            ERunState.Choice => BazaarAgentRunStateName.Choice,
            ERunState.Encounter => BazaarAgentRunStateName.Encounter,
            ERunState.Combat => BazaarAgentRunStateName.Combat,
            ERunState.LevelUp => BazaarAgentRunStateName.LevelUp,
            ERunState.Loot => BazaarAgentRunStateName.Loot,
            ERunState.Pedestal => BazaarAgentRunStateName.Pedestal,
            ERunState.PVPCombat => BazaarAgentRunStateName.PvpCombat,
            ERunState.EndRunVictory => BazaarAgentRunStateName.EndRunVictory,
            ERunState.EndRunDefeat => BazaarAgentRunStateName.EndRunDefeat,
            _ => BazaarAgentRunStateName.Unknown,
        };
    }

    // -------------------------------------------------------------------------
    // Card snapshot builders
    // -------------------------------------------------------------------------

    private static IReadOnlyList<BazaarAgentCardSnapshot> BuildBoardCards(
        Run? run,
        int playerGold,
        bool canSell,
        BazaarAgentCardLocation location
    )
    {
        if (run?.Player == null)
            return Array.Empty<BazaarAgentCardSnapshot>();

        var inventory =
            location == BazaarAgentCardLocation.Board ? run.Player.Hand : run.Player.Stash;

        if (inventory == null)
            return Array.Empty<BazaarAgentCardSnapshot>();

        var result = new List<BazaarAgentCardSnapshot>();
        int order = 0;

        var container = (inventory as CardContainer)?.Container;
        if (container == null)
            return Array.Empty<BazaarAgentCardSnapshot>();

        foreach (var (socketable, socketId) in container.GetCardsAndSockets())
        {
            if (socketable is not ItemCard card)
                continue;

            int? sellPrice = card.GetAttributeValue(ECardAttributeType.SellPrice);
            result.Add(
                new BazaarAgentCardSnapshot
                {
                    InstanceId = card.InstanceId.Value ?? "",
                    Kind = BazaarAgentCardKind.Item,
                    Type = card.Type.ToString(),
                    TemplateId = card.TemplateId.ToString("D"),
                    DisplayName = card.Name,
                    Tier = card.Tier.ToString(),
                    Size = card.Size.ToString(),
                    Enchantment = card.Enchantment?.ToString(),
                    SocketId = socketId.ToString(),
                    Location = location,
                    Order = order++,
                    Tags = BuildStringList(card.Tags),
                    HiddenTags = BuildStringList(card.HiddenTags),
                    Attributes = BuildAttributes(card),
                    ActiveAbilities = BuildActiveAbilities(card),
                    SellPrice = sellPrice,
                    CanSell = canSell && !card.HiddenTags.Contains(EHiddenTag.Unsellable),
                }
            );
        }

        return result;
    }

    private static IReadOnlyList<BazaarAgentCardSnapshot> BuildSkillCards(Run? run, bool canSell)
    {
        if (run?.Player?.Skills == null)
            return Array.Empty<BazaarAgentCardSnapshot>();

        var result = new List<BazaarAgentCardSnapshot>();
        int order = 0;

        foreach (var skill in run.Player.Skills)
        {
            int? sellPrice = skill.GetAttributeValue(ECardAttributeType.SellPrice);
            result.Add(
                new BazaarAgentCardSnapshot
                {
                    InstanceId = skill.InstanceId.Value ?? "",
                    Kind = BazaarAgentCardKind.Skill,
                    Type = skill.Type.ToString(),
                    TemplateId = skill.TemplateId.ToString("D"),
                    DisplayName = skill.Name,
                    Tier = skill.Tier.ToString(),
                    Size = skill.Size.ToString(),
                    SocketId = null,
                    Location = BazaarAgentCardLocation.Skill,
                    Order = order++,
                    Tags = BuildStringList(skill.Tags),
                    HiddenTags = BuildStringList(skill.HiddenTags),
                    Attributes = BuildAttributes(skill),
                    ActiveAbilities = BuildActiveAbilities(skill),
                    SellPrice = sellPrice,
                    CanSell = canSell && !skill.HiddenTags.Contains(EHiddenTag.Unsellable),
                }
            );
        }

        return result;
    }

    private static IReadOnlyList<BazaarAgentCardSnapshot> BuildSellableItems(
        IReadOnlyList<BazaarAgentCardSnapshot> boardItems,
        IReadOnlyList<BazaarAgentCardSnapshot> chestItems,
        bool canSell
    )
    {
        if (!canSell)
            return Array.Empty<BazaarAgentCardSnapshot>();

        var result = new List<BazaarAgentCardSnapshot>();
        foreach (var c in boardItems)
            if (c.CanSell == true)
                result.Add(c);
        foreach (var c in chestItems)
            if (c.CanSell == true)
                result.Add(c);
        return result;
    }

    private static List<BazaarAgentCardSnapshot> BuildSelectionOptions(
        RunState? runState,
        int playerGold,
        bool selectionIsFree,
        bool canSelectItem,
        HashSet<int> occupiedHand,
        HashSet<int> occupiedStash
    )
    {
        var result = new List<BazaarAgentCardSnapshot>();
        if (runState?.SelectionSet == null)
            return result;

        int handCapacity = SocketedContainer.SocketCount;
        int stashCapacity = SocketedContainer.SocketCount;

        int order = 0;
        foreach (var entry in runState.SelectionSet)
        {
            var instanceId = InstanceId.TryParse(entry);
            if (!Data.Entities.TryGetValue(instanceId, out var card))
                continue;

            BazaarAgentCardKind kind;
            if (card is ItemCard)
                kind = BazaarAgentCardKind.Item;
            else if (card is SkillCard)
                kind = BazaarAgentCardKind.Skill;
            else
                kind = BazaarAgentCardKind.Encounter; // EncounterCard or similar

            int? buyPrice = card.GetAttributeValue(ECardAttributeType.BuyPrice);
            int? sellPrice = card.GetAttributeValue(ECardAttributeType.SellPrice);
            bool canAfford = selectionIsFree || playerGold >= (buyPrice ?? 0);

            // Determine first legal placement for informational hint
            BazaarAgentTargetSection? targetSection = null;
            string? targetSockets = null;
            bool canFit = false;

            if (kind == BazaarAgentCardKind.Item)
            {
                int size = (int)card.Size;
                var handPlacements = BazaarAgentMoveTargetPlanner.Enumerate(
                    size,
                    handCapacity,
                    occupiedHand
                );
                var stashPlacements = BazaarAgentMoveTargetPlanner.Enumerate(
                    size,
                    stashCapacity,
                    occupiedStash
                );

                if (handPlacements.Count > 0)
                {
                    canFit = true;
                    targetSection = BazaarAgentTargetSection.Hand;
                    targetSockets = string.Join(",", handPlacements[0]);
                }
                else if (stashPlacements.Count > 0)
                {
                    canFit = true;
                    targetSection = BazaarAgentTargetSection.Stash;
                    targetSockets = string.Join(",", stashPlacements[0]);
                }
            }
            else if (kind == BazaarAgentCardKind.Skill)
            {
                canFit = true; // skill sockets handled separately
                targetSection = BazaarAgentTargetSection.Skill;
            }
            else
            {
                canFit = true; // encounters don't need a placement
            }

            var canSelect = kind switch
            {
                BazaarAgentCardKind.Item => canSelectItem && canAfford && canFit,
                BazaarAgentCardKind.Skill => canAfford && canFit,
                _ => true,
            };

            result.Add(
                new BazaarAgentCardSnapshot
                {
                    InstanceId = card.InstanceId.Value ?? "",
                    Kind = kind,
                    Type = card.Type.ToString(),
                    TemplateId = card.TemplateId.ToString("D"),
                    DisplayName = card.Name,
                    Tier = card.Tier.ToString(),
                    Size = card.Size.ToString(),
                    Enchantment = (card as ItemCard)?.Enchantment?.ToString(),
                    SocketId = null,
                    Location = BazaarAgentCardLocation.Selection,
                    Order = order++,
                    Tags = BuildStringList(card.Tags),
                    HiddenTags = BuildStringList(card.HiddenTags),
                    Attributes = BuildAttributes(card),
                    ActiveAbilities = BuildActiveAbilities(card),
                    BuyPrice = buyPrice,
                    SellPrice = sellPrice,
                    CanAfford = canAfford,
                    CanFit = canFit,
                    CanSelect = canSelect,
                    IsFree = selectionIsFree,
                    TargetSection = targetSection,
                    TargetSockets = targetSockets,
                }
            );
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Available actions builder
    // -------------------------------------------------------------------------

    private static IReadOnlyList<BazaarAgentDecisionOption> BuildActions(
        BazaarAgentRunStateName stateName,
        BazaarAgentReplayPhase replayPhase,
        bool isInRun,
        Func<StateOps, bool> canHandleOp,
        bool canReroll,
        bool canStartOrContinueRun,
        RunState? runState,
        List<BazaarAgentCardSnapshot> selectionOptions,
        IReadOnlyList<BazaarAgentCardSnapshot> boardItems,
        IReadOnlyList<BazaarAgentCardSnapshot> chestItems,
        IReadOnlyList<BazaarAgentCardSnapshot> playerSkills,
        bool canMove,
        bool canSell,
        Run? run,
        HashSet<int> occupiedHand,
        HashSet<int> occupiedStash,
        HashSet<string> pedestalEligibleIds,
        bool endScreenInteractable
    )
    {
        var actions = new List<BazaarAgentDecisionOption>();

        // 1. Wait (always first)
        actions.Add(WaitOption());

        // 1b. Continue — replay finished and awaiting the continue button. Surface it as a generic
        // Flow advance so the replay-agnostic agent can proceed; the dispatcher routes Continue to
        // CombatReplayRuntime.TryContinueReplay (ADR-0008). No card, no target.
        if (replayPhase == BazaarAgentReplayPhase.FinishedAwaitingContinue)
        {
            actions.Add(
                new BazaarAgentDecisionOption
                {
                    ActionKind = BazaarAgentActionKind.Continue,
                    Group = BazaarAgentActionGroup.Flow,
                    DisplayKey = "Continue",
                }
            );
        }

        // 2. StartOrContinueRun
        if (canStartOrContinueRun)
        {
            actions.Add(
                new BazaarAgentDecisionOption
                {
                    ActionKind = BazaarAgentActionKind.StartOrContinueRun,
                    Group = BazaarAgentActionGroup.Flow,
                    DisplayKey = "StartOrContinueRun",
                }
            );
        }

        // 3. AbandonRun
        static bool isEndOrReplay(BazaarAgentRunStateName s) =>
            s
                is BazaarAgentRunStateName.Combat
                    or BazaarAgentRunStateName.PvpCombat
                    or BazaarAgentRunStateName.Replay
                    or BazaarAgentRunStateName.EndRunVictory
                    or BazaarAgentRunStateName.EndRunDefeat;

        if (isInRun && !isEndOrReplay(stateName) && canHandleOp(StateOps.AbandonRun))
        {
            actions.Add(
                new BazaarAgentDecisionOption
                {
                    ActionKind = BazaarAgentActionKind.AbandonRun,
                    Group = BazaarAgentActionGroup.Flow,
                    DisplayKey = "AbandonRun",
                }
            );
        }

        // 4. Reroll
        if (canReroll)
        {
            actions.Add(
                new BazaarAgentDecisionOption
                {
                    ActionKind = BazaarAgentActionKind.Reroll,
                    Group = BazaarAgentActionGroup.Reroll,
                    DisplayKey = "Reroll",
                }
            );
        }

        // 5. ExitState
        if (canHandleOp(StateOps.ExitState) && runState?.SelectionContextRules?.CanExit != false)
        {
            actions.Add(
                new BazaarAgentDecisionOption
                {
                    ActionKind = BazaarAgentActionKind.ExitState,
                    Group = BazaarAgentActionGroup.Exit,
                    DisplayKey = "ExitState",
                }
            );
        }

        // 6. SellItem — per-card
        if (canSell && canHandleOp(StateOps.SellItem))
        {
            foreach (var card in SellableSnapshotsFrom(boardItems, chestItems))
            {
                if (card.CanSell != true)
                    continue;
                actions.Add(
                    new BazaarAgentDecisionOption
                    {
                        ActionKind = BazaarAgentActionKind.SellItem,
                        Group = BazaarAgentActionGroup.Sell,
                        DisplayKey = $"SellItem:{card.InstanceId}",
                        CardInstanceId = card.InstanceId,
                        Card = card,
                    }
                );
            }
        }

        // 7. MoveItem — per-card per-placement (board + chest)
        if (canMove && canHandleOp(StateOps.MoveItem) && run?.Player != null)
        {
            int cap = SocketedContainer.SocketCount;

            EmitMoveActions(actions, boardItems, occupiedHand, occupiedStash, cap, isOwnHand: true);
            EmitMoveActions(
                actions,
                chestItems,
                occupiedHand,
                occupiedStash,
                cap,
                isOwnHand: false
            );
        }

        // 8. SelectItem / SelectSkill / SelectEncounter — per offer
        foreach (var offer in selectionOptions)
        {
            if (offer.CanSelect == false)
                continue;

            if (offer.Kind == BazaarAgentCardKind.Item && canHandleOp(StateOps.SelectItem))
            {
                // Enumerate legal placements (same logic as in BuildSelectionOptions but authoritative)
                int size = BazaarAgentCardSize.Parse(offer.Size, fallback: 0);
                int cap = SocketedContainer.SocketCount;

                foreach (
                    var placement in BazaarAgentMoveTargetPlanner.Enumerate(size, cap, occupiedHand)
                )
                {
                    var sockets = new List<string>(placement);
                    actions.Add(
                        new BazaarAgentDecisionOption
                        {
                            ActionKind = BazaarAgentActionKind.SelectItem,
                            Group = BazaarAgentActionGroup.Offer,
                            DisplayKey =
                                $"SelectItem:{offer.InstanceId}:Hand:{string.Join(",", sockets)}",
                            CardInstanceId = offer.InstanceId,
                            TargetSection = BazaarAgentTargetSection.Hand,
                            TargetSockets = sockets,
                            Card = offer,
                        }
                    );
                }
                foreach (
                    var placement in BazaarAgentMoveTargetPlanner.Enumerate(
                        size,
                        cap,
                        occupiedStash
                    )
                )
                {
                    var sockets = new List<string>(placement);
                    actions.Add(
                        new BazaarAgentDecisionOption
                        {
                            ActionKind = BazaarAgentActionKind.SelectItem,
                            Group = BazaarAgentActionGroup.Offer,
                            DisplayKey =
                                $"SelectItem:{offer.InstanceId}:Stash:{string.Join(",", sockets)}",
                            CardInstanceId = offer.InstanceId,
                            TargetSection = BazaarAgentTargetSection.Stash,
                            TargetSockets = sockets,
                            Card = offer,
                        }
                    );
                }
            }
            else if (offer.Kind == BazaarAgentCardKind.Skill && canHandleOp(StateOps.SelectSkill))
            {
                actions.Add(
                    new BazaarAgentDecisionOption
                    {
                        ActionKind = BazaarAgentActionKind.SelectSkill,
                        Group = BazaarAgentActionGroup.Offer,
                        DisplayKey = $"SelectSkill:{offer.InstanceId}",
                        CardInstanceId = offer.InstanceId,
                        Card = offer,
                    }
                );
            }
            else if (
                offer.Kind == BazaarAgentCardKind.Encounter
                && canHandleOp(StateOps.SelectEncounter)
            )
            {
                actions.Add(
                    new BazaarAgentDecisionOption
                    {
                        ActionKind = BazaarAgentActionKind.SelectEncounter,
                        Group = BazaarAgentActionGroup.Offer,
                        DisplayKey = $"SelectEncounter:{offer.InstanceId}",
                        CardInstanceId = offer.InstanceId,
                        Card = offer,
                    }
                );
            }
        }

        // 9. CommitToPedestal — per owned item card that the active pedestal template
        // marks as a valid upgrade target. The eligibility set is computed once per
        // tick via the probe (which reflects on PedestalState._validCards), so a
        // pedestal with N owned items costs O(N) per snapshot instead of the O(N²)
        // we'd pay calling CanBeUpgraded(card) per card. Cards mid-transition (when
        // AppState.CurrentState is briefly not yet a PedestalState even though
        // stateName resolves to Pedestal) yield an empty eligibility set and no
        // CommitToPedestal options — picker should ExitState in that window.
        if (stateName == BazaarAgentRunStateName.Pedestal && canHandleOp(StateOps.CommitToPedestal))
        {
            var eligibleIds = pedestalEligibleIds;
            foreach (var card in boardItems)
            {
                if (!eligibleIds.Contains(card.InstanceId))
                    continue;
                actions.Add(
                    new BazaarAgentDecisionOption
                    {
                        ActionKind = BazaarAgentActionKind.CommitToPedestal,
                        Group = BazaarAgentActionGroup.Pedestal,
                        DisplayKey = $"CommitToPedestal:{card.InstanceId}",
                        CardInstanceId = card.InstanceId,
                        Card = card,
                    }
                );
            }
            foreach (var card in chestItems)
            {
                if (!eligibleIds.Contains(card.InstanceId))
                    continue;
                actions.Add(
                    new BazaarAgentDecisionOption
                    {
                        ActionKind = BazaarAgentActionKind.CommitToPedestal,
                        Group = BazaarAgentActionGroup.Pedestal,
                        DisplayKey = $"CommitToPedestal:{card.InstanceId}",
                        CardInstanceId = card.InstanceId,
                        Card = card,
                    }
                );
            }
        }

        // 10. ReturnToMenu — end-of-run states expose no StateOps; this advances the end screen
        // back to hero-select (the dispatcher calls RunManager.LoadMainMenu). Guarded to mirror the
        // native button: only while the scene loader is not transitioning.
        if (
            (
                stateName == BazaarAgentRunStateName.EndRunVictory
                || stateName == BazaarAgentRunStateName.EndRunDefeat
            ) && endScreenInteractable
        )
        {
            actions.Add(
                new BazaarAgentDecisionOption
                {
                    ActionKind = BazaarAgentActionKind.ReturnToMenu,
                    Group = BazaarAgentActionGroup.Flow,
                    DisplayKey = "ReturnToMenu",
                }
            );
        }

        return actions;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void EmitMoveActions(
        List<BazaarAgentDecisionOption> actions,
        IReadOnlyList<BazaarAgentCardSnapshot> cards,
        ISet<int> occupiedHand,
        ISet<int> occupiedStash,
        int cap,
        bool isOwnHand
    )
    {
        foreach (var card in cards)
        {
            int size = BazaarAgentCardSize.Parse(card.Size, fallback: 0);

            // Hand placements
            var ownLeftSocket = isOwnHand ? ParseSocketIndex(card.SocketId) : -1;
            var handPlacements = BazaarAgentMoveTargetPlanner.Enumerate(
                size,
                cap,
                occupiedHand,
                excludeStartIndexInclusive: isOwnHand ? ownLeftSocket : -1,
                excludeCountInclusive: isOwnHand ? size : 0
            );

            foreach (var placement in handPlacements)
            {
                var sockets = new List<string>(placement);
                actions.Add(
                    new BazaarAgentDecisionOption
                    {
                        ActionKind = BazaarAgentActionKind.MoveItem,
                        Group = BazaarAgentActionGroup.Move,
                        DisplayKey = $"MoveItem:{card.InstanceId}:Hand:{string.Join(",", sockets)}",
                        CardInstanceId = card.InstanceId,
                        TargetSection = BazaarAgentTargetSection.Hand,
                        TargetSockets = sockets,
                        Card = card,
                    }
                );
            }

            // Stash placements
            var ownStashLeftSocket = !isOwnHand ? ParseSocketIndex(card.SocketId) : -1;
            var stashPlacements = BazaarAgentMoveTargetPlanner.Enumerate(
                size,
                cap,
                occupiedStash,
                excludeStartIndexInclusive: !isOwnHand ? ownStashLeftSocket : -1,
                excludeCountInclusive: !isOwnHand ? size : 0
            );

            foreach (var placement in stashPlacements)
            {
                var sockets = new List<string>(placement);
                actions.Add(
                    new BazaarAgentDecisionOption
                    {
                        ActionKind = BazaarAgentActionKind.MoveItem,
                        Group = BazaarAgentActionGroup.Move,
                        DisplayKey =
                            $"MoveItem:{card.InstanceId}:Stash:{string.Join(",", sockets)}",
                        CardInstanceId = card.InstanceId,
                        TargetSection = BazaarAgentTargetSection.Stash,
                        TargetSockets = sockets,
                        Card = card,
                    }
                );
            }
        }
    }

    private static HashSet<int> GetOccupiedAndLockedSockets(SocketedContainer? container)
    {
        var result = new HashSet<int>();
        if (container == null)
            return result;

        for (int i = 0; i < container.Sockets.Length; i++)
        {
            if (container.Sockets[i] != null || container.IsSocketLocked(i))
                result.Add(i);
        }

        return result;
    }

    private static IEnumerable<BazaarAgentCardSnapshot> SellableSnapshotsFrom(
        IReadOnlyList<BazaarAgentCardSnapshot> board,
        IReadOnlyList<BazaarAgentCardSnapshot> chest
    )
    {
        foreach (var c in board)
            yield return c;
        foreach (var c in chest)
            yield return c;
    }

    private static int ParseSocketIndex(string? socketId)
    {
        // e.g. "Socket_3" → 3
        if (socketId == null)
            return -1;
        var idx = socketId.LastIndexOf('_');
        if (idx < 0 || idx + 1 >= socketId.Length)
            return -1;
        if (int.TryParse(socketId.AsSpan(idx + 1), out var n))
            return n;
        return -1;
    }

    private static IReadOnlyDictionary<string, int> BuildAttributes(Card card)
    {
        var result = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (card.Attributes == null)
            return result;
        foreach (var kv in card.Attributes)
        {
            result[kv.Key.ToString()] = kv.Value;
        }
        return result;
    }

    private static IReadOnlyList<string> BuildStringList<T>(IEnumerable<T>? source)
    {
        if (source is null)
            return Array.Empty<string>();
        var result = new List<string>();
        foreach (var value in source)
        {
            if (value is null)
                continue;
            result.Add(value.ToString() ?? "");
        }
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static IReadOnlyList<BazaarAgentCardAbilitySnapshot> BuildActiveAbilities(Card card)
    {
        try
        {
            var result = new List<BazaarAgentCardAbilitySnapshot>();
            foreach (var ability in card.GetActiveAbilities())
            {
                if (ability is null)
                    continue;
                result.Add(
                    new BazaarAgentCardAbilitySnapshot
                    {
                        Id = ability.Id,
                        InternalName = EmptyAsNull(ability.InternalName),
                        InternalDescription = EmptyAsNull(ability.InternalDescription),
                        Trigger = ability.Trigger?.GetType().Name,
                        Action = ability.Action?.GetType().Name,
                        ActiveIn = ability.ActiveIn.ToString(),
                        WorksIn = ability.WorksIn.ToString(),
                        Priority = ability.Priority.ToString(),
                    }
                );
            }
            result.Sort((x, y) => string.CompareOrdinal(x.Id, y.Id));
            return result;
        }
        catch
        {
            return Array.Empty<BazaarAgentCardAbilitySnapshot>();
        }
    }

    private static string? EmptyAsNull(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static BazaarAgentDecisionOption WaitOption() =>
        new()
        {
            ActionKind = BazaarAgentActionKind.Wait,
            Group = BazaarAgentActionGroup.Wait,
            DisplayKey = "Wait",
        };

    private static BazaarAgentDecisionOption StartOrContinueRunOption() =>
        new()
        {
            ActionKind = BazaarAgentActionKind.StartOrContinueRun,
            Group = BazaarAgentActionGroup.Flow,
            DisplayKey = "StartOrContinueRun",
        };

    private static BazaarAgentContext MakeDegenerate(double cooldown) =>
        new()
        {
            SchemaVersion = BazaarAgentSchema.Version,
            ServerTimeUtc = UtcNow(),
            StateName = BazaarAgentRunStateName.Unknown,
            IsClientBusy = ReadClientBusy(),
            ActionCooldownRemainingSeconds = cooldown,
            AvailableActions = new[] { WaitOption() },
        };

    // Client-busy = waiting on a server round-trip OR input blocked by a transition/animation.
    // Read in every construction path (full, lobby, degenerate) so isClientBusy is honest at the
    // hero-select/transition windows too, not just mid-run.
    private static bool ReadClientBusy() =>
        AppState.IsWaitingForServerResponse || AppState.BlockInput;

    private static string UtcNow() =>
        DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}
