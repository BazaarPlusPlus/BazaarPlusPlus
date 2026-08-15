#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Combat;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Runs;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.Events;
using TheBazaar;

namespace BazaarPlusPlus.Game.CombatReplay;

/// <summary>
/// Captures only the two useful combat boundaries: the opening lineups and the terminal combat
/// attributes. Combat frames are deliberately never published to the agent.
/// </summary>
internal sealed class BazaarAgentCombatSummaryModule : IBppFeature, IBazaarAgentBattleSummarySource
{
    private readonly IBppEventBus _eventBus;
    private readonly object _gate = new();
    private IDisposable? _messageSubscription;
    private BazaarAgentBattleSummarySnapshot? _opening;
    private BazaarAgentBattleSummarySnapshot? _completed;
    private IReadOnlyDictionary<EPlayerAttributeType, int>? _openingPlayerAttributes;
    private IReadOnlyDictionary<EPlayerAttributeType, int>? _openingOpponentAttributes;

    public BazaarAgentCombatSummaryModule(IBppEventBus eventBus)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public void Start() => _messageSubscription = _eventBus.Subscribe<NetMessageObserved>(Observe);

    public void Stop()
    {
        _messageSubscription?.Dispose();
        _messageSubscription = null;
        lock (_gate)
        {
            _opening = null;
            _completed = null;
            _openingPlayerAttributes = null;
            _openingOpponentAttributes = null;
        }
    }

    public BazaarAgentBattleSummarySnapshot? GetOpeningSummary()
    {
        lock (_gate)
            return _opening;
    }

    public BazaarAgentBattleSummarySnapshot? GetCompletedSummary()
    {
        lock (_gate)
            return _completed;
    }

    public void AcknowledgeCompletedSummary(string summaryId)
    {
        if (string.IsNullOrWhiteSpace(summaryId))
            return;
        lock (_gate)
        {
            if (string.Equals(_completed?.SummaryId, summaryId, StringComparison.Ordinal))
            {
                _completed = null;
                if (string.Equals(_opening?.SummaryId, summaryId, StringComparison.Ordinal))
                    _opening = null;
            }
        }
    }

    private void Observe(NetMessageObserved observed)
    {
        switch (observed.Message)
        {
            case NetMessageGameSim gameSim when IsCombatOpening(gameSim):
                lock (_gate)
                {
                    _opening = CaptureOpening(gameSim);
                    _completed = null;
                    _openingPlayerAttributes = new Dictionary<EPlayerAttributeType, int>(
                        gameSim.Data.Player.Attributes
                    );
                    _openingOpponentAttributes = new Dictionary<EPlayerAttributeType, int>(
                        gameSim.Data.Opponent.Attributes
                    );
                }
                break;
            case NetMessageCombatSim combatSim:
                lock (_gate)
                {
                    if (_opening is null)
                        return;
                    _completed = BuildSummary(
                        _opening,
                        combatSim,
                        _openingPlayerAttributes ?? new Dictionary<EPlayerAttributeType, int>(),
                        _openingOpponentAttributes ?? new Dictionary<EPlayerAttributeType, int>()
                    );
                    _openingPlayerAttributes = null;
                    _openingOpponentAttributes = null;
                }
                break;
        }
    }

    private static BazaarAgentBattleSummarySnapshot CaptureOpening(NetMessageGameSim message)
    {
        return new BazaarAgentBattleSummarySnapshot
        {
            SummaryId = Guid.NewGuid().ToString("N"),
            Phase = "starting",
            BattleType =
                message.Data.CurrentState?.StateName == ERunState.PVPCombat ? "pvp" : "pve",
            Player = CaptureCombatant(message, ECombatantId.Player),
            Opponent = CaptureCombatant(message, ECombatantId.Opponent),
        };
    }

    private static bool IsCombatOpening(NetMessageGameSim message) =>
        message.Data.CurrentState?.StateName is ERunState.Combat or ERunState.PVPCombat;

    private static BazaarAgentBattleCombatantSnapshot CaptureCombatant(
        NetMessageGameSim message,
        ECombatantId owner
    )
    {
        var cards = new Dictionary<string, BazaarAgentBattleCardSnapshot>(StringComparer.Ordinal);
        var skills = new Dictionary<string, BazaarAgentBattleCardSnapshot>(StringComparer.Ordinal);
        var spawnedCards = message.Data.Events.OfType<GameSimEventCardSpawned>().ToArray();
        var spawnedCardByInstanceId = spawnedCards
            .GroupBy(evt => evt.InstanceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var equippedSkills = message
            .Data.Events.OfType<GameSimEventPlayerSkillEquipped>()
            .Where(evt => evt.Owner == owner)
            .ToArray();
        var opponentSkillTemplates =
            owner == ECombatantId.Opponent
                ? TryGetOpponentSkillTemplates(
                    message.Data.CurrentState?.CurrentEncounterId,
                    equippedSkills.Length
                )
                : Array.Empty<TCardInstanceSkill>();
        foreach (
            var spawned in spawnedCards.Where(evt =>
                evt.CombatantId == owner && evt.Section == EInventorySection.Hand
            )
        )
        {
            message.Data.Cards.TryGetValue(spawned.InstanceId, out var update);
            cards[spawned.InstanceId] = CreateCard(spawned, update);
        }

        for (var index = 0; index < equippedSkills.Length; index++)
        {
            var equipped = equippedSkills[index];
            if (skills.ContainsKey(equipped.InstanceId))
                continue;
            message.Data.Cards.TryGetValue(equipped.InstanceId, out var update);
            spawnedCardByInstanceId.TryGetValue(equipped.InstanceId, out var spawned);
            skills[equipped.InstanceId] = CreateCard(
                spawned,
                update,
                instanceId: equipped.InstanceId,
                fallbackType: "Skill",
                fallbackSkill: opponentSkillTemplates.ElementAtOrDefault(index)
            );
        }

        // The opening GameSim reliably carries the opponent's spawned cards, but the local board
        // and skills can already exist when its event arrives. Merge both live captures for the
        // player even when some item spawn events were present.
        if (owner == ECombatantId.Player)
        {
            foreach (var card in Data.GetCards<Card>(ECombatantId.Player, EInventorySection.Hand))
            {
                cards.TryAdd(card.InstanceId.ToString(), CreateLiveCardSnapshot(card));
            }

            foreach (var skill in Data.Run?.Player?.Skills?.Where(skill => skill is not null) ?? [])
            {
                skills[skill.InstanceId.ToString()] = CreateLiveCardSnapshot(skill);
            }
        }

        return new BazaarAgentBattleCombatantSnapshot
        {
            Board = cards
                .Values.OrderBy(card => card.SocketId)
                .ThenBy(card => card.InstanceId)
                .ToArray(),
            Skills = skills.Values.OrderBy(card => card.InstanceId).ToArray(),
        };
    }

    private static BazaarAgentBattleCardSnapshot CreateCard(
        GameSimEventCardSpawned spawned,
        SimUpdateCard? update
    ) => CreateCard(spawned, update, spawned.InstanceId, spawned.Type.ToString());

    private static BazaarAgentBattleCardSnapshot CreateCard(
        GameSimEventCardSpawned? spawned,
        SimUpdateCard? update,
        string instanceId,
        string fallbackType,
        TCardInstanceSkill? fallbackSkill = null
    )
    {
        var existing = TryGetExistingCard(instanceId);
        var templateId =
            spawned?.TemplateId
            ?? (
                existing?.TemplateId is Guid existingTemplateId && existingTemplateId != Guid.Empty
                    ? existingTemplateId.ToString()
                    : null
            )
            ?? (
                fallbackSkill is { TemplateId: var skillTemplateId }
                && skillTemplateId != Guid.Empty
                    ? skillTemplateId.ToString()
                    : null
            )
            ?? "";
        var type =
            spawned?.Type ?? existing?.Type ?? (fallbackSkill is null ? null : ECardType.Skill);

        // The opening GameSim is observed before the client has always materialised opponent
        // entities. Build an equivalent detached card from the native template and update data
        // instead of serialising that partial transport record. This preserves the same rendered
        // name and runtime-valued description used for the local board without mutating Data.
        if (existing?.Template is not TCardBase && type is not null)
        {
            var detached = TryCreateDetachedCard(
                instanceId,
                templateId,
                type.Value,
                update,
                fallbackSkill
            );
            if (detached is not null)
                existing = detached;
        }

        if (existing is not null)
            return CreateLiveCardSnapshot(existing);

        return new BazaarAgentBattleCardSnapshot
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            Type = fallbackType,
            Size = update?.Size?.ToString(),
            Enchantment = update?.Enchantment?.ToString(),
            Section = update?.Placement?.Section?.ToString() ?? spawned?.Section?.ToString(),
            SocketId = update?.Placement?.Socket?.ToString() ?? spawned?.Socket?.ToString(),
            Tags = update?.Tags?.Select(tag => tag.ToString()).ToArray() ?? Array.Empty<string>(),
        };
    }

    private static Card? TryCreateDetachedCard(
        string instanceId,
        string templateId,
        ECardType type,
        SimUpdateCard? update,
        TCardInstanceSkill? fallbackSkill
    )
    {
        if (string.IsNullOrWhiteSpace(templateId))
            return null;

        try
        {
            var card = DTOUtils.CreateCard(instanceId, templateId, type);
            if (update?.InstanceId == instanceId)
                card.Update(update);
            if (fallbackSkill is not null)
            {
                card.Tier = fallbackSkill.Tier;
                foreach (var attribute in fallbackSkill.Attributes ?? [])
                    card.Attributes[attribute.Key] = attribute.Value;
            }
            return card;
        }
        catch
        {
            return null;
        }
    }

    private static BazaarAgentBattleCardSnapshot CreateLiveCardSnapshot(Card card) =>
        new()
        {
            InstanceId = card.InstanceId.ToString(),
            TemplateId = card.TemplateId.ToString(),
            Type = card.Type.ToString(),
            DisplayName = CardDisplayNameResolver.Resolve(card.Template as TCardBase),
            Tier = card.Tier.ToString(),
            Size = card.Size.ToString(),
            Enchantment = (card as ItemCard)?.Enchantment?.ToString(),
            Section = card.Section?.ToString(),
            SocketId = card.LeftSocketId?.ToString(),
            Tags = card.Tags?.Select(tag => tag.ToString()).ToArray() ?? Array.Empty<string>(),
            HiddenTags =
                card.HiddenTags?.Select(tag => tag.ToString()).ToArray() ?? Array.Empty<string>(),
            Description = CardDescriptionResolver.Resolve(card),
            CooldownSeconds = ReadCooldownSeconds(card),
            Ammo = ReadAmmo(card),
            AmmoMax = ReadAmmoMax(card),
            SellPrice = card.GetAttributeValue(ECardAttributeType.SellPrice),
        };

    private static BazaarAgentBattleSummarySnapshot BuildSummary(
        BazaarAgentBattleSummarySnapshot opening,
        NetMessageCombatSim message,
        IReadOnlyDictionary<EPlayerAttributeType, int> openingPlayerAttributes,
        IReadOnlyDictionary<EPlayerAttributeType, int> openingOpponentAttributes
    ) =>
        new()
        {
            SummaryId = opening.SummaryId,
            Phase = "completed",
            BattleType = opening.BattleType,
            Result = message.Data.Winner switch
            {
                ECombatantId.Player => "win",
                ECombatantId.Opponent => "loss",
                _ => null,
            },
            Player = new BazaarAgentBattleCombatantSnapshot
            {
                Board = opening.Player.Board,
                Skills = opening.Player.Skills,
                Attributes = SummarizeAttributes(
                    message.Data.Frames,
                    static frame => frame.PlayerUpdates,
                    openingPlayerAttributes
                ),
            },
            Opponent = new BazaarAgentBattleCombatantSnapshot
            {
                Board = opening.Opponent.Board,
                Skills = opening.Opponent.Skills,
                Attributes = SummarizeAttributes(
                    message.Data.Frames,
                    static frame => frame.OpponentUpdates,
                    openingOpponentAttributes
                ),
            },
        };

    private static BazaarAgentBattleAttributesSnapshot SummarizeAttributes(
        IEnumerable<CombatSimFrame> frames,
        Func<CombatSimFrame, CombatSimPlayerUpdate?> selectUpdate,
        IReadOnlyDictionary<EPlayerAttributeType, int> openingValues
    )
    {
        var values = openingValues
            .Where(pair => IsCapturedAttribute(pair.Key))
            .ToDictionary(
                pair => pair.Key,
                pair => new BazaarAgentBattleValueChange { Start = pair.Value, End = pair.Value }
            );
        foreach (var update in frames.Select(selectUpdate).Where(update => update is not null))
        foreach (var attribute in update!.Attributes)
        {
            if (!IsCapturedAttribute(attribute.Key))
                continue;
            values.TryGetValue(attribute.Key, out var existing);
            values[attribute.Key] = new BazaarAgentBattleValueChange
            {
                Start = existing?.Start ?? attribute.Value.PreviousValue,
                End = attribute.Value.CurrentValue,
            };
        }

        return new BazaarAgentBattleAttributesSnapshot
        {
            Health = Value(EPlayerAttributeType.Health),
            MaxHealth = Value(EPlayerAttributeType.HealthMax),
            Shield = Value(EPlayerAttributeType.Shield),
            Burn = Value(EPlayerAttributeType.Burn),
            Poison = Value(EPlayerAttributeType.Poison),
        };

        BazaarAgentBattleValueChange? Value(EPlayerAttributeType type) =>
            values.TryGetValue(type, out var value) ? value : null;
    }

    private static bool IsCapturedAttribute(EPlayerAttributeType type) =>
        type
            is EPlayerAttributeType.Health
                or EPlayerAttributeType.HealthMax
                or EPlayerAttributeType.Shield
                or EPlayerAttributeType.Burn
                or EPlayerAttributeType.Poison;

    private static Card? TryGetExistingCard(string instanceId)
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

    private static IReadOnlyList<TCardInstanceSkill> TryGetOpponentSkillTemplates(
        string? encounterIdText,
        int equippedSkillCount
    )
    {
        if (equippedSkillCount == 0 || !Guid.TryParse(encounterIdText, out var encounterId))
            return Array.Empty<TCardInstanceSkill>();

        try
        {
            // GameSim carries the encounter's static template ID here, not an entity instance
            // ID. Looking it up through Data.GetCard would therefore always miss this early.
            if (
                Data.GetStatic().GetCardById(encounterId)
                is not TCardEncounterCombat { CombatantType: TCombatantMonster combatant }
            )
                return Array.Empty<TCardInstanceSkill>();

            var skills = Data.GetStatic()
                .GetMonsterById(combatant.MonsterTemplateId)
                ?.Player.Skills;
            return skills is { Count: var count } && count == equippedSkillCount
                ? skills.ToArray()
                : Array.Empty<TCardInstanceSkill>();
        }
        catch
        {
            // PvP does not have a local monster definition, and some state transitions can
            // arrive before their encounter card is materialised.
            return Array.Empty<TCardInstanceSkill>();
        }
    }

    private static double? ReadCooldownSeconds(Card card)
    {
        var milliseconds = card.GetAttributeValue(ECardAttributeType.CooldownMax);
        return milliseconds is > 0 ? milliseconds.Value / 1000d : null;
    }

    private static int? ReadAmmoMax(Card card)
    {
        var ammoMax = card.GetAttributeValue(ECardAttributeType.AmmoMax);
        return ammoMax is > 0 ? ammoMax : null;
    }

    private static int? ReadAmmo(Card card) =>
        ReadAmmoMax(card) is null ? null : card.GetAttributeValue(ECardAttributeType.Ammo) ?? 0;
}
