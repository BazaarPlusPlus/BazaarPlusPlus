#nullable enable
using System.Diagnostics;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.Heroes;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using MessagePack;
using TheBazaar;

namespace BazaarPlusPlus.Game.PvpBattles;

/// <summary>
/// The desktop's reads for <see cref="PvpBattleCaptureWindow{TMessage}"/>: Mono net messages, the
/// client's <c>Data</c> statics for the player, and replay parts serialized at the close. Every
/// read may throw; the window guards each one.
/// </summary>
internal sealed class DesktopPvpBattleReads : IPvpBattleReads<INetMessage>
{
    public PvpBattleSignal Classify(INetMessage message) =>
        message switch
        {
            NetMessageGameSim gameSim => PvpBattleSignal.GameSim(
                gameSim.Data?.CurrentState?.StateName.ToString()
            ),
            NetMessageCombatSim combatSim => PvpBattleSignal.CombatSim(
                combatSim.Data.Winner.ToString(),
                combatSim.Data.Loser.ToString()
            ),
            _ => PvpBattleSignal.Other,
        };

    public string? OpeningText(INetMessage opening, PvpOpeningText field)
    {
        var data = AsGameSim(opening).Data;
        if (field == PvpOpeningText.EncounterId)
            return data.CurrentState?.CurrentEncounterId;
        var opponent = Opponent(data);
        return field switch
        {
            PvpOpeningText.OpponentName => opponent?.Name,
            PvpOpeningText.OpponentHero => (
                opponent != null ? opponent.Hero : Data.Run?.Opponent?.Hero
            )
                is { } hero
                ? TheDragonsHeroIdentity.ToCanonicalId(hero)
                : null,
            PvpOpeningText.OpponentRank => opponent?.Rank?.ToString(),
            PvpOpeningText.OpponentAccountId => opponent?.PlayerLoadout?.accountId,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };
    }

    public int? OpeningNumber(INetMessage opening, PvpOpeningNumber field)
    {
        var data = AsGameSim(opening).Data;
        switch (field)
        {
            case PvpOpeningNumber.Day:
                return data.Run is { } dayRun ? unchecked((int)dayRun.Day) : null;
            case PvpOpeningNumber.Hour:
                return data.Run is { } hourRun ? unchecked((int)hourRun.Hour) : null;
        }
        if (Opponent(data) is not { } opponent)
            return null;
        return field switch
        {
            PvpOpeningNumber.OpponentRating => opponent.Rating,
            PvpOpeningNumber.OpponentLevel => opponent.Level,
            PvpOpeningNumber.OpponentPrestige => opponent.Prestige,
            PvpOpeningNumber.OpponentVictories => unchecked((int)opponent.Victories),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };
    }

    public IReadOnlyList<PvpBattleCardSnapshot> OpponentBoard(
        INetMessage opening,
        PvpSnapshotSection section
    ) =>
        section == PvpSnapshotSection.Hand
            ? OpeningHand(AsGameSim(opening), ECombatantId.Opponent)
            : OpponentSkills(AsGameSim(opening));

    public string? PlayerText(PvpPlayerText field) =>
        field switch
        {
            PvpPlayerText.Hero => Data.Run?.Player is { } player
                ? TheDragonsHeroIdentity.ToCanonicalId(player.Hero)
                : null,
            PvpPlayerText.Name => BppClientCacheBridge.TryGetProfileUsername(),
            PvpPlayerText.AccountId => BppClientCacheBridge.TryGetProfileAccountId(),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

    public int? PlayerNumber(PvpPlayerNumber field) =>
        field switch
        {
            PvpPlayerNumber.Level => Attribute(EPlayerAttributeType.Level),
            PvpPlayerNumber.Prestige => Attribute(EPlayerAttributeType.Prestige),
            PvpPlayerNumber.Income => Attribute(EPlayerAttributeType.Income),
            PvpPlayerNumber.Gold => Attribute(EPlayerAttributeType.Gold),
            PvpPlayerNumber.Victories => Data.Run is { } run ? unchecked((int)run.Victories) : null,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

    public IReadOnlyList<PvpBattleCardSnapshot> PlayerBoard(PvpSnapshotSection section) =>
        section == PvpSnapshotSection.Hand
            ? Data.GetCards<Card>(ECombatantId.Player, EInventorySection.Hand)
                .Where(card => card != null)
                .Select(CreateSnapshot)
                .ToList()
            : Data.Run?.Player?.Skills?.Where(skill => skill != null)
                .Select(CreateSkillSnapshot)
                .ToList()
                ?? [];

    // Each message is serialized as its concrete type, so the bytes carry no union header and
    // the replay loader reads them back the same way.
    public PvpReplayPart KeepReplayPart(PvpReplayStage stage, INetMessage message, string battleId)
    {
        if (stage == PvpReplayStage.Combat)
        {
            var combat = (NetMessageCombatSim)message;
            return PvpReplayPart.Deferred(() => Serialize(combat, battleId, "combat"));
        }
        var gameSim = AsGameSim(message);
        var part = stage == PvpReplayStage.Spawn ? "spawn" : "despawn";
        return PvpReplayPart.Deferred(() => Serialize(gameSim, battleId, part));
    }

    // Runs once per part when the window closes, inside the call that delivered the closing
    // message; a throw is left to the window, which fails the part. Temporary #514 probe: the
    // part_serialized event, its timing, and the assertions on it in DesktopPvpBattleCaptureTests
    // are deleted in the commit that acts on #514's measurement, or when #514 is closed.
    private static byte[] Serialize<T>(T message, string battleId, string part)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var bytes = MessagePackSerializer.Serialize(message, MessagePackConfig.Options);
        var elapsedMs = (Stopwatch.GetTimestamp() - startedAt) * 1000.0 / Stopwatch.Frequency;
        BppLog.InfoEvent(
            new BppLogEvent(
                BppLogFeatureScope.CombatReplay,
                "combat_replay.capture.part_serialized"
            ),
            ("battle_id", battleId, BppLogCorrelationPolicy.Short),
            ("part", part),
            ("bytes", bytes.Length),
            ("elapsed_ms", Math.Round(elapsedMs, 3))
        );
        return bytes;
    }

    private static NetMessageGameSim AsGameSim(INetMessage message) => (NetMessageGameSim)message;

    private static SimPvpOpponent? Opponent(GameSim data) =>
        data.CurrentState?.PvpOpponent ?? Data.SimPvpOpponent;

    private static int? Attribute(EPlayerAttributeType attribute) =>
        Data.Run?.Player?.GetAttributeValue(attribute);

    private static List<PvpBattleCardSnapshot> OpeningHand(
        NetMessageGameSim message,
        ECombatantId combatantId
    ) =>
        message
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
            .ToList();

    private static List<PvpBattleCardSnapshot> OpponentSkills(NetMessageGameSim message)
    {
        var spawnedCards = message
            .Data.Events.OfType<GameSimEventCardSpawned>()
            .ToDictionary(evt => evt.InstanceId, StringComparer.Ordinal);
        return message
            .Data.Events.OfType<GameSimEventPlayerSkillEquipped>()
            .Where(evt => evt.Owner == ECombatantId.Opponent)
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
            .ToList();
    }

    private static PvpBattleCardSnapshot CreateSnapshot(Card card)
    {
        return new PvpBattleCardSnapshot
        {
            InstanceId = card.InstanceId.ToString(),
            TemplateId = card.TemplateId.ToString(),
            Type = DesktopCardFacets.CapturedType(card.Type),
            Size = DesktopCardFacets.CapturedSize(card.Size),
            Section = DesktopCardFacets.CapturedSection(card.Section),
            Socket = DesktopCardFacets.CapturedSocket(card.LeftSocketId),
            Name = card.Template?.InternalName,
            Tier = card.Tier.ToString(),
            Enchant = card.GetEnchantment().ToString(),
            Tags = card.Tags?.Select(tag => tag.ToString()).ToList() ?? new List<string>(),
            Attributes =
                card.Attributes?.ToDictionary(entry => entry.Key.ToString(), entry => entry.Value)
                ?? new Dictionary<string, int>(),
        };
    }

    private static PvpBattleCardSnapshot CreateSkillSnapshot(SkillCard skill)
    {
        var snapshot = CreateSnapshot(skill);
        snapshot.Name = skill.Template?.Localization?.Title?.Text ?? skill.Template?.InternalName;
        return snapshot;
    }

    private static PvpBattleCardSnapshot CreateOpeningSnapshot(
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

        return new PvpBattleCardSnapshot
        {
            InstanceId = instanceId,
            TemplateId =
                spawnedCard?.TemplateId ?? existingCard?.TemplateId.ToString() ?? string.Empty,
            Type = DesktopCardFacets.CapturedType(
                spawnedCard?.Type ?? existingCard?.Type ?? fallbackType ?? ECardType.Item
            ),
            Size = DesktopCardFacets.CapturedSize(cardUpdate?.Size ?? existingCard?.Size),
            Section = DesktopCardFacets.CapturedSection(
                cardUpdate?.Placement?.Section ?? existingCard?.Section
            ),
            Socket = DesktopCardFacets.CapturedSocket(
                cardUpdate?.Placement?.Socket ?? existingCard?.LeftSocketId
            ),
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

    // The client's card cache is a fallback for the opening message: a lookup that throws, as it
    // does outside a run, leaves the message's own fields.
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
}
