#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.BazaarAgent;

public static class BazaarAgentSchema
{
    public const string Version = "2.2.0";
}

public enum BazaarAgentActionKind
{
    Wait,
    StartOrContinueRun,
    AbandonRun,
    SelectItem,
    SelectSkill,
    SelectEncounter,
    CommitToPedestal,
    MoveItem,
    SellItem,
    Reroll,
    ExitState,
    ReturnToMenu,
    Continue,
}

public enum BazaarAgentActionGroup
{
    Wait,
    Flow,
    Offer,
    Route,
    Pedestal,
    Move,
    Sell,
    Reroll,
    Exit,
}

public enum BazaarAgentRunStateName
{
    Unknown,
    StartRun,
    Choice,
    Encounter,
    Combat,
    PvpCombat,
    Replay,
    LevelUp,
    Loot,
    Pedestal,
    EndRunVictory,
    EndRunDefeat,
}

public enum BazaarAgentCardKind
{
    Item,
    Skill,
    Encounter,
    Unknown,
}

public enum BazaarAgentCardLocation
{
    Selection,
    Board,
    Chest,
    Skill,
    Unknown,
}

public enum BazaarAgentTargetSection
{
    Hand,
    Stash,
    Skill,
    Fuse,
}

/// <summary>Combat-replay playback phase, exposed so external scripts can tell when the replay
/// "continue" button is clickable (<see cref="FinishedAwaitingContinue"/>). Serialized camelCase
/// (<c>none</c>/<c>starting</c>/<c>playing</c>/<c>finishedAwaitingContinue</c>) per the wire
/// contract; the type-level converter overrides the PascalCase settings-level one.</summary>
[JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
public enum BazaarAgentReplayPhase
{
    None,
    Starting,
    Playing,
    FinishedAwaitingContinue,
}

public sealed class BazaarAgentCardSnapshot
{
    public string InstanceId { get; init; } = "";
    public BazaarAgentCardKind Kind { get; init; }
    public string? Type { get; init; }
    public string? TemplateId { get; init; }
    public string? DisplayName { get; init; }
    public string? Tier { get; init; }
    public string? Size { get; init; }
    public string? Enchantment { get; init; }
    public string? SocketId { get; init; }
    public BazaarAgentCardLocation Location { get; init; }
    public int Order { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = System.Array.Empty<string>();
    public IReadOnlyList<string> HiddenTags { get; init; } = System.Array.Empty<string>();
    public IReadOnlyDictionary<string, int> Attributes { get; init; } =
        new Dictionary<string, int>();
    public IReadOnlyList<BazaarAgentCardAbilitySnapshot> ActiveAbilities { get; init; } =
        System.Array.Empty<BazaarAgentCardAbilitySnapshot>();

    // Selection-only fields
    public int? BuyPrice { get; init; }
    public int? SellPrice { get; init; }
    public bool? CanAfford { get; init; }
    public bool? CanFit { get; init; }
    public bool? CanSelect { get; init; }
    public bool? IsFree { get; init; }
    public BazaarAgentTargetSection? TargetSection { get; init; }

    /// <summary>Comma-joined informational hint (e.g. "Socket_2,Socket_3") for human/UI display. Not the authoritative dispatch input — clients must POST <see cref="BazaarAgentAction.TargetSockets"/> sourced from <see cref="BazaarAgentDecisionOption.TargetSockets"/>.</summary>
    public string? TargetSockets { get; init; }
    public string? UnavailableReason { get; init; }

    // Owned (Board/Chest/Skill)
    public bool? CanSell { get; init; }
}

public sealed class BazaarAgentCardAbilitySnapshot
{
    public string Id { get; init; } = "";
    public string? InternalName { get; init; }
    public string? InternalDescription { get; init; }
    public string? Trigger { get; init; }
    public string? Action { get; init; }
    public string? ActiveIn { get; init; }
    public string? WorksIn { get; init; }
    public string? Priority { get; init; }
}

public sealed class BazaarAgentDecisionOption
{
    public BazaarAgentActionKind ActionKind { get; init; }
    public BazaarAgentActionGroup Group { get; init; }
    public string DisplayKey { get; init; } = "";
    public string? CardInstanceId { get; init; }
    public BazaarAgentTargetSection? TargetSection { get; init; }

    /// <summary>Structured socket list the validator and dispatcher use to match a POST body. Order-sensitive. Clients pick a triplet `(CardInstanceId, TargetSection, TargetSockets)` from this list verbatim.</summary>
    public IReadOnlyList<string>? TargetSockets { get; init; }
    public BazaarAgentCardSnapshot? Card { get; init; }
}

public sealed class BazaarAgentContext
{
    public string SchemaVersion { get; init; } = BazaarAgentSchema.Version;
    public ulong TickId { get; init; }
    public string ServerTimeUtc { get; init; } = "";

    public bool IsInRun { get; init; }
    public bool HasActiveRun { get; init; }
    public bool CanStartOrContinueRun { get; init; }
    public bool IsClientBusy { get; init; }

    public string? RunId { get; init; }
    public BazaarAgentRunStateName StateName { get; init; }
    public string? PlayerHero { get; init; }
    public int? Day { get; init; }
    public int? Hour { get; init; }
    public int? Wins { get; init; }
    public int? Losses { get; init; }
    public int PlayerGold { get; init; }
    public int? PlayerIncome { get; init; }
    public int? PlayerHealth { get; init; }
    public int? PlayerMaxHealth { get; init; }
    public int? PlayerPrestige { get; init; }
    public int? PlayerLevel { get; init; }
    public bool SelectionIsFree { get; init; }
    public bool CanExit { get; init; }
    public bool CanReroll { get; init; }
    public int RerollCost { get; init; }
    public int RerollsRemaining { get; init; }
    public string? CurrentEncounterId { get; init; }
    public string? CurrentEncounterType { get; init; }
    public double ActionCooldownRemainingSeconds { get; init; }

    /// <summary>Where combat-replay playback currently is; <c>finishedAwaitingContinue</c> means
    /// <c>POST /v1/replay/continue</c> will finalize the replay (and any recording).</summary>
    public BazaarAgentReplayPhase ReplayPhase { get; init; }

    /// <summary>Battle id of the active replay session, when one is active.</summary>
    public string? ReplayBattleId { get; init; }

    /// <summary>Template IDs the game currently restricts player clicks to (target-selection mode: upgrade, enchant). Null/empty means no filter is active. When non-empty, only owned board/chest item cards whose templateId is in this set accept a SelectItem POST; offer-based SelectItem actions are suppressed from <see cref="AvailableActions"/>.</summary>
    public IReadOnlyList<string>? InteractableTemplateIds { get; init; }

    public IReadOnlyList<BazaarAgentCardSnapshot> BoardItems { get; init; } =
        System.Array.Empty<BazaarAgentCardSnapshot>();
    public IReadOnlyList<BazaarAgentCardSnapshot> ChestItems { get; init; } =
        System.Array.Empty<BazaarAgentCardSnapshot>();
    public IReadOnlyList<BazaarAgentCardSnapshot> PlayerSkills { get; init; } =
        System.Array.Empty<BazaarAgentCardSnapshot>();
    public IReadOnlyList<BazaarAgentCardSnapshot> SellableItems { get; init; } =
        System.Array.Empty<BazaarAgentCardSnapshot>();
    public IReadOnlyList<BazaarAgentCardSnapshot> SelectionOptions { get; init; } =
        System.Array.Empty<BazaarAgentCardSnapshot>();
    public IReadOnlyList<BazaarAgentDecisionOption> AvailableActions { get; init; } =
        System.Array.Empty<BazaarAgentDecisionOption>();
}

public sealed class BazaarAgentAction
{
    public string? SchemaVersion { get; set; }
    public BazaarAgentActionKind ActionKind { get; set; }
    public string? CardInstanceId { get; set; }
    public BazaarAgentTargetSection? TargetSection { get; set; }
    public IReadOnlyList<string>? TargetSockets { get; set; }
    public string? Hero { get; set; }
    public string? PlayMode { get; set; }
    public string? Reason { get; set; }
    public ulong? ForTickId { get; set; }
}
