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
    private SnapshotWindow _window = SnapshotWindow.Empty;

    public BazaarAgentContextSnapshot? Current => Volatile.Read(ref _window).Current;

    /// <summary>
    /// Returns the next available context after a caller's acknowledged revision. Retaining one
    /// predecessor lets an agent observe both combat boundaries even when a short battle starts
    /// and ends between its polls.
    /// </summary>
    public BazaarAgentContextSnapshot? GetNextAfter(ulong revision)
    {
        var window = Volatile.Read(ref _window);
        return window.Previous is { } previous && previous.TickId > revision
            ? previous
            : window.Current;
    }

    public BazaarAgentContextSnapshot Publish(BazaarAgentContext candidate) =>
        Publish(candidate, out _);

    public BazaarAgentContextSnapshot Publish(
        BazaarAgentContext candidate,
        out bool isFirstSnapshot
    )
    {
        var window = Volatile.Read(ref _window);
        var current = window.Current;
        isFirstSnapshot = current is null;
        if (current is not null && EqualsIgnoreTimeAndTick(current.Context, candidate))
        {
            return current;
        }
        _tickId++;
        var stamped = CloneWithTickId(candidate, _tickId);
        var snap = new BazaarAgentContextSnapshot(stamped);
        Volatile.Write(ref _window, new SnapshotWindow(current, snap));
        return snap;
    }

    public void Reset()
    {
        _tickId = 0;
        Volatile.Write(ref _window, SnapshotWindow.Empty);
    }

    private sealed class SnapshotWindow
    {
        public static readonly SnapshotWindow Empty = new(null, null);

        public SnapshotWindow(
            BazaarAgentContextSnapshot? previous,
            BazaarAgentContextSnapshot? current
        )
        {
            Previous = previous;
            Current = current;
        }

        public BazaarAgentContextSnapshot? Previous { get; }
        public BazaarAgentContextSnapshot? Current { get; }
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
            GameModeId = src.GameModeId,
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
            LockedBoardSockets = src.LockedBoardSockets,
            PlayerSkills = src.PlayerSkills,
            SellableItems = src.SellableItems,
            SelectionOptions = src.SelectionOptions,
            AvailableActions = src.AvailableActions,
            LastBattle = src.LastBattle,
        };

    private static bool EqualsIgnoreTimeAndTick(BazaarAgentContext a, BazaarAgentContext b)
    {
        // Explicitly ignore: ServerTimeUtc, TickId, SchemaVersion (publisher-controlled)
        return a.IsInRun == b.IsInRun
            && a.HasActiveRun == b.HasActiveRun
            && a.CanStartOrContinueRun == b.CanStartOrContinueRun
            && a.IsClientBusy == b.IsClientBusy
            && a.RunId == b.RunId
            && a.GameModeId == b.GameModeId
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
            && a.ReplayPhase == b.ReplayPhase
            && a.ReplayBattleId == b.ReplayBattleId
            && SocketsEqual(a.InteractableTemplateIds, b.InteractableTemplateIds)
            && CardsEqual(a.BoardItems, b.BoardItems)
            && CardsEqual(a.ChestItems, b.ChestItems)
            && SocketsEqual(a.LockedBoardSockets, b.LockedBoardSockets)
            && CardsEqual(a.PlayerSkills, b.PlayerSkills)
            && CardsEqual(a.SellableItems, b.SellableItems)
            && CardsEqual(a.SelectionOptions, b.SelectionOptions)
            && OptionsEqual(a.AvailableActions, b.AvailableActions)
            && ReferenceEquals(a.LastBattle, b.LastBattle);
    }

    public static bool HasGameplayStateChanged(BazaarAgentContext before, BazaarAgentContext after)
    {
        if (before is null)
            throw new ArgumentNullException(nameof(before));
        if (after is null)
            throw new ArgumentNullException(nameof(after));

        // Busy/cooldown and AvailableActions are transport-facing signals. They can change solely
        // because an accepted command is in flight, so none of them proves the game applied it.
        return before.IsInRun != after.IsInRun
            || before.HasActiveRun != after.HasActiveRun
            || before.CanStartOrContinueRun != after.CanStartOrContinueRun
            || before.RunId != after.RunId
            || before.GameModeId != after.GameModeId
            || before.StateName != after.StateName
            || before.PlayerHero != after.PlayerHero
            || before.Day != after.Day
            || before.Hour != after.Hour
            || before.Wins != after.Wins
            || before.Losses != after.Losses
            || before.PlayerGold != after.PlayerGold
            || before.PlayerIncome != after.PlayerIncome
            || before.PlayerHealth != after.PlayerHealth
            || before.PlayerMaxHealth != after.PlayerMaxHealth
            || before.PlayerPrestige != after.PlayerPrestige
            || before.PlayerLevel != after.PlayerLevel
            || before.SelectionIsFree != after.SelectionIsFree
            || before.CanExit != after.CanExit
            || before.CanReroll != after.CanReroll
            || before.RerollCost != after.RerollCost
            || before.RerollsRemaining != after.RerollsRemaining
            || before.CurrentEncounterId != after.CurrentEncounterId
            || before.CurrentEncounterType != after.CurrentEncounterType
            || before.ReplayPhase != after.ReplayPhase
            || before.ReplayBattleId != after.ReplayBattleId
            || !SocketsEqual(before.InteractableTemplateIds, after.InteractableTemplateIds)
            || !CardsEqual(before.BoardItems, after.BoardItems)
            || !CardsEqual(before.ChestItems, after.ChestItems)
            || !SocketsEqual(before.LockedBoardSockets, after.LockedBoardSockets)
            || !CardsEqual(before.PlayerSkills, after.PlayerSkills)
            || !CardsEqual(before.SellableItems, after.SellableItems)
            || !CardsEqual(before.SelectionOptions, after.SelectionOptions)
            || !ReferenceEquals(before.LastBattle, after.LastBattle);
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
            && (a.Kind != BazaarAgentCardKind.Skill || a.DisplayName == b.DisplayName)
            && a.Tier == b.Tier
            && (a.Kind != BazaarAgentCardKind.Item || a.Size == b.Size)
            && (a.Kind != BazaarAgentCardKind.Item || a.Enchantment == b.Enchantment)
            && a.SocketId == b.SocketId
            && a.Location == b.Location
            && a.Order == b.Order
            && SocketsEqual(a.Tags, b.Tags)
            && SocketsEqual(a.HiddenTags, b.HiddenTags)
            && a.Description == b.Description
            && a.CooldownSeconds == b.CooldownSeconds
            && a.Ammo == b.Ammo
            && a.AmmoMax == b.AmmoMax
            && a.BuyPrice == b.BuyPrice
            && a.SellPrice == b.SellPrice
            && a.TargetSection == b.TargetSection
            && a.TargetSockets == b.TargetSockets
            && a.UnavailableReason == b.UnavailableReason
            && OpponentPreviewEqual(a.OpponentPreview, b.OpponentPreview);
    }

    private static bool OpponentPreviewEqual(
        BazaarAgentCombatOpponentPreview? a,
        BazaarAgentCombatOpponentPreview? b
    )
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a is null || b is null)
            return false;
        return a.Health == b.Health
            && a.MaxHealth == b.MaxHealth
            && CardsEqual(a.Board, b.Board)
            && CardsEqual(a.Skills, b.Skills);
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
}
