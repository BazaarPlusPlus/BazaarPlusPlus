#nullable enable
namespace BazaarPlusPlus.Core.Combat;

// The configured values of a card's attribute modifiers (ADR-0019), as far as attribution can
// evaluate them without the live game: a fixed number, a range, a reference to the source card's
// own attribute, or a kind of reference it can only name. Records, so two copies of one modifier
// compare equal as the game's do.

/// <summary>A configured value an attribute modifier applies.</summary>
public abstract record CombatValue;

/// <summary>A fixed number.</summary>
public sealed record CombatFixedValue : CombatValue
{
    public float Value { get; init; }
}

/// <summary>A number the game draws from a range at run time.</summary>
public sealed record CombatRangeValue : CombatValue
{
    public float MinValue { get; init; }

    public float MaxValue { get; init; } = 1f;

    public CombatValueModifier? Modifier { get; init; }
}

/// <summary>A card attribute's value, read from the target the reference names.</summary>
public sealed record CombatCardAttributeReference : CombatValue
{
    public CombatCardAttribute AttributeType { get; init; }

    /// <summary>The reference reads the source card itself, the only target attribution can read.</summary>
    public bool TargetsSelf { get; init; }

    public CombatValueModifier? Modifier { get; init; }
}

/// <summary>A reference attribution can name but not evaluate.</summary>
public sealed record CombatOpaqueReference : CombatValue
{
    public CombatOpaqueReferenceKind Kind { get; init; }
}

public enum CombatOpaqueReferenceKind
{
    CardAttributeUnscaled,
    AttributeChange,
    PlayerAttribute,
    CardCount,
}

/// <summary>A value of a kind attribution does not know.</summary>
public sealed record CombatUnsupportedValue : CombatValue;

/// <summary>An operation a configured value applies to the value it modifies.</summary>
public sealed record CombatValueModifier
{
    public CombatValueModifierMode ModifyMode { get; init; } = CombatValueModifierMode.Add;

    public CombatValue Value { get; init; } = new CombatFixedValue();

    public bool ShouldRound { get; init; } = true;

    /// <summary>
    /// The game's own arithmetic over a fixed operand: rounding away from zero, a positive
    /// fraction below one rounding up to one, and division by zero leaving the value unchanged.
    /// </summary>
    public float GetModifiedValue(float originalValue, float operand) =>
        ModifyMode switch
        {
            CombatValueModifierMode.Add => originalValue + operand,
            CombatValueModifierMode.Subtract => originalValue - operand,
            CombatValueModifierMode.Multiply => ShouldRound
                ? Rounded(originalValue * operand)
                : originalValue * operand,
            CombatValueModifierMode.Divide => operand == 0f ? originalValue
            : ShouldRound ? Rounded(originalValue / operand)
            : originalValue / operand,
            _ => originalValue,
        };

    private static float Rounded(float value) =>
        value > 0f && value < 1f ? 1 : (int)Math.Round(value, MidpointRounding.AwayFromZero);
}

/// <summary>A card ability's or aura's attribute modifier: which attribute, how, and by what.</summary>
public sealed record CombatAttributeModifier
{
    public CombatCardAttribute AttributeType { get; init; }

    public CombatAttributeOperation Operation { get; init; }

    public CombatValue Value { get; init; } = new CombatFixedValue();
}
