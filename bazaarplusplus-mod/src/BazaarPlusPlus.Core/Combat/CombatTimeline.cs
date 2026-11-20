#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Core.Combat;

// Combat facets copy native enum values to reproduce the game's set and sort order. Adapters map by name (ADR-0019).
// Host adapters also preserve native iteration order when copying timelines.

public sealed class CombatTimeline
{
    // The game's simulation begins with an empty frame.
    public List<CombatFrame> Frames { get; set; } = new() { new CombatFrame() };

    public Dictionary<string, Dictionary<CombatCardStat, int>> CardStats { get; set; } = new();
}

public sealed class CombatFrame
{
    public List<CombatEvent> Events { get; set; } = new();

    public CombatPlayerUpdate? PlayerUpdates { get; set; }

    public CombatPlayerUpdate? OpponentUpdates { get; set; }

    public Dictionary<string, CombatCardUpdate> CardUpdates { get; set; } = new();
}

public sealed class CombatPlayerUpdate
{
    public List<CombatHealthAdjustment> HealthAdjustments { get; set; } = new();

    public Dictionary<CombatPlayerAttribute, CombatPlayerAttributeUpdate> Attributes { get; set; } =
        new();
}

public sealed class CombatHealthAdjustment
{
    public CombatDamageType DamageType { get; set; }

    public CombatHealthChange AttributeChanged { get; set; }

    public int Amount { get; set; }

    public bool IsCrit { get; set; }

    public bool IsDamageReduced { get; set; }
}

public sealed class CombatPlayerAttributeUpdate
{
    public CombatPlayerAttribute AttributeType { get; set; }

    public int PreviousValue { get; set; }

    public int CurrentValue { get; set; }

    public int Delta => CurrentValue - PreviousValue;
}

public sealed class CombatCardUpdate
{
    public string CardInstanceId { get; set; } = string.Empty;

    public Dictionary<CombatCardAttribute, CombatCardAttributeUpdate> Attributes { get; set; } =
        new();
}

public sealed class CombatCardAttributeUpdate
{
    public CombatCardAttribute AttributeType { get; set; }

    public int PreviousValue { get; set; }

    public int CurrentValue { get; set; }

    public int Delta => CurrentValue - PreviousValue;
}

public abstract class CombatEvent { }

public sealed class CombatEffectExecuted : CombatEvent
{
    public string ExecutionContextId { get; set; } = string.Empty;

    public string EffectId { get; set; } = string.Empty;

    public CombatActionType ActionType { get; set; }

    public string? Source { get; set; }

    public string? TriggerSource { get; set; }

    public CombatTarget? Target { get; set; }
}

public sealed class CombatAuraExecuted : CombatEvent
{
    public string ExecutionContextId { get; set; } = string.Empty;

    public string EffectId { get; set; } = string.Empty;

    public string? Source { get; set; }

    public string? TriggerSource { get; set; }

    public HashSet<CombatTarget> AppliedTo { get; set; } = new();

    public HashSet<CombatTarget> RemovedFrom { get; set; } = new();
}

public sealed class CombatantDied : CombatEvent
{
    public Combatant CombatantId { get; set; }
}

public sealed class CombatCardEnchanted : CombatEvent
{
    public string InstanceId { get; set; } = string.Empty;

    public CardEnchantment? EnchantmentType { get; set; }

    public bool IsReverted { get; set; }
}

public sealed class CombatCardTransformed : CombatEvent
{
    public string OriginalInstanceId { get; set; } = string.Empty;

    // Transformation order must match the game's order.
    public List<string>? TransformedCardInstanceIds { get; set; }
}

public sealed class CombatCardTransformReverted : CombatEvent
{
    public string? OriginalInstanceId { get; set; }

    public List<string>? TransformedCardInstanceIds { get; set; }
}

// Some game builds omit this event, so attribution leaves those costs unbooked.
public sealed class CombatCardActionCostSpent : CombatEvent
{
    public string ExecutingCard { get; set; } = string.Empty;

    public CombatPlayerAttribute? PlayerAttributeSpent { get; set; }
}

// Use reference identity so distinct aura targets with equal values survive, as in the game.
public abstract class CombatTarget { }

public sealed class CombatCardTarget : CombatTarget
{
    public string Target { get; set; } = string.Empty;
}

public sealed class CombatPlayerTarget : CombatTarget
{
    public Combatant Target { get; set; }
}
