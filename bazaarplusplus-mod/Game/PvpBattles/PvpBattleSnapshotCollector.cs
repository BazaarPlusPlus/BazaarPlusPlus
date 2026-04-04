#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.RunLogging;
using TheBazaar;

namespace BazaarPlusPlus.Game.PvpBattles;

internal sealed class PvpBattleSnapshotCollector
{
    public CombatReplaySequenceCandidate CreateOpeningCandidate(
        NetMessageGameSim message,
        string? runId
    )
    {
        RunLoggingGameDataReader.TryGetPlayerRankSnapshot(out var playerRank, out var playerRating);
        var playerHero = TryGetPlayerHeroSafe();
        var playerLevel = TryGetPlayerLevelSafe();
        var (
            opponentName,
            opponentHero,
            opponentRank,
            opponentRating,
            opponentLevel,
            opponentAccountId
        ) = CaptureOpponentIdentityAtOpening(message);
        var candidate = new CombatReplaySequenceCandidate
        {
            RunId = runId,
            PlayerHero = playerHero,
            PlayerRank = playerRank,
            PlayerRating = playerRating,
            PlayerLevel = playerLevel,
            OpponentName = opponentName,
            OpponentHero = opponentHero,
            OpponentRank = opponentRank,
            OpponentRating = opponentRating,
            OpponentLevel = opponentLevel,
            OpponentAccountId = opponentAccountId,
            SpawnMessage = message,
        };

        (candidate.PlayerHandCardsCapturedFromOpening, candidate.PlayerHandCards) =
            CaptureCurrentHandCardsAtOpening(ECombatantId.Player);
        (candidate.PlayerSkillsCapturedFromOpening, candidate.PlayerSkills) =
            CaptureCurrentSkillsAtOpening(ECombatantId.Player);
        (candidate.OpponentHandCardsCapturedFromOpening, candidate.OpponentHandCards) =
            CaptureOpeningHandCards(message, ECombatantId.Opponent);
        (candidate.OpponentSkillsCapturedFromOpening, candidate.OpponentSkills) =
            CaptureOpponentSkillsFromOpening(message);
        return candidate;
    }

    public void CaptureLiveSnapshots(CombatReplaySequenceCandidate candidate)
    {
        if (!candidate.PlayerHandCardsCapturedFromOpening && candidate.PlayerHandCards.Count == 0)
        {
            (candidate.PlayerHandCardsCapturedLive, candidate.PlayerHandCards) =
                CapturePlayerHandCards();
        }

        if (!candidate.PlayerSkillsCapturedFromOpening && candidate.PlayerSkills.Count == 0)
        {
            (candidate.PlayerSkillsCapturedLive, candidate.PlayerSkills) = CapturePlayerSkills();
        }
    }

    public PvpBattleParticipants BuildParticipants(CombatReplaySequenceCandidate candidate)
    {
        return new PvpBattleParticipants
        {
            PlayerName = TryGetPlayerNameSafe(),
            PlayerAccountId = TryGetPlayerAccountIdSafe(),
            PlayerHero = candidate.PlayerHero,
            PlayerRank = candidate.PlayerRank,
            PlayerRating = candidate.PlayerRating,
            PlayerLevel = candidate.PlayerLevel,
            OpponentName = candidate.OpponentName,
            OpponentHero = candidate.OpponentHero,
            OpponentRank = candidate.OpponentRank,
            OpponentRating = candidate.OpponentRating,
            OpponentLevel = candidate.OpponentLevel,
            OpponentAccountId = candidate.OpponentAccountId,
        };
    }

    public PvpBattleOutcome BuildOutcome(NetMessageCombatSim combatMessage)
    {
        return new PvpBattleOutcome
        {
            Result = ResolvePlayerResult(combatMessage),
            WinnerCombatantId = combatMessage.Data.Winner.ToString(),
            LoserCombatantId = combatMessage.Data.Loser.ToString(),
        };
    }

    public PvpBattleSnapshots BuildSnapshots(CombatReplaySequenceCandidate candidate)
    {
        return new PvpBattleSnapshots
        {
            PlayerHand = CreateCardSetCapture(
                candidate.PlayerHandCards,
                candidate.PlayerHandCardsCapturedFromOpening,
                candidate.PlayerHandCardsCapturedLive,
                PvpBattleCaptureSource.OpeningMessage,
                PvpBattleCaptureSource.LiveRetry
            ),
            PlayerSkills = CreateCardSetCapture(
                candidate.PlayerSkills,
                candidate.PlayerSkillsCapturedFromOpening,
                candidate.PlayerSkillsCapturedLive,
                PvpBattleCaptureSource.OpeningMessage,
                PvpBattleCaptureSource.LiveRetry
            ),
            OpponentHand = CreateCardSetCapture(
                candidate.OpponentHandCards,
                candidate.OpponentHandCardsCapturedFromOpening,
                false,
                PvpBattleCaptureSource.OpeningMessage,
                PvpBattleCaptureSource.LiveRetry
            ),
            OpponentSkills = CreateCardSetCapture(
                candidate.OpponentSkills,
                candidate.OpponentSkillsCapturedFromOpening,
                false,
                PvpBattleCaptureSource.OpeningMessage,
                PvpBattleCaptureSource.LiveRetry
            ),
        };
    }

    private static PvpBattleCardSetCapture CreateCardSetCapture(
        IReadOnlyList<CombatReplayCardSnapshot> items,
        bool capturedFromOpening,
        bool capturedLive,
        PvpBattleCaptureSource openingSource,
        PvpBattleCaptureSource liveSource
    )
    {
        var clonedItems = items.Select(item => item.Clone()).ToList();
        if (capturedFromOpening)
        {
            return new PvpBattleCardSetCapture
            {
                Items = clonedItems,
                Status =
                    clonedItems.Count == 0
                        ? PvpBattleCaptureStatus.CapturedEmpty
                        : PvpBattleCaptureStatus.Captured,
                Source = openingSource,
            };
        }

        if (capturedLive)
        {
            return new PvpBattleCardSetCapture
            {
                Items = clonedItems,
                Status =
                    clonedItems.Count == 0
                        ? PvpBattleCaptureStatus.CapturedEmpty
                        : PvpBattleCaptureStatus.Captured,
                Source = liveSource,
            };
        }

        return new PvpBattleCardSetCapture
        {
            Items = clonedItems,
            Status = PvpBattleCaptureStatus.Missing,
            Source = PvpBattleCaptureSource.Unknown,
        };
    }

    private static (
        bool Captured,
        List<CombatReplayCardSnapshot> Snapshots
    ) CapturePlayerHandCards()
    {
        try
        {
            return (true, CaptureCards(ECombatantId.Player, EInventorySection.Hand));
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayCaptureService",
                $"Unable to snapshot player hand cards for combat replay capture: {ex.Message}"
            );
            return (false, new List<CombatReplayCardSnapshot>());
        }
    }

    private static (bool Captured, List<CombatReplayCardSnapshot> Snapshots) CapturePlayerSkills()
    {
        try
        {
            return (true, CapturePlayerSkillsUnsafe());
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayCaptureService",
                $"Unable to snapshot player skills for combat replay capture: {ex.Message}"
            );
            return (false, new List<CombatReplayCardSnapshot>());
        }
    }

    private static List<CombatReplayCardSnapshot> CapturePlayerSkillsUnsafe()
    {
        return Data.Run?.Player?.Skills?.Where(skill => skill != null)
                .Select(CreateSkillSnapshot)
                .ToList()
            ?? new List<CombatReplayCardSnapshot>();
    }

    private static List<CombatReplayCardSnapshot> CaptureCards(
        ECombatantId combatantId,
        EInventorySection section
    )
    {
        return Data.GetCards<Card>(combatantId, section)
            .Where(card => card != null)
            .Select(CreateSnapshot)
            .ToList();
    }

    private static CombatReplayCardSnapshot CreateSnapshot(Card card)
    {
        return new CombatReplayCardSnapshot
        {
            InstanceId = card.InstanceId.ToString(),
            TemplateId = card.TemplateId.ToString(),
            Type = card.Type,
            Size = card.Size,
            Section = card.Section,
            Socket = card.LeftSocketId,
            Name = card.Template?.InternalName,
            Tier = card.Tier.ToString(),
            Enchant = card.GetEnchantment().ToString(),
            Tags = card.Tags?.Select(tag => tag.ToString()).ToList() ?? new List<string>(),
            Attributes =
                card.Attributes?.ToDictionary(entry => entry.Key.ToString(), entry => entry.Value)
                ?? new Dictionary<string, int>(),
        };
    }

    private static CombatReplayCardSnapshot CreateSkillSnapshot(SkillCard skill)
    {
        var snapshot = CreateSnapshot(skill);
        snapshot.Name = skill.Template?.Localization?.Title?.Text ?? skill.Template?.InternalName;
        return snapshot;
    }

    private static (
        bool Captured,
        List<CombatReplayCardSnapshot> Snapshots
    ) CaptureOpeningHandCards(NetMessageGameSim message, ECombatantId combatantId)
    {
        try
        {
            var snapshots = message
                .Data.Events.OfType<GameSimEventCardSpawned>()
                .Where(evt => evt.CombatantId == combatantId)
                .Where(evt => evt.Section == EInventorySection.Hand)
                .OrderBy(evt => evt.Socket ?? EContainerSocketId.Socket_0)
                .Select(entry =>
                    CreateOpeningSnapshot(
                        entry.InstanceId,
                        message.Data.Cards.TryGetValue(entry.InstanceId, out var cardUpdate)
                            ? cardUpdate
                            : null,
                        entry
                    )
                )
                .OfType<CombatReplayCardSnapshot>()
                .ToList();

            return (true, snapshots);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayCaptureService",
                $"Unable to capture opening {combatantId} hand cards from GameSim: {ex.Message}"
            );
            return (false, new List<CombatReplayCardSnapshot>());
        }
    }

    private static (
        bool Captured,
        List<CombatReplayCardSnapshot> Snapshots
    ) CaptureCurrentHandCardsAtOpening(ECombatantId combatantId)
    {
        try
        {
            return (true, CaptureCards(combatantId, EInventorySection.Hand));
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayCaptureService",
                $"Unable to capture opening {combatantId} hand cards from current Data: {ex.Message}"
            );
            return (false, new List<CombatReplayCardSnapshot>());
        }
    }

    private static (
        bool Captured,
        List<CombatReplayCardSnapshot> Snapshots
    ) CaptureCurrentSkillsAtOpening(ECombatantId combatantId)
    {
        try
        {
            return combatantId == ECombatantId.Player
                ? (true, CapturePlayerSkillsUnsafe())
                : (false, new List<CombatReplayCardSnapshot>());
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayCaptureService",
                $"Unable to capture opening {combatantId} skills from current Data: {ex.Message}"
            );
            return (false, new List<CombatReplayCardSnapshot>());
        }
    }

    private static (
        bool Captured,
        List<CombatReplayCardSnapshot> Snapshots
    ) CaptureOpponentSkillsFromOpening(NetMessageGameSim message)
    {
        try
        {
            var spawnedCards = message
                .Data.Events.OfType<GameSimEventCardSpawned>()
                .ToDictionary(evt => evt.InstanceId, StringComparer.Ordinal);
            var skillEvents = message
                .Data.Events.OfType<GameSimEventPlayerSkillEquipped>()
                .Where(evt => evt.Owner == ECombatantId.Opponent)
                .ToList();
            if (skillEvents.Count == 0)
                return (true, new List<CombatReplayCardSnapshot>());

            var snapshots = skillEvents
                .Select(evt =>
                {
                    message.Data.Cards.TryGetValue(evt.InstanceId, out var cardUpdate);
                    return CreateOpeningSnapshot(
                        evt.InstanceId,
                        cardUpdate,
                        spawnedCards.TryGetValue(evt.InstanceId, out var spawnedCard)
                            ? spawnedCard
                            : null,
                        fallbackType: ECardType.Skill
                    );
                })
                .OfType<CombatReplayCardSnapshot>()
                .ToList();
            return (true, snapshots);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayCaptureService",
                $"Unable to capture opening opponent skills from GameSim: {ex.Message}"
            );
            return (false, new List<CombatReplayCardSnapshot>());
        }
    }

    private static CombatReplayCardSnapshot? CreateOpeningSnapshot(
        string instanceId,
        SimUpdateCard? cardUpdate,
        GameSimEventCardSpawned? spawnedCard,
        ECardType? fallbackType = null
    )
    {
        var existingCard = TryGetExistingCardSafe(instanceId);
        var existingSkill = existingCard as SkillCard;
        var attributes =
            cardUpdate?.Attributes?.ToDictionary(
                entry => entry.Key.ToString(),
                entry => entry.Value.Value
            )
            ?? new Dictionary<string, int>();

        return new CombatReplayCardSnapshot
        {
            InstanceId = instanceId,
            TemplateId =
                spawnedCard?.TemplateId ?? existingCard?.TemplateId.ToString() ?? string.Empty,
            Type = spawnedCard?.Type ?? existingCard?.Type ?? fallbackType ?? default,
            Size = cardUpdate?.Size ?? existingCard?.Size ?? default,
            Section = cardUpdate?.Placement?.Section ?? existingCard?.Section,
            Socket = cardUpdate?.Placement?.Socket ?? existingCard?.LeftSocketId,
            Name =
                existingSkill?.Template?.Localization?.Title?.Text
                ?? existingCard?.Template?.InternalName,
            Tier = cardUpdate?.Tier?.ToString() ?? existingCard?.Tier.ToString(),
            Enchant =
                cardUpdate?.Enchantment?.ToString() ?? existingCard?.GetEnchantment().ToString(),
            Tags =
                cardUpdate?.Tags?.Select(tag => tag.ToString()).ToList()
                ?? existingCard?.Tags?.Select(tag => tag.ToString()).ToList()
                ?? new List<string>(),
            Attributes =
                attributes.Count > 0
                    ? attributes
                    : existingCard?.Attributes?.ToDictionary(
                        entry => entry.Key.ToString(),
                        entry => entry.Value
                    )
                        ?? new Dictionary<string, int>(),
        };
    }

    private static Card? TryGetExistingCardSafe(string instanceId)
    {
        try
        {
            return Data.GetCard(instanceId);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolvePlayerResult(NetMessageCombatSim message)
    {
        if (message.Data.Winner == ECombatantId.Player)
            return "win";
        if (message.Data.Loser == ECombatantId.Player)
            return "loss";
        return null;
    }

    private static string? TryGetPlayerNameSafe()
    {
        try
        {
            return BppClientCacheBridge.TryGetProfileUsername();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetPlayerAccountIdSafe()
    {
        try
        {
            return BppClientCacheBridge.TryGetProfileAccountId();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetPlayerHeroSafe()
    {
        try
        {
            var hero = Data.Run?.Player?.Hero.ToString();
            return string.IsNullOrWhiteSpace(hero) ? null : hero;
        }
        catch
        {
            return null;
        }
    }

    private static int? TryGetPlayerLevelSafe()
    {
        try
        {
            return Data.Run?.Player?.GetAttributeValue(EPlayerAttributeType.Level);
        }
        catch
        {
            return null;
        }
    }

    private static (
        string? Name,
        string? Hero,
        string? Rank,
        int? Rating,
        int? Level,
        string? AccountId
    ) CaptureOpponentIdentityAtOpening(NetMessageGameSim? message)
    {
        try
        {
            var opponent = message?.Data.CurrentState?.PvpOpponent ?? Data.SimPvpOpponent;
            var name = opponent?.Name;
            var hero = opponent?.Hero.ToString() ?? Data.Run?.Opponent?.Hero.ToString();
            var rank = opponent?.Rank?.ToString();
            int? rating = opponent != null ? opponent.Rating : null;
            int? level = opponent != null ? opponent.Level : null;
            var accountId = opponent?.PlayerLoadout?.accountId;
            return (
                string.IsNullOrWhiteSpace(name) ? null : name,
                string.IsNullOrWhiteSpace(hero) ? null : hero,
                string.IsNullOrWhiteSpace(rank) ? null : rank,
                rating,
                level,
                string.IsNullOrWhiteSpace(accountId) ? null : accountId
            );
        }
        catch
        {
            return (null, null, null, null, null, null);
        }
    }
}
