#nullable enable
namespace BazaarPlusPlus.BazaarAgent;

/// <summary>A complete live-combat boundary: either its opening lineups or its completed result.</summary>
public sealed class BazaarAgentBattleSummary
{
    public string SummaryId { get; init; } = "";
    public string Phase { get; init; } = "";
    public string BattleType { get; init; } = "unknown";
    public string? Result { get; init; }
    public BazaarAgentBattleCombatant Player { get; init; } = new();
    public BazaarAgentBattleCombatant Opponent { get; init; } = new();
}

public sealed class BazaarAgentBattleCombatant
{
    public IReadOnlyList<BazaarAgentCardSnapshot> Board { get; init; } =
        System.Array.Empty<BazaarAgentCardSnapshot>();
    public IReadOnlyList<BazaarAgentCardSnapshot> Skills { get; init; } =
        System.Array.Empty<BazaarAgentCardSnapshot>();
    public BazaarAgentBattleAttributes Attributes { get; init; } = new();
}

public sealed class BazaarAgentBattleAttributes
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
