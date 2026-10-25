using System.Diagnostics;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppBazaarGameClient.Domain.Models.Cards;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppBazaarGameShared.Infra.Messages;
using Il2CppBazaarGameShared.Infra.Messages.GameSimEvents;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The Android host's reads for <see cref="PvpBattleCaptureWindow{TMessage}"/>, over IL2CPP
/// proxies: the opening message's battle, opponent and opponent boards, the player's facts and
/// boards from the live game state, and each replay part serialized as its message arrives
/// (<see cref="AndroidReplayMessages"/>), since a proxy is not kept past the call that delivered
/// it. Every read may throw; the window guards each one. Main thread only.
/// </summary>
internal sealed class AndroidPvpBattleReads : IPvpBattleReads<INetMessage>
{
    public PvpBattleSignal Classify(INetMessage message)
    {
        var gameSim = message.TryCast<NetMessageGameSim>();
        if (gameSim != null)
            return PvpBattleSignal.GameSim(gameSim.Data?.CurrentState?.StateName.ToString());
        var combatSim = message.TryCast<NetMessageCombatSim>();
        return combatSim != null
            ? PvpBattleSignal.CombatSim(
                combatSim.Data.Winner.ToString(),
                combatSim.Data.Loser.ToString()
            )
            : PvpBattleSignal.Other;
    }

    public string? OpeningText(INetMessage opening, PvpOpeningText field)
    {
        var data = AsGameSim(opening).Data;
        if (field == PvpOpeningText.EncounterId)
            return data.CurrentState?.CurrentEncounterId;
        var opponent = Opponent(data);
        return field switch
        {
            PvpOpeningText.OpponentName => opponent?.Name,
            PvpOpeningText.OpponentHero => opponent != null ? AndroidGameIds.Hero(opponent.Hero)
            : Data.Run?.Opponent is { } runOpponent ? AndroidGameIds.Hero(runOpponent.Hero)
            : null,
            PvpOpeningText.OpponentRank => opponent != null
                ? AndroidGameIds.Unboxed(() => opponent.Rank)?.ToString()
                : null,
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
            ? OpponentHand(AsGameSim(opening))
            : OpponentSkills(AsGameSim(opening));

    public string? PlayerText(PvpPlayerText field) =>
        field switch
        {
            PvpPlayerText.Hero => Data.Run?.Player is { } player
                ? AndroidGameIds.Hero(player.Hero)
                : null,
            PvpPlayerText.Name => AndroidClientCache.TryGetProfileUsername(),
            PvpPlayerText.AccountId => AndroidClientCache.TryGetProfileAccountId(),
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
        section == PvpSnapshotSection.Hand ? PlayerHand() : PlayerSkills();

    // The proxy is serialized now, so the window keeps bytes, never the message.
    public PvpReplayPart KeepReplayPart(PvpReplayStage stage, INetMessage message, string battleId)
    {
        var bytes = stage switch
        {
            PvpReplayStage.Spawn => Serialize(AsGameSim(message), battleId, "spawn"),
            PvpReplayStage.Combat => Serialize(
                message.TryCast<NetMessageCombatSim>()
                    ?? throw new InvalidCastException("The combat part is not a combat sim."),
                battleId,
                "combat"
            ),
            PvpReplayStage.Despawn => Serialize(AsGameSim(message), battleId, "despawn"),
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
        };
        return PvpReplayPart.Serialized(bytes);
    }

    private static NetMessageGameSim AsGameSim(INetMessage message) =>
        message.TryCast<NetMessageGameSim>()
        ?? throw new InvalidCastException("The message is not a game sim.");

    private static SimPvpOpponent? Opponent(GameSim data) =>
        data.CurrentState?.PvpOpponent ?? Data.SimPvpOpponent;

    private static int? Attribute(EPlayerAttributeType attribute) =>
        Data.Run?.Player is { } player ? AndroidGameIds.Attribute(player, attribute) : null;

    // One message's bytes, or none when the game's serializer failed: the battle is still
    // recorded, without its Replay Payload.
    private static byte[] Serialize<T>(T message, string battleId, string part)
        where T : Il2CppObjectBase
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var bytes = AndroidReplayMessages.Serialize(message);
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.replay.message_serialized"),
                ("battle_id", battleId, BppLogCorrelationPolicy.Short),
                ("part", part),
                ("bytes", bytes.Length),
                ("elapsed_ms", stopwatch.ElapsedMilliseconds)
            );
            return bytes;
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Android,
                    "android.replay.serialize_failed",
                    storm: ["part"]
                ),
                exception,
                ("battle_id", battleId, BppLogCorrelationPolicy.Short),
                ("part", part)
            );
            return [];
        }
    }

    // Data.GetCards<Card>(Player, Hand) without its Nullable<EInventorySection> argument: the
    // cards in Data.Entities the run's player owns in that section.
    private static List<PvpBattleCardSnapshot> PlayerHand()
    {
        var player = Data.Run?.Player;
        var cards = new List<PvpBattleCardSnapshot>();
        if (player == null)
            return cards;
        foreach (var card in Data.Entities.Values)
        {
            if (card == null || card.Owner?.Pointer != player.Pointer)
                continue;
            if (AndroidGameIds.Unboxed(() => card.Section) != EInventorySection.Hand)
                continue;
            cards.Add(FromCard(card));
        }
        return cards;
    }

    private static List<PvpBattleCardSnapshot> PlayerSkills()
    {
        var cards = new List<PvpBattleCardSnapshot>();
        var skills = Data.Run?.Player?.Skills;
        if (skills == null)
            return cards;
        foreach (var skill in skills)
        {
            if (skill == null)
                continue;
            var snapshot = FromCard(skill);
            snapshot.Name = SkillTitle(skill) ?? snapshot.Name;
            cards.Add(snapshot);
        }
        return cards;
    }

    private static List<PvpBattleCardSnapshot> OpponentHand(NetMessageGameSim opening)
    {
        var data = opening.Data;
        var spawned = new List<(EContainerSocketId Socket, PvpBattleCardSnapshot Card)>();
        foreach (var gameEvent in data.Events)
        {
            var spawn = gameEvent?.TryCast<GameSimEventCardSpawned>();
            if (spawn == null || spawn.CombatantId != ECombatantId.Opponent)
                continue;
            if (AndroidGameIds.Unboxed(() => spawn.Section) != EInventorySection.Hand)
                continue;
            spawned.Add(
                (
                    AndroidGameIds.Unboxed(() => spawn.Socket) ?? EContainerSocketId.Socket_0,
                    FromOpening(spawn.InstanceId, Update(data, spawn.InstanceId), spawn, null)
                )
            );
        }
        return spawned.OrderBy(entry => entry.Socket).Select(entry => entry.Card).ToList();
    }

    private static List<PvpBattleCardSnapshot> OpponentSkills(NetMessageGameSim opening)
    {
        var data = opening.Data;
        var spawned = new Dictionary<string, GameSimEventCardSpawned>(StringComparer.Ordinal);
        var equipped = new List<GameSimEventPlayerSkillEquipped>();
        foreach (var gameEvent in data.Events)
        {
            if (gameEvent == null)
                continue;
            var spawn = gameEvent.TryCast<GameSimEventCardSpawned>();
            if (spawn != null)
            {
                spawned[spawn.InstanceId] = spawn;
                continue;
            }
            var skill = gameEvent.TryCast<GameSimEventPlayerSkillEquipped>();
            if (skill != null && skill.Owner == ECombatantId.Opponent)
                equipped.Add(skill);
        }
        return equipped
            .Select(skill =>
                FromOpening(
                    skill.InstanceId,
                    Update(data, skill.InstanceId),
                    spawned.TryGetValue(skill.InstanceId, out var spawn) ? spawn : null,
                    ECardType.Skill
                )
            )
            .ToList();
    }

    private static SimUpdateCard? Update(GameSim data, string instanceId) =>
        data.Cards != null && data.Cards.TryGetValue(instanceId, out var update) ? update : null;

    private static PvpBattleCardSnapshot FromCard(Card card) =>
        new()
        {
            InstanceId = card.InstanceId.ToString(),
            TemplateId = card.TemplateId.ToString(),
            Type = AndroidCardFacets.CapturedType(card.Type),
            Size = AndroidCardFacets.CapturedSize(card.Size),
            Section = AndroidCardFacets.CapturedSection(AndroidGameIds.Unboxed(() => card.Section)),
            Socket = AndroidCardFacets.CapturedSocket(
                AndroidGameIds.Unboxed(() => card.LeftSocketId)
            ),
            Name = card.Template?.InternalName,
            Tier = card.Tier.ToString(),
            Enchant = Enchantment(card)?.ToString() ?? string.Empty,
            Tags = Tags(card.Tags),
            Attributes = Attributes(card.Attributes),
        };

    // The desktop's opening snapshot: the message's card update first, then the card the client
    // already holds, then the fallback type.
    private static PvpBattleCardSnapshot FromOpening(
        string instanceId,
        SimUpdateCard? update,
        GameSimEventCardSpawned? spawn,
        ECardType? fallbackType
    )
    {
        var existing = ExistingCard(instanceId);
        var attributes = new Dictionary<string, int>(StringComparer.Ordinal);
        if (update?.Attributes != null)
            foreach (var entry in update.Attributes)
                attributes[entry.Key.ToString()] = entry.Value.Value;

        var placement = update?.Placement;
        var section = placement != null ? AndroidGameIds.Unboxed(() => placement.Section) : null;
        var socket = placement != null ? AndroidGameIds.Unboxed(() => placement.Socket) : null;
        var tier = update != null ? AndroidGameIds.Unboxed(() => update.Tier) : null;
        var enchantment = update != null ? AndroidGameIds.Unboxed(() => update.Enchantment) : null;
        var size = update != null ? AndroidGameIds.Unboxed(() => update.Size) : null;
        return new PvpBattleCardSnapshot
        {
            InstanceId = instanceId,
            TemplateId = spawn?.TemplateId ?? existing?.TemplateId.ToString() ?? string.Empty,
            Type = AndroidCardFacets.CapturedType(
                spawn?.Type ?? existing?.Type ?? fallbackType ?? ECardType.Item
            ),
            Size = AndroidCardFacets.CapturedSize(size ?? existing?.Size),
            Section = AndroidCardFacets.CapturedSection(
                section
                    ?? (existing != null ? AndroidGameIds.Unboxed(() => existing.Section) : null)
            ),
            Socket = AndroidCardFacets.CapturedSocket(
                socket
                    ?? (
                        existing != null
                            ? AndroidGameIds.Unboxed(() => existing.LeftSocketId)
                            : null
                    )
            ),
            Name =
                (existing != null ? SkillTitle(existing) : null)
                ?? existing?.Template?.InternalName,
            Tier = tier?.ToString() ?? existing?.Tier.ToString(),
            Enchant =
                enchantment?.ToString()
                ?? (existing != null ? Enchantment(existing)?.ToString() ?? string.Empty : null),
            Tags =
                update?.Tags != null ? Tags(update.Tags)
                : existing != null ? Tags(existing.Tags)
                : new List<string>(),
            Attributes =
                attributes.Count > 0 ? attributes
                : existing != null ? Attributes(existing.Attributes)
                : new Dictionary<string, int>(StringComparer.Ordinal),
        };
    }

    // The client's card cache is a fallback for the opening message: a lookup that throws, as it
    // does outside a run, leaves the message's own fields.
    private static Card? ExistingCard(string instanceId)
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

    // The desktop's CardExtensions.GetEnchantment: only an item card carries one.
    private static EEnchantmentType? Enchantment(Card card)
    {
        var item = card.TryCast<ItemCard>();
        return item != null ? AndroidGameIds.Unboxed(() => item.Enchantment) : null;
    }

    private static string? SkillTitle(Card card) =>
        card.TryCast<SkillCard>() != null ? card.Template?.Localization?.Title?.Text : null;

    private static List<string> Tags(Il2CppSystem.Collections.Generic.HashSet<ECardTag>? tags)
    {
        var names = new List<string>();
        if (tags != null)
            foreach (var tag in tags)
                names.Add(tag.ToString());
        return names;
    }

    private static Dictionary<string, int> Attributes(
        Il2CppSystem.Collections.Generic.Dictionary<ECardAttributeType, int>? attributes
    )
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        if (attributes != null)
            foreach (var entry in attributes)
                values[entry.Key.ToString()] = entry.Value;
        return values;
    }
}
