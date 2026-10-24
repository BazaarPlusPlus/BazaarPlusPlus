#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Core.Combat;

// BPP's combat timeline (ADR-0019): the facts of one finished combat that the Combat Impact
// producer reads, in BPP vocabulary. Each host adapter fills it from its own game build's
// simulation, in the game's iteration order, so set and dictionary order match the game's.
// The shape mirrors the game's simulation DTOs member for member so attribution reads the same
// facts by the same names; the game fields no attribution reads are left out.

/// <summary>A finished combat: its frames in order and the per-card totals the game reports.</summary>
public sealed class CombatTimeline
{
    /// <summary>Starts with one empty frame, as the game's simulation does.</summary>
    public List<CombatFrame> Frames { get; set; } = new() { new CombatFrame() };

    public Dictionary<string, Dictionary<CombatCardStat, int>> CardStats { get; set; } = new();
}

/// <summary>One tick: the events it ran and the player and card changes they produced.</summary>
public sealed class CombatFrame
{
    public List<CombatEvent> Events { get; set; } = new();

    public CombatPlayerUpdate? PlayerUpdates { get; set; }

    public CombatPlayerUpdate? OpponentUpdates { get; set; }

    /// <summary>Card changes by card instance id.</summary>
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

/// <summary>An event a frame ran. An adapter leaves out the game events attribution never reads.</summary>
public abstract class CombatEvent { }

/// <summary>An effect that ran an action on a target.</summary>
public sealed class CombatEffectExecuted : CombatEvent
{
    public string ExecutionContextId { get; set; } = string.Empty;

    public string EffectId { get; set; } = string.Empty;

    public CombatActionType ActionType { get; set; }

    /// <summary>The card instance whose effect ran.</summary>
    public string? Source { get; set; }

    /// <summary>The card instance whose trigger ran the effect.</summary>
    public string? TriggerSource { get; set; }

    public CombatTarget? Target { get; set; }
}

/// <summary>An aura applied to or removed from its targets.</summary>
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

    /// <summary>The instance ids of the cards the original became, in the game's order.</summary>
    public List<string>? TransformedCardInstanceIds { get; set; }
}

public sealed class CombatCardTransformReverted : CombatEvent
{
    /// <summary>The card the transformed cards revert to; null when the game sent none.</summary>
    public string? OriginalInstanceId { get; set; }

    public List<string>? TransformedCardInstanceIds { get; set; }
}

/// <summary>
/// A card paid a player attribute to act. Optional in the game: builds that lack the event
/// produce none, and attribution then leaves the cost unbooked.
/// </summary>
public sealed class CombatCardActionCostSpent : CombatEvent
{
    public string ExecutingCard { get; set; } = string.Empty;

    public CombatPlayerAttribute? PlayerAttributeSpent { get; set; }
}

/// <summary>
/// What an effect targeted. Compared by reference, as the game's targets are, so an aura's
/// target set keeps every target the game sent.
/// </summary>
public abstract class CombatTarget { }

public sealed class CombatCardTarget : CombatTarget
{
    /// <summary>The targeted card's instance id.</summary>
    public string Target { get; set; } = string.Empty;
}

public sealed class CombatPlayerTarget : CombatTarget
{
    public Combatant Target { get; set; }
}
