#nullable enable
using System.Globalization;

namespace BazaarPlusPlus.BazaarAgent;

/// <summary>
/// Maintains the small amount of server-side state needed to turn immutable game snapshots into
/// an Agent-applicable full snapshot or delta. Short IDs are scoped to this projector session.
/// </summary>
public sealed class BazaarAgentProtocolV3Projector
{
    private const int MaximumSessions = 16;
    private readonly object _gate = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);

    public BazaarAgentV3Projection Bootstrap(BazaarAgentContextSnapshot snapshot)
    {
        if (snapshot is null)
            throw new ArgumentNullException(nameof(snapshot));

        lock (_gate)
        {
            var sessionId = BazaarAgentUlid.New();
            var session = CreateSession(sessionId);
            session.LastAccessUtc = DateTime.UtcNow;
            var view = BuildView(snapshot.Context, session, full: true);
            session.LastContext = snapshot.Context;
            session.LastRevision = snapshot.TickId;
            return new BazaarAgentV3Projection(view, sessionId);
        }
    }

    public bool TryProjectDelta(
        BazaarAgentContextSnapshot snapshot,
        string? requestedSessionId,
        ulong revision,
        out BazaarAgentV3Projection projection
    )
    {
        if (snapshot is null)
            throw new ArgumentNullException(nameof(snapshot));

        lock (_gate)
        {
            if (
                string.IsNullOrWhiteSpace(requestedSessionId)
                || !_sessions.TryGetValue(requestedSessionId.Trim(), out var session)
                || session.LastRevision != revision
            )
            {
                projection = default;
                return false;
            }
            session.LastAccessUtc = DateTime.UtcNow;
            var view = BuildView(snapshot.Context, session, full: false);
            session.LastContext = snapshot.Context;
            session.LastRevision = snapshot.TickId;
            projection = new BazaarAgentV3Projection(view, requestedSessionId.Trim());
            return true;
        }
    }

    public bool IsCurrentSession(string? requestedSessionId, ulong revision, ulong currentRevision)
    {
        lock (_gate)
        {
            return !string.IsNullOrWhiteSpace(requestedSessionId)
                && _sessions.TryGetValue(requestedSessionId.Trim(), out var session)
                && session.LastRevision == revision
                && currentRevision == revision;
        }
    }

    public bool TryResolveInstanceId(
        string? requestedSessionId,
        string token,
        out string instanceId
    )
    {
        lock (_gate)
        {
            if (
                !string.IsNullOrWhiteSpace(requestedSessionId)
                && _sessions.TryGetValue(requestedSessionId.Trim(), out var session)
                && session.InstanceByToken.TryGetValue(token, out instanceId!)
            )
            {
                session.LastAccessUtc = DateTime.UtcNow;
                return true;
            }
            instanceId = "";
            return false;
        }
    }

    private Session CreateSession(string sessionId)
    {
        if (_sessions.Count >= MaximumSessions)
        {
            var oldest = _sessions.OrderBy(static entry => entry.Value.LastAccessUtc).First();
            _sessions.Remove(oldest.Key);
        }
        var created = new Session();
        _sessions.Add(sessionId, created);
        return created;
    }

    private static BazaarAgentV3Context BuildView(
        BazaarAgentContext current,
        Session session,
        bool full
    )
    {
        var previous = session.LastContext;
        var previouslyObservedCards = full || previous is null ? null : IndexCards(previous);
        return new BazaarAgentV3Context
        {
            Revision = current.TickId,
            IsFull = full ? true : null,
            State = BuildState(current, full ? null : previous),
            Board = BuildChanges(
                current.BoardItems,
                full ? null : previous!.BoardItems,
                previouslyObservedCards,
                session,
                full
            ),
            Chest = BuildChanges(
                current.ChestItems,
                full ? null : previous!.ChestItems,
                previouslyObservedCards,
                session,
                full
            ),
            Skills = BuildChanges(
                current.PlayerSkills,
                full ? null : previous!.PlayerSkills,
                previouslyObservedCards,
                session,
                full
            ),
            Selection = SelectionChanged(current, previous, full)
                ? current.SelectionOptions.Select(card => ProjectCard(card, session)).ToArray()
                : null,
            Operations = OperationsChanged(current, previous, full)
                ? BuildOperations(current)
                : null,
            LockedBoardSlots = LocksChanged(current, previous, full)
                ? ToSlotIndexes(current.LockedBoardSockets)
                : null,
            LastBattle = ProjectChangedBattle(current, previous, session, full),
            BattleCleared = ConsumeBattleCleared(current, session, full),
        };
    }

    private static IReadOnlyDictionary<string, object?>? BuildState(
        BazaarAgentContext current,
        BazaarAgentContext? previous
    )
    {
        var now = StateValues(current);
        if (previous is null)
            return now;
        var before = StateValues(previous);
        var changed = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in now)
            if (!before.TryGetValue(pair.Key, out var old) || !Equals(pair.Value, old))
                changed.Add(pair.Key, pair.Value);
        return changed.Count == 0 ? null : changed;
    }

    private static Dictionary<string, object?> StateValues(BazaarAgentContext context) =>
        BuildStateValues(context);

    private static Dictionary<string, object?> BuildStateValues(BazaarAgentContext context)
    {
        var state = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["state"] = context.StateName.ToString(),
            ["busy"] = context.IsClientBusy,
            ["hero"] = context.PlayerHero,
            ["day"] = context.Day,
            ["hour"] = context.Hour,
            ["wins"] = context.Wins,
            ["losses"] = context.Losses,
            ["gold"] = context.PlayerGold,
            ["income"] = context.PlayerIncome,
            ["health"] = context.PlayerHealth,
            ["maxHealth"] = context.PlayerMaxHealth,
            ["prestige"] = context.PlayerPrestige,
            ["level"] = context.PlayerLevel,
            ["rerolls"] = context.RerollsRemaining,
            ["rerollCost"] = context.RerollCost,
        };
        if (!string.IsNullOrWhiteSpace(context.CurrentEncounterType))
            state.Add("encounterType", context.CurrentEncounterType);
        // Replay remains available to the recording workflow, but its ordinary None state has no
        // decision value. Leaving Replay also changes state, so callers need not cache a None.
        if (context.ReplayPhase != BazaarAgentReplayPhase.None)
            state.Add("replay", context.ReplayPhase.ToString());
        return state;
    }

    private static BazaarAgentV3Battle? ProjectChangedBattle(
        BazaarAgentContext current,
        BazaarAgentContext? previous,
        Session session,
        bool full
    )
    {
        if (current.LastBattle is null)
            return null;
        if (
            !full
            && previous is not null
            && ReferenceEquals(current.LastBattle, previous.LastBattle)
        )
            return null;

        // A normal PvE choice response already included the selected monster's exact native
        // right-click lineup. Sending that same opening snapshot on the next combat tick only
        // wastes context. A bootstrap during combat still gets the full opening boundary because
        // it has no preceding choice cache.
        if (CanReusePveOpponentPreview(current, previous, full))
            return null;

        var projected = ProjectBattle(current.LastBattle, session);
        session.HasPublishedBattle = projected is not null;
        return projected;
    }

    private static bool? ConsumeBattleCleared(
        BazaarAgentContext current,
        Session session,
        bool full
    )
    {
        if (full || current.LastBattle is not null || !session.HasPublishedBattle)
            return null;
        session.HasPublishedBattle = false;
        return true;
    }

    private static bool CanReusePveOpponentPreview(
        BazaarAgentContext current,
        BazaarAgentContext? previous,
        bool full
    )
    {
        if (
            full
            || previous is null
            || current.LastBattle is not { Phase: "starting", BattleType: "pve" }
            || !Guid.TryParse(current.CurrentEncounterId, out var encounterTemplateId)
        )
            return false;

        return previous.SelectionOptions.Any(card =>
            card.Kind == BazaarAgentCardKind.Encounter
            && card.OpponentPreview is not null
            && Guid.TryParse(card.TemplateId, out var templateId)
            && templateId == encounterTemplateId
        );
    }

    private static BazaarAgentV3CardChanges? BuildChanges(
        IReadOnlyList<BazaarAgentCardSnapshot> current,
        IReadOnlyList<BazaarAgentCardSnapshot>? previous,
        IReadOnlyDictionary<string, BazaarAgentCardSnapshot>? previouslyObservedCards,
        Session session,
        bool full
    )
    {
        var before = previous?.ToDictionary(
            card => GetGroupKey(session, card),
            StringComparer.Ordinal
        );
        var upsert = new List<BazaarAgentV3Card>();
        foreach (var card in current)
        {
            if (full || before is null)
                upsert.Add(ProjectCard(card, session));
            else if (!before.TryGetValue(GetGroupKey(session, card), out var old))
            {
                // Membership changes (including board/chest relocation and a selection becoming
                // owned) retain the card's prior context as their baseline.
                if (
                    previouslyObservedCards is not null
                    && previouslyObservedCards.TryGetValue(card.InstanceId, out var previousCard)
                )
                    upsert.Add(ProjectCardDelta(card, previousCard, session));
                else
                    upsert.Add(ProjectCard(card, session));
            }
            else if (!CardEqual(card, old))
                upsert.Add(ProjectCardDelta(card, old, session));
        }

        List<string>? remove = null;
        if (!full && previous is not null)
        {
            var currentIds = current
                .Select(card => GetGroupKey(session, card))
                .ToHashSet(StringComparer.Ordinal);
            remove = previous
                .Where(card => !currentIds.Contains(GetGroupKey(session, card)))
                .Select(card => GetGroupKey(session, card))
                .ToList();
        }
        return upsert.Count == 0 && (remove is null || remove.Count == 0)
            ? null
            : new BazaarAgentV3CardChanges
            {
                Upsert = upsert.Count == 0 ? null : upsert,
                Remove = remove is { Count: > 0 } ? remove : null,
            };
    }

    private static BazaarAgentV3Card ProjectCard(
        BazaarAgentCardSnapshot card,
        Session session,
        bool replace = false
    ) =>
        new()
        {
            Item = card.Kind == BazaarAgentCardKind.Item ? GetActionReference(session, card) : null,
            Skill =
                card.Kind == BazaarAgentCardKind.Skill
                && card.Location == BazaarAgentCardLocation.Selection
                    ? GetActionReference(session, card)
                    : null,
            Encounter =
                card.Kind == BazaarAgentCardKind.Encounter
                    ? GetActionReference(session, card)
                    : null,
            Name =
                card.Kind == BazaarAgentCardKind.Skill
                && card.Location != BazaarAgentCardLocation.Selection
                    ? SkillName(card)
                    : null,
            IsFull = replace ? true : null,
            Slots = card.Kind == BazaarAgentCardKind.Item ? Slots(card) : null,
            Size = card.Kind == BazaarAgentCardKind.Item ? card.Size : null,
            Tier = card.Kind != BazaarAgentCardKind.Encounter ? card.Tier : null,
            Enchantment = card.Kind == BazaarAgentCardKind.Item ? card.Enchantment : null,
            Tags = card.Kind == BazaarAgentCardKind.Item ? card.Tags : null,
            HiddenTags = card.Kind != BazaarAgentCardKind.Encounter ? card.HiddenTags : null,
            Description = card.Description,
            CooldownSeconds =
                card.Kind != BazaarAgentCardKind.Encounter ? card.CooldownSeconds : null,
            Ammo = card.Kind != BazaarAgentCardKind.Encounter ? card.Ammo : null,
            AmmoMax = card.Kind != BazaarAgentCardKind.Encounter ? card.AmmoMax : null,
            BuyPrice = card.Location == BazaarAgentCardLocation.Selection ? card.BuyPrice : null,
            SellPrice = card.Kind == BazaarAgentCardKind.Item ? card.SellPrice : null,
            Opponent = ProjectOpponent(card.OpponentPreview, session),
        };

    private static BazaarAgentV3CombatOpponentPreview? ProjectOpponent(
        BazaarAgentCombatOpponentPreview? opponent,
        Session session
    ) =>
        opponent is null
            ? null
            : new BazaarAgentV3CombatOpponentPreview
            {
                Board = opponent.Board.Select(card => ProjectCard(card, session)).ToArray(),
                Skills = opponent.Skills.Select(card => ProjectCard(card, session)).ToArray(),
                Health = opponent.Health,
                MaxHealth = opponent.MaxHealth,
            };

    private static BazaarAgentV3Card ProjectCardDelta(
        BazaarAgentCardSnapshot current,
        BazaarAgentCardSnapshot previous,
        Session session
    )
    {
        if (RequiresFullReplacement(current, previous))
            return ProjectCard(current, session, replace: true);
        return new BazaarAgentV3Card
        {
            Item =
                current.Kind == BazaarAgentCardKind.Item
                    ? GetActionReference(session, current)
                    : null,
            Name = current.Kind == BazaarAgentCardKind.Skill ? SkillName(current) : null,
            Slots =
                current.Kind == BazaarAgentCardKind.Item
                && SequenceEqual(Slots(current), Slots(previous))
                    ? null
                    : Slots(current),
            Size =
                current.Kind == BazaarAgentCardKind.Item && current.Size != previous.Size
                    ? current.Size
                    : null,
            Tier = current.Tier != previous.Tier ? current.Tier : null,
            Enchantment =
                current.Kind == BazaarAgentCardKind.Item
                && current.Enchantment != previous.Enchantment
                    ? current.Enchantment
                    : null,
            Tags =
                current.Kind == BazaarAgentCardKind.Item
                && !SequenceEqual(current.Tags, previous.Tags)
                    ? current.Tags
                    : null,
            HiddenTags = SequenceEqual(current.HiddenTags, previous.HiddenTags)
                ? null
                : current.HiddenTags,
            Description = current.Description != previous.Description ? current.Description : null,
            CooldownSeconds =
                current.CooldownSeconds != previous.CooldownSeconds
                    ? current.CooldownSeconds
                    : null,
            Ammo = current.Ammo != previous.Ammo ? current.Ammo : null,
            AmmoMax = current.AmmoMax != previous.AmmoMax ? current.AmmoMax : null,
            BuyPrice = null,
            SellPrice =
                current.Kind == BazaarAgentCardKind.Item && current.SellPrice != previous.SellPrice
                    ? current.SellPrice
                    : null,
        };
    }

    private static bool RequiresFullReplacement(
        BazaarAgentCardSnapshot current,
        BazaarAgentCardSnapshot previous
    ) =>
        (
            current.Kind == BazaarAgentCardKind.Item
            && Slots(current) is null
            && Slots(previous) is not null
        )
        || (
            current.Kind == BazaarAgentCardKind.Item
            && current.Size is null
            && previous.Size is not null
        )
        || (current.Tier is null && previous.Tier is not null)
        || (
            current.Kind == BazaarAgentCardKind.Item
            && current.Enchantment is null
            && previous.Enchantment is not null
        )
        || (
            current.Kind == BazaarAgentCardKind.Item
            && current.Tags is null
            && previous.Tags is not null
        )
        || (current.HiddenTags is null && previous.HiddenTags is not null)
        || (current.Description is null && previous.Description is not null)
        || (current.CooldownSeconds is null && previous.CooldownSeconds is not null)
        || (current.Ammo is null && previous.Ammo is not null)
        || (current.AmmoMax is null && previous.AmmoMax is not null)
        || (current.SellPrice is null && previous.SellPrice is not null);

    private static IReadOnlyList<int>? Slots(BazaarAgentCardSnapshot card)
    {
        if (
            card.Location is not BazaarAgentCardLocation.Board and not BazaarAgentCardLocation.Chest
        )
            return null;
        if (!TryParseSlot(card.SocketId, out var start))
            return null;
        var size = BazaarAgentCardSize.Parse(card.Size, fallback: 1);
        return Enumerable.Range(start, size).ToArray();
    }

    private static IReadOnlyList<string> BuildOperations(BazaarAgentContext context) =>
        context
            .AvailableActions.Select(static action =>
                action.ActionKind switch
                {
                    BazaarAgentActionKind.StartOrContinueRun => "start",
                    BazaarAgentActionKind.SelectItem => "select",
                    BazaarAgentActionKind.SelectSkill => "select",
                    BazaarAgentActionKind.SelectEncounter => "select",
                    BazaarAgentActionKind.CommitToPedestal => "select",
                    BazaarAgentActionKind.MoveItem => "move",
                    BazaarAgentActionKind.SellItem => "sell",
                    BazaarAgentActionKind.Reroll => "reroll",
                    BazaarAgentActionKind.ExitState => "exit",
                    BazaarAgentActionKind.ReturnToMenu => "menu",
                    BazaarAgentActionKind.Continue => "continue",
                    _ => "",
                }
            )
            .Where(static operation => operation.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static operation => operation, StringComparer.Ordinal)
            .ToArray();

    private static BazaarAgentV3Battle? ProjectBattle(
        BazaarAgentBattleSummary? battle,
        Session session
    )
    {
        if (battle is null)
            return null;

        BazaarAgentV3BattleCombatant ProjectCombatant(BazaarAgentBattleCombatant combatant) =>
            new()
            {
                Board = combatant.Board.Select(card => ProjectCard(card, session)).ToArray(),
                Skills = combatant.Skills.Select(card => ProjectCard(card, session)).ToArray(),
                Attributes = HasValues(combatant.Attributes) ? combatant.Attributes : null,
            };

        return new BazaarAgentV3Battle
        {
            Phase = battle.Phase,
            BattleType = battle.BattleType,
            Result = battle.Result,
            Player = ProjectCombatant(battle.Player),
            Opponent = ProjectCombatant(battle.Opponent),
        };
    }

    private static bool HasValues(BazaarAgentBattleAttributes attributes) =>
        attributes.Health is not null
        || attributes.MaxHealth is not null
        || attributes.Shield is not null
        || attributes.Burn is not null
        || attributes.Poison is not null;

    private static IReadOnlyDictionary<string, BazaarAgentCardSnapshot> IndexCards(
        BazaarAgentContext context
    )
    {
        var indexed = new Dictionary<string, BazaarAgentCardSnapshot>(StringComparer.Ordinal);
        foreach (
            var card in context
                .BoardItems.Concat(context.ChestItems)
                .Concat(context.PlayerSkills)
                .Concat(context.SelectionOptions)
        )
        {
            // Native purchase animations can expose an owned card and its outgoing selection
            // together. The already-owned card is the authoritative delta baseline.
            if (!indexed.ContainsKey(card.InstanceId))
                indexed.Add(card.InstanceId, card);
        }
        return indexed;
    }

    private static bool OperationsChanged(
        BazaarAgentContext current,
        BazaarAgentContext? previous,
        bool full
    ) =>
        full
        || previous is null
        || !SequenceEqual(BuildOperations(current), BuildOperations(previous));

    private static bool LocksChanged(
        BazaarAgentContext current,
        BazaarAgentContext? previous,
        bool full
    ) =>
        full
        || previous is null
        || !SequenceEqual(current.LockedBoardSockets, previous.LockedBoardSockets);

    private static bool SelectionChanged(
        BazaarAgentContext current,
        BazaarAgentContext? previous,
        bool full
    )
    {
        if (full || previous is null)
            return true;
        if (current.SelectionOptions.Count == 0 && previous.SelectionOptions.Count == 0)
            return false;
        if (
            current.StateName != previous.StateName
            || current.Day != previous.Day
            || current.Hour != previous.Hour
            || current.CurrentEncounterType != previous.CurrentEncounterType
        )
            return true;
        if (current.SelectionOptions.Count != previous.SelectionOptions.Count)
            return true;
        for (var index = 0; index < current.SelectionOptions.Count; index++)
            if (
                current.SelectionOptions[index].InstanceId
                    != previous.SelectionOptions[index].InstanceId
                || !CardEqual(current.SelectionOptions[index], previous.SelectionOptions[index])
            )
                return true;
        return false;
    }

    private static IReadOnlyList<int> ToSlotIndexes(IReadOnlyList<string> sockets) =>
        sockets
            .Where(static socket => TryParseSlot(socket, out _))
            .Select(static socket =>
            {
                _ = TryParseSlot(socket, out var slot);
                return slot;
            })
            .ToArray();

    private static string GetGroupKey(Session session, BazaarAgentCardSnapshot card) =>
        card.Kind == BazaarAgentCardKind.Skill
            ? SkillName(card)
            : GetActionReference(session, card);

    private static string SkillName(BazaarAgentCardSnapshot card) =>
        string.IsNullOrWhiteSpace(card.DisplayName) ? "Unknown skill" : card.DisplayName;

    private static string GetActionReference(Session session, BazaarAgentCardSnapshot card)
    {
        if (session.ReferenceByInstance.TryGetValue(card.InstanceId, out var reference))
            return reference;
        var name = string.IsNullOrWhiteSpace(card.DisplayName) ? "Unknown" : card.DisplayName;
        reference =
            name
            + "#"
            + (++session.NextInstanceReference).ToString("D5", CultureInfo.InvariantCulture);
        session.ReferenceByInstance.Add(card.InstanceId, reference);
        session.InstanceByToken.Add(reference, card.InstanceId);
        return reference;
    }

    private static bool CardEqual(BazaarAgentCardSnapshot left, BazaarAgentCardSnapshot right) =>
        left.Kind == right.Kind
        && (left.Kind != BazaarAgentCardKind.Skill || left.DisplayName == right.DisplayName)
        && left.Tier == right.Tier
        && (left.Kind != BazaarAgentCardKind.Item || left.Size == right.Size)
        && (left.Kind != BazaarAgentCardKind.Item || left.Enchantment == right.Enchantment)
        && left.SocketId == right.SocketId
        && left.Location == right.Location
        && SequenceEqual(left.Tags, right.Tags)
        && SequenceEqual(left.HiddenTags, right.HiddenTags)
        && left.Description == right.Description
        && left.CooldownSeconds == right.CooldownSeconds
        && left.Ammo == right.Ammo
        && left.AmmoMax == right.AmmoMax
        && left.BuyPrice == right.BuyPrice
        && left.SellPrice == right.SellPrice;

    private static bool SequenceEqual<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;
        for (var index = 0; index < left.Count; index++)
            if (!EqualityComparer<T>.Default.Equals(left[index], right[index]))
                return false;
        return true;
    }

    private static bool TryParseSlot(string? socket, out int slot)
    {
        slot = -1;
        const string prefix = "Socket_";
        return socket is not null
            && socket.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(
                socket.AsSpan(prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out slot
            )
            && slot >= 0;
    }

    private sealed class Session
    {
        internal BazaarAgentContext? LastContext { get; set; }
        internal ulong LastRevision { get; set; }
        internal DateTime LastAccessUtc { get; set; } = DateTime.UtcNow;
        internal int NextInstanceReference { get; set; }
        internal Dictionary<string, string> ReferenceByInstance { get; } =
            new(StringComparer.Ordinal);
        internal Dictionary<string, string> InstanceByToken { get; } = new(StringComparer.Ordinal);
        internal bool HasPublishedBattle { get; set; }
    }
}

public readonly record struct BazaarAgentV3Projection(BazaarAgentV3Context View, string SessionId);
