#nullable enable
namespace BazaarPlusPlus.BazaarAgent;

public sealed class BazaarAgentContextSnapshot
{
    public BazaarAgentContext Context { get; }
    public ulong TickId => Context.TickId;
    public string ETag { get; }

    public BazaarAgentContextSnapshot(BazaarAgentContext context)
    {
        Context = context;
        ETag =
            "\""
            + context.TickId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "\"";
    }
}

public sealed class BazaarAgentContextSnapshotPublisher
{
    private ulong _tickId;
    private BazaarAgentContextSnapshot? _current;

    public BazaarAgentContextSnapshot? Current => Volatile.Read(ref _current);

    public BazaarAgentContextSnapshot Publish(BazaarAgentContext candidate) =>
        Publish(candidate, out _);

    public BazaarAgentContextSnapshot Publish(
        BazaarAgentContext candidate,
        out bool isFirstSnapshot
    )
    {
        var current = _current;
        isFirstSnapshot = current is null;
        if (current is not null && EqualsIgnoreTimeAndTick(current.Context, candidate))
        {
            return current;
        }
        _tickId++;
        var stamped = CloneWithTickId(candidate, _tickId);
        var snap = new BazaarAgentContextSnapshot(stamped);
        Volatile.Write(ref _current, snap);
        return snap;
    }

    public void Reset()
    {
        _tickId = 0;
        Volatile.Write(ref _current, null);
    }

    private static BazaarAgentContext CloneWithTickId(BazaarAgentContext src, ulong tickId) =>
        new()
        {
            SchemaVersion = src.SchemaVersion,
            TickId = tickId,
            ServerTimeUtc = src.ServerTimeUtc,
            IsInRun = src.IsInRun,
            HasActiveRun = src.HasActiveRun,
            CanStartOrContinueRun = src.CanStartOrContinueRun,
            IsClientBusy = src.IsClientBusy,
            RunId = src.RunId,
            StateName = src.StateName,
            PlayerHero = src.PlayerHero,
            Day = src.Day,
            Hour = src.Hour,
            Wins = src.Wins,
            Losses = src.Losses,
            PlayerGold = src.PlayerGold,
            PlayerIncome = src.PlayerIncome,
            PlayerHealth = src.PlayerHealth,
            PlayerMaxHealth = src.PlayerMaxHealth,
            PlayerPrestige = src.PlayerPrestige,
            PlayerLevel = src.PlayerLevel,
            SelectionIsFree = src.SelectionIsFree,
            CanExit = src.CanExit,
            CanReroll = src.CanReroll,
            RerollCost = src.RerollCost,
            RerollsRemaining = src.RerollsRemaining,
            CurrentEncounterId = src.CurrentEncounterId,
            CurrentEncounterType = src.CurrentEncounterType,
            ActionCooldownRemainingSeconds = src.ActionCooldownRemainingSeconds,
            ReplayPhase = src.ReplayPhase,
            ReplayBattleId = src.ReplayBattleId,
            InteractableTemplateIds = src.InteractableTemplateIds,
            BoardItems = src.BoardItems,
            ChestItems = src.ChestItems,
            PlayerSkills = src.PlayerSkills,
            SellableItems = src.SellableItems,
            SelectionOptions = src.SelectionOptions,
            AvailableActions = src.AvailableActions,
        };

    private static bool EqualsIgnoreTimeAndTick(BazaarAgentContext a, BazaarAgentContext b)
    {
        // Explicitly ignore: ServerTimeUtc, TickId, SchemaVersion (publisher-controlled)
        return a.IsInRun == b.IsInRun
            && a.HasActiveRun == b.HasActiveRun
            && a.CanStartOrContinueRun == b.CanStartOrContinueRun
            && a.IsClientBusy == b.IsClientBusy
            && a.RunId == b.RunId
            && a.StateName == b.StateName
            && a.PlayerHero == b.PlayerHero
            && a.Day == b.Day
            && a.Hour == b.Hour
            && a.Wins == b.Wins
            && a.Losses == b.Losses
            && a.PlayerGold == b.PlayerGold
            && a.PlayerIncome == b.PlayerIncome
            && a.PlayerHealth == b.PlayerHealth
            && a.PlayerMaxHealth == b.PlayerMaxHealth
            && a.PlayerPrestige == b.PlayerPrestige
            && a.PlayerLevel == b.PlayerLevel
            && a.SelectionIsFree == b.SelectionIsFree
            && a.CanExit == b.CanExit
            && a.CanReroll == b.CanReroll
            && a.RerollCost == b.RerollCost
            && a.RerollsRemaining == b.RerollsRemaining
            && a.CurrentEncounterId == b.CurrentEncounterId
            && a.CurrentEncounterType == b.CurrentEncounterType
            && a.ActionCooldownRemainingSeconds == b.ActionCooldownRemainingSeconds
            && a.ReplayPhase == b.ReplayPhase
            && a.ReplayBattleId == b.ReplayBattleId
            && SocketsEqual(a.InteractableTemplateIds, b.InteractableTemplateIds)
            && CardsEqual(a.BoardItems, b.BoardItems)
            && CardsEqual(a.ChestItems, b.ChestItems)
            && CardsEqual(a.PlayerSkills, b.PlayerSkills)
            && CardsEqual(a.SellableItems, b.SellableItems)
            && CardsEqual(a.SelectionOptions, b.SelectionOptions)
            && OptionsEqual(a.AvailableActions, b.AvailableActions);
    }

    private static bool CardsEqual(
        IReadOnlyList<BazaarAgentCardSnapshot> a,
        IReadOnlyList<BazaarAgentCardSnapshot> b
    )
    {
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
            if (!CardEqual(a[i], b[i]))
                return false;
        return true;
    }

    private static bool CardEqual(BazaarAgentCardSnapshot a, BazaarAgentCardSnapshot b)
    {
        return a.InstanceId == b.InstanceId
            && a.Kind == b.Kind
            && a.Type == b.Type
            && a.TemplateId == b.TemplateId
            && a.DisplayName == b.DisplayName
            && a.Tier == b.Tier
            && a.Size == b.Size
            && a.Enchantment == b.Enchantment
            && a.SocketId == b.SocketId
            && a.Location == b.Location
            && a.Order == b.Order
            && SocketsEqual(a.Tags, b.Tags)
            && SocketsEqual(a.HiddenTags, b.HiddenTags)
            && AttributesEqual(a.Attributes, b.Attributes)
            && AbilitiesEqual(a.ActiveAbilities, b.ActiveAbilities)
            && a.BuyPrice == b.BuyPrice
            && a.SellPrice == b.SellPrice
            && a.CanAfford == b.CanAfford
            && a.CanFit == b.CanFit
            && a.CanSelect == b.CanSelect
            && a.IsFree == b.IsFree
            && a.TargetSection == b.TargetSection
            && a.TargetSockets == b.TargetSockets
            && a.UnavailableReason == b.UnavailableReason
            && a.CanSell == b.CanSell;
    }

    private static bool OptionsEqual(
        IReadOnlyList<BazaarAgentDecisionOption> a,
        IReadOnlyList<BazaarAgentDecisionOption> b
    )
    {
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            if (x.ActionKind != y.ActionKind)
                return false;
            if (x.Group != y.Group)
                return false;
            if (x.DisplayKey != y.DisplayKey)
                return false;
            if (x.CardInstanceId != y.CardInstanceId)
                return false;
            if (x.TargetSection != y.TargetSection)
                return false;
            if (!SocketsEqual(x.TargetSockets, y.TargetSockets))
                return false;
            if (x.Card is null != y.Card is null)
                return false;
            if (x.Card is not null && y.Card is not null && !CardEqual(x.Card, y.Card))
                return false;
        }
        return true;
    }

    private static bool SocketsEqual(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a is null || b is null)
            return false;
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i] != b[i])
                return false;
        return true;
    }

    private static bool AttributesEqual(
        IReadOnlyDictionary<string, int> a,
        IReadOnlyDictionary<string, int> b
    )
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a.Count != b.Count)
            return false;
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var value) || value != kv.Value)
                return false;
        }
        return true;
    }

    private static bool AbilitiesEqual(
        IReadOnlyList<BazaarAgentCardAbilitySnapshot> a,
        IReadOnlyList<BazaarAgentCardAbilitySnapshot> b
    )
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            if (x.Id != y.Id)
                return false;
            if (x.InternalName != y.InternalName)
                return false;
            if (x.InternalDescription != y.InternalDescription)
                return false;
            if (x.Trigger != y.Trigger)
                return false;
            if (x.Action != y.Action)
                return false;
            if (x.ActiveIn != y.ActiveIn)
                return false;
            if (x.WorksIn != y.WorksIn)
                return false;
            if (x.Priority != y.Priority)
                return false;
        }
        return true;
    }
}
