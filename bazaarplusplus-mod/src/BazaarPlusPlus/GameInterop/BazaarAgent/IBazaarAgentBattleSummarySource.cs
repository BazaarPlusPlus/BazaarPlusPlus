#nullable enable
namespace BazaarPlusPlus.GameInterop;

/// <summary>
/// Handoff for the two live-combat boundaries. The host publishes the opening lineup once when
/// combat begins, then retains the completed result until the first confirmed post-combat action.
/// </summary>
public interface IBazaarAgentBattleSummarySource
{
    BazaarAgentBattleSummarySnapshot? GetOpeningSummary();

    BazaarAgentBattleSummarySnapshot? GetCompletedSummary();

    void AcknowledgeCompletedSummary(string summaryId);
}

public sealed class BazaarAgentBattleSummarySnapshot
{
    public string SummaryId { get; init; } = "";
    public string Phase { get; init; } = "";
    public string BattleType { get; init; } = "unknown";
    public string? Result { get; init; }
    public BazaarAgentBattleCombatantSnapshot Player { get; init; } = new();
    public BazaarAgentBattleCombatantSnapshot Opponent { get; init; } = new();
}

public sealed class BazaarAgentBattleCombatantSnapshot
{
    public IReadOnlyList<BazaarAgentBattleCardSnapshot> Board { get; init; } =
        Array.Empty<BazaarAgentBattleCardSnapshot>();
    public IReadOnlyList<BazaarAgentBattleCardSnapshot> Skills { get; init; } =
        Array.Empty<BazaarAgentBattleCardSnapshot>();
    public BazaarAgentBattleAttributesSnapshot Attributes { get; init; } = new();
}

public sealed class BazaarAgentBattleCardSnapshot
{
    public string InstanceId { get; init; } = "";
    public string TemplateId { get; init; } = "";
    public string Type { get; init; } = "";
    public string? DisplayName { get; init; }
    public string? Tier { get; init; }
    public string? Size { get; init; }
    public string? Enchantment { get; init; }
    public string? Section { get; init; }
    public string? SocketId { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> HiddenTags { get; init; } = Array.Empty<string>();
    public string? Description { get; init; }
    public double? CooldownSeconds { get; init; }
    public int? Ammo { get; init; }
    public int? AmmoMax { get; init; }
    public int? SellPrice { get; init; }
}

/// <summary>Combat-only player attributes. Each pair is the value at combat start and end.</summary>
public sealed class BazaarAgentBattleAttributesSnapshot
{
    public BazaarAgentBattleValueChange? Health { get; init; }
    public BazaarAgentBattleValueChange? MaxHealth { get; init; }
    public BazaarAgentBattleValueChange? Shield { get; init; }
    public BazaarAgentBattleValueChange? Burn { get; init; }
    public BazaarAgentBattleValueChange? Poison { get; init; }
}

public sealed class BazaarAgentBattleValueChange
{
    public int Start { get; init; }
    public int End { get; init; }
    public int Delta => End - Start;
}
