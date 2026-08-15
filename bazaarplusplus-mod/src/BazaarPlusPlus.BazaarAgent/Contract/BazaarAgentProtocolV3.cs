#nullable enable
using Newtonsoft.Json;

namespace BazaarPlusPlus.BazaarAgent;

/// <summary>The replacement Agent protocol. The wire shape is intentionally terse because it is LLM context.</summary>
public static class BazaarAgentProtocolV3
{
    public const string Version = "3";
    public const string SessionHeader = "X-Bazaar-Agent-Session";
    public const string RevisionHeader = "X-Bazaar-Agent-Revision";
}

public sealed class BazaarAgentV3Context
{
    [JsonProperty("revision")]
    public ulong Revision { get; init; }

    [JsonProperty("full", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsFull { get; init; }

    [JsonProperty("state", NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyDictionary<string, object?>? State { get; init; }

    [JsonProperty("board", NullValueHandling = NullValueHandling.Ignore)]
    public BazaarAgentV3CardChanges? Board { get; init; }

    [JsonProperty("chest", NullValueHandling = NullValueHandling.Ignore)]
    public BazaarAgentV3CardChanges? Chest { get; init; }

    [JsonProperty("skills", NullValueHandling = NullValueHandling.Ignore)]
    public BazaarAgentV3CardChanges? Skills { get; init; }

    // A present selection replaces the current offer row in full. Omission means its full
    // previously received row remains valid; an empty array explicitly clears that row.
    [JsonProperty("selection", NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<BazaarAgentV3Card>? Selection { get; init; }

    [JsonProperty("operations", NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<string>? Operations { get; init; }

    [JsonProperty("lockedBoardSlots", NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<int>? LockedBoardSlots { get; init; }

    [JsonProperty("battle", NullValueHandling = NullValueHandling.Ignore)]
    public BazaarAgentV3Battle? LastBattle { get; init; }

    [JsonProperty("battleCleared", NullValueHandling = NullValueHandling.Ignore)]
    public bool? BattleCleared { get; init; }
}

public sealed class BazaarAgentV3CardChanges
{
    [JsonProperty("upsert", NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<BazaarAgentV3Card>? Upsert { get; init; }

    [JsonProperty("remove", NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<string>? Remove { get; init; }
}

/// <summary>Compact cards carry actionable summary data; full mechanics live in the rendered description.</summary>
public sealed class BazaarAgentV3Card
{
    // Item / skill / encounter references are deliberately human-readable and are also the
    // values callers POST as action ids. Exactly one of these is populated for a selection card;
    // owned skills instead use Name as their delta key.
    [JsonProperty("item", NullValueHandling = NullValueHandling.Ignore)]
    public string? Item { get; init; }

    [JsonProperty("skill", NullValueHandling = NullValueHandling.Ignore)]
    public string? Skill { get; init; }

    [JsonProperty("encounter", NullValueHandling = NullValueHandling.Ignore)]
    public string? Encounter { get; init; }

    [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
    public string? Name { get; init; }

    // A delta carrying this flag replaces the cached card rather than patching it.
    [JsonProperty("replace", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsFull { get; init; }

    [JsonProperty("slots", NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<int>? Slots { get; init; }

    [JsonProperty("size", NullValueHandling = NullValueHandling.Ignore)]
    public string? Size { get; init; }

    [JsonProperty("tier", NullValueHandling = NullValueHandling.Ignore)]
    public string? Tier { get; init; }

    [JsonProperty("enchantment", NullValueHandling = NullValueHandling.Ignore)]
    public string? Enchantment { get; init; }

    [JsonProperty("tags", NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<string>? Tags { get; init; }

    [JsonProperty("hiddenTags", NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<string>? HiddenTags { get; init; }

    [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
    public string? Description { get; init; }

    [JsonProperty("cooldownSeconds", NullValueHandling = NullValueHandling.Ignore)]
    public double? CooldownSeconds { get; init; }

    [JsonProperty("ammo", NullValueHandling = NullValueHandling.Ignore)]
    public int? Ammo { get; init; }

    [JsonProperty("ammoMax", NullValueHandling = NullValueHandling.Ignore)]
    public int? AmmoMax { get; init; }

    [JsonProperty("buyPrice", NullValueHandling = NullValueHandling.Ignore)]
    public int? BuyPrice { get; init; }

    [JsonProperty("sellPrice", NullValueHandling = NullValueHandling.Ignore)]
    public int? SellPrice { get; init; }

    /// <summary>Static PvE monster lineup supplied with a combat-encounter offer.</summary>
    [JsonProperty("opponent", NullValueHandling = NullValueHandling.Ignore)]
    public BazaarAgentV3CombatOpponentPreview? Opponent { get; init; }
}

public sealed class BazaarAgentV3CombatOpponentPreview
{
    [JsonProperty("board")]
    public IReadOnlyList<BazaarAgentV3Card> Board { get; init; } =
        System.Array.Empty<BazaarAgentV3Card>();

    [JsonProperty("skills")]
    public IReadOnlyList<BazaarAgentV3Card> Skills { get; init; } =
        System.Array.Empty<BazaarAgentV3Card>();

    [JsonProperty("health", NullValueHandling = NullValueHandling.Ignore)]
    public int? Health { get; init; }

    [JsonProperty("maxHealth", NullValueHandling = NullValueHandling.Ignore)]
    public int? MaxHealth { get; init; }
}

/// <summary>Complete immutable lineups emitted only at a combat start or completion boundary.</summary>
public sealed class BazaarAgentV3Battle
{
    [JsonProperty("phase")]
    public string Phase { get; init; } = "";

    [JsonProperty("battleType")]
    public string BattleType { get; init; } = "unknown";

    [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
    public string? Result { get; init; }

    [JsonProperty("player")]
    public BazaarAgentV3BattleCombatant Player { get; init; } = new();

    [JsonProperty("opponent")]
    public BazaarAgentV3BattleCombatant Opponent { get; init; } = new();
}

public sealed class BazaarAgentV3BattleCombatant
{
    [JsonProperty("board")]
    public IReadOnlyList<BazaarAgentV3Card> Board { get; init; } =
        System.Array.Empty<BazaarAgentV3Card>();

    [JsonProperty("skills")]
    public IReadOnlyList<BazaarAgentV3Card> Skills { get; init; } =
        System.Array.Empty<BazaarAgentV3Card>();

    [JsonProperty("attributes", NullValueHandling = NullValueHandling.Ignore)]
    public BazaarAgentBattleAttributes? Attributes { get; init; }
}

public sealed class BazaarAgentV3ActionRequest
{
    [JsonProperty("op")]
    public string Operation { get; set; } = "";

    [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
    public string? Id { get; set; }

    [JsonProperty("target", NullValueHandling = NullValueHandling.Ignore)]
    public string? Target { get; set; }

    [JsonProperty("slot", NullValueHandling = NullValueHandling.Ignore)]
    public int? StartSlot { get; set; }

    [JsonProperty("hero", NullValueHandling = NullValueHandling.Ignore)]
    public string? Hero { get; set; }

    [JsonProperty("mode", NullValueHandling = NullValueHandling.Ignore)]
    public string? PlayMode { get; set; }

    [JsonProperty("revision")]
    public ulong Revision { get; set; }
}
