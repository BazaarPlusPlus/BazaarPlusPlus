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
/// The four board captures of a PvP battle in the <see cref="PvpBattleCardSetDocument"/> shape the
/// desktop's <c>PvpBattleSnapshotCollector</c> stores, read when its opening game sim arrives: the
/// player's hand and skills from the live game state, the opponent's from the opening message's
/// spawn and skill events. A player set whose opening read threw or came back empty is read again
/// live (<see cref="RetryLive"/>), when the combat sim arrives and when the battle closes, and
/// <see cref="PvpBattleCaptureRule"/> labels each set as the desktop does. A read that throws
/// leaves that set Missing; the other three are still taken. Main thread only.
/// </summary>
internal sealed class AndroidPvpBattleSnapshots
{
    private readonly string _battleId;
    private readonly CardSet _playerHand = new("player_hand");
    private readonly CardSet _playerSkills = new("player_skills");
    private readonly CardSet _opponentHand = new("opponent_hand");
    private readonly CardSet _opponentSkills = new("opponent_skills");

    private AndroidPvpBattleSnapshots(string battleId) => _battleId = battleId;

    public static AndroidPvpBattleSnapshots Open(NetMessageGameSim opening, string battleId)
    {
        var snapshots = new AndroidPvpBattleSnapshots(battleId);
        snapshots.ReadOpening(snapshots._playerHand, PlayerHand);
        snapshots.ReadOpening(snapshots._playerSkills, PlayerSkills);
        snapshots.ReadOpening(snapshots._opponentHand, () => OpponentHand(opening));
        snapshots.ReadOpening(snapshots._opponentSkills, () => OpponentSkills(opening));
        return snapshots;
    }

    /// <summary>Reads each player set again that <see cref="PvpBattleCaptureRule"/> retries.</summary>
    public void RetryLive()
    {
        ReadLive(_playerHand, PlayerHand);
        ReadLive(_playerSkills, PlayerSkills);
    }

    public PvpBattleSnapshotDocuments Documents() =>
        new()
        {
            PlayerHand = PvpBattleCardSetDocument.Serialize(_playerHand.Document()),
            PlayerSkills = PvpBattleCardSetDocument.Serialize(_playerSkills.Document()),
            OpponentHand = PvpBattleCardSetDocument.Serialize(_opponentHand.Document()),
            OpponentSkills = PvpBattleCardSetDocument.Serialize(_opponentSkills.Document()),
        };

    /// <summary>Each set's label and card count, for the battle's recorded event.</summary>
    public IEnumerable<BppLogField> LogFields() =>
        new[] { _playerHand, _playerSkills, _opponentHand, _opponentSkills }.Select(
            set => new BppLogField(set.Label, set.Summary())
        );

    private void ReadOpening(CardSet set, Func<List<PvpBattleCardDocument>> read)
    {
        if (TryRead(set, "opening", read, out var items))
        {
            set.Items = items;
            set.FromOpening = true;
        }
    }

    private void ReadLive(CardSet set, Func<List<PvpBattleCardDocument>> read)
    {
        if (!PvpBattleCaptureRule.ShouldRetryLive(set.FromOpening, set.Live, set.Items.Count))
            return;
        // A live read that throws leaves the set empty and not captured live, as on the desktop.
        set.Live = TryRead(set, "live", read, out var items);
        set.Items = items;
    }

    private bool TryRead(
        CardSet set,
        string stage,
        Func<List<PvpBattleCardDocument>> read,
        out List<PvpBattleCardDocument> items
    )
    {
        try
        {
            items = read();
            return true;
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Android,
                    "android.pvp_battle.snapshot_degraded",
                    storm: ["set", "stage"]
                ),
                exception,
                ("set", set.Label),
                ("stage", stage),
                ("battle_id", _battleId, BppLogCorrelationPolicy.Short)
            );
            items = [];
            return false;
        }
    }

    private sealed class CardSet(string label)
    {
        public string Label { get; } = label;

        public List<PvpBattleCardDocument> Items { get; set; } = [];

        public bool FromOpening { get; set; }

        public bool Live { get; set; }

        public PvpBattleCardSetDocument Document()
        {
            var (status, source) = PvpBattleCaptureRule.Label(FromOpening, Live, Items.Count);
            return new PvpBattleCardSetDocument
            {
                Items = Items,
                Status = status,
                Source = source,
            };
        }

        public string Summary()
        {
            var (status, source) = PvpBattleCaptureRule.Label(FromOpening, Live, Items.Count);
            return $"{status}/{source}/{Items.Count}";
        }
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
