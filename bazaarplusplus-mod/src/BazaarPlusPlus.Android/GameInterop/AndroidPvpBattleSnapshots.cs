using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppBazaarGameClient.Domain.Models.Cards;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppBazaarGameShared.Infra.Messages;
using Il2CppBazaarGameShared.Infra.Messages.GameSimEvents;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The four board captures of a PvP battle, taken when its opening game sim arrives, in the
/// <see cref="PvpBattleCardSetDocument"/> shape the desktop's <c>PvpBattleSnapshotCollector</c>
/// stores: the player's hand and skills from the live game state, the opponent's from the opening
/// message's spawn and skill events. The desktop's live retry of an empty player set is not taken.
/// A set that throws is stored as Missing, and the other three still are. Main thread only.
/// </summary>
internal static class AndroidPvpBattleSnapshots
{
    public static PvpBattleSnapshotDocuments Capture(NetMessageGameSim opening, string battleId) =>
        new()
        {
            PlayerHand = Document("player_hand", battleId, () => PlayerHand()),
            PlayerSkills = Document("player_skills", battleId, () => PlayerSkills()),
            OpponentHand = Document("opponent_hand", battleId, () => OpponentHand(opening)),
            OpponentSkills = Document("opponent_skills", battleId, () => OpponentSkills(opening)),
        };

    private static string Document(
        string label,
        string battleId,
        Func<List<PvpBattleCardDocument>> capture
    )
    {
        PvpBattleCardSetDocument document;
        try
        {
            var items = capture();
            document = new PvpBattleCardSetDocument
            {
                Items = items,
                Status =
                    items.Count == 0
                        ? PvpBattleCaptureStatus.CapturedEmpty
                        : PvpBattleCaptureStatus.Captured,
                Source = PvpBattleCaptureSource.OpeningMessage,
            };
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Android,
                    "android.pvp_battle.snapshot_degraded",
                    storm: ["set"]
                ),
                exception,
                ("set", label),
                ("battle_id", battleId, BppLogCorrelationPolicy.Short)
            );
            document = new PvpBattleCardSetDocument();
        }
        return PvpBattleCardSetDocument.Serialize(document);
    }

    // Data.GetCards<Card>(Player, Hand) without its Nullable<EInventorySection> argument: the
    // cards in Data.Entities the run's player owns in that section.
    private static List<PvpBattleCardDocument> PlayerHand()
    {
        var player = Data.Run?.Player;
        var cards = new List<PvpBattleCardDocument>();
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

    private static List<PvpBattleCardDocument> PlayerSkills()
    {
        var cards = new List<PvpBattleCardDocument>();
        var skills = Data.Run?.Player?.Skills;
        if (skills == null)
            return cards;
        foreach (var skill in skills)
        {
            if (skill == null)
                continue;
            var document = FromCard(skill);
            document.Name = SkillTitle(skill) ?? document.Name;
            cards.Add(document);
        }
        return cards;
    }

    private static List<PvpBattleCardDocument> OpponentHand(NetMessageGameSim opening)
    {
        var data = opening.Data;
        var spawned = new List<(EContainerSocketId Socket, PvpBattleCardDocument Card)>();
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

    private static List<PvpBattleCardDocument> OpponentSkills(NetMessageGameSim opening)
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

    private static PvpBattleCardDocument FromCard(Card card) =>
        new()
        {
            InstanceId = card.InstanceId.ToString(),
            TemplateId = card.TemplateId.ToString(),
            Type = card.Type.ToString(),
            Size = card.Size.ToString(),
            Section = AndroidGameIds.Unboxed(() => card.Section)?.ToString(),
            Socket = AndroidGameIds.Unboxed(() => card.LeftSocketId)?.ToString(),
            Name = card.Template?.InternalName,
            Tier = card.Tier.ToString(),
            Enchant = Enchantment(card)?.ToString() ?? string.Empty,
            Tags = Tags(card.Tags),
            Attributes = Attributes(card.Attributes),
        };

    // The desktop's CreateOpeningSnapshot: the message's card update first, then the card the
    // client already holds, then the fallback type.
    private static PvpBattleCardDocument FromOpening(
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
        return new PvpBattleCardDocument
        {
            InstanceId = instanceId,
            TemplateId = spawn?.TemplateId ?? existing?.TemplateId.ToString() ?? string.Empty,
            Type = (spawn?.Type ?? existing?.Type ?? fallbackType ?? default).ToString(),
            Size = (size ?? existing?.Size ?? default).ToString(),
            Section = (
                section
                ?? (existing != null ? AndroidGameIds.Unboxed(() => existing.Section) : null)
            )?.ToString(),
            Socket = (
                socket
                ?? (existing != null ? AndroidGameIds.Unboxed(() => existing.LeftSocketId) : null)
            )?.ToString(),
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
