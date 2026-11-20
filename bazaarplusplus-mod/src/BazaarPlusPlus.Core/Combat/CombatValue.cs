#nullable enable
namespace BazaarPlusPlus.Core.Combat;

// Use record equality to match the game's value comparison for copied modifiers.

public abstract record CombatValue;

public sealed record CombatFixedValue : CombatValue
{
    public float Value { get; init; }
}

public sealed record CombatRangeValue : CombatValue
{
    public float MinValue { get; init; }

    public float MaxValue { get; init; } = 1f;

    public CombatValueModifier? Modifier { get; init; }
}

public sealed record CombatCardAttributeReference : CombatValue
{
    public CombatCardAttribute AttributeType { get; init; }

    public bool TargetsSelf { get; init; }

    public CombatValueModifier? Modifier { get; init; }
}

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

public sealed record CombatUnsupportedValue : CombatValue;

public sealed record CombatValueModifier
{
    public CombatValueModifierMode ModifyMode { get; init; } = CombatValueModifierMode.Add;

    public CombatValue Value { get; init; } = new CombatFixedValue();

    public bool ShouldRound { get; init; } = true;

    // Match the game's rounding and division-by-zero behavior for fixed operands.
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

public sealed record CombatAttributeModifier
{
    public CombatCardAttribute AttributeType { get; init; }

    public CombatAttributeOperation Operation { get; init; }

    public CombatValue Value { get; init; } = new CombatFixedValue();
}
