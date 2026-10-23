#nullable enable
using System.Globalization;

namespace BazaarPlusPlus.Game.Tooltips;

/// <summary>
/// The host-free arithmetic of the upgrade preview's value projection: how an item's runtime
/// attribute values carry over to its next tier, how out-of-combat multiplier auras are undone
/// and reapplied, and how a projected value is rendered. Each host builds the projected card and
/// resolves the game's tooltip tokens against it with its own game types; both feed the numbers
/// through here so that the desktop and Android previews show the same next-tier values.
/// </summary>
public static class UpgradePreviewValueMath
{
    private const float IdentityTolerance = 0.0001f;

    /// <summary>
    /// The next tier's value of an attribute the item already has: the next tier's base plus
    /// whatever the item gained over its current base at runtime.
    /// </summary>
    public static int ProjectAttribute(int currentValue, int? currentBase, int nextBase) =>
        currentBase.HasValue ? nextBase + currentValue - currentBase.Value : nextBase;

    /// <summary>The multiplier a <c>PercentCooldownReduction</c> value applies to a cooldown.</summary>
    public static float CooldownMultiplier(int percentCooldownReduction) =>
        Math.Max(0, 100 - percentCooldownReduction) / 100f;

    /// <summary>
    /// Reapplies multiplicative modifiers at the next tier: the runtime delta is taken with the
    /// current multiplier undone, added to the next tier's base, and scaled by the projected
    /// multiplier. False when neither side multiplies, or when a multiplier cannot be undone;
    /// the additive projection then stands.
    /// </summary>
    public static bool TryApplyMultipliers(
        int? currentValue,
        int? currentBase,
        int nextBase,
        UpgradePreviewMultipliers multipliers,
        out int projectedValue
    )
    {
        projectedValue = default;
        var current = multipliers.Current;
        var projected = multipliers.Projected;
        if (
            MathF.Abs(current - 1f) < IdentityTolerance
            && MathF.Abs(projected - 1f) < IdentityTolerance
        )
            return false;

        if (current <= 0f || projected < 0f)
            return false;

        var runtimeDelta = 0f;
        if (currentBase.HasValue && currentValue.HasValue)
            runtimeDelta = currentValue.Value / current - currentBase.Value;

        projectedValue = Math.Max(
            0,
            (int)Math.Round((nextBase + runtimeDelta) * projected, MidpointRounding.AwayFromZero)
        );
        return true;
    }

    /// <summary>
    /// The current and upgraded cooldowns in seconds, from the two cards' <c>CooldownMax</c>
    /// in milliseconds. <c>CooldownMax</c> is already the effective runtime cooldown: applying
    /// the separate <c>PercentCooldownReduction</c> again would double-count conditional auras
    /// such as Kitchen Scale's "Cooldown is halved".
    /// </summary>
    public static bool TryEffectiveCooldownSeconds(
        int? currentMilliseconds,
        int? upgradedMilliseconds,
        out float currentSeconds,
        out float upgradedSeconds
    )
    {
        currentSeconds = default;
        upgradedSeconds = default;
        if (currentMilliseconds is not { } current || upgradedMilliseconds is not { } upgraded)
            return false;
        if (current <= 0 || upgraded <= 0)
            return false;

        currentSeconds = current / 1000f;
        upgradedSeconds = upgraded / 1000f;
        return true;
    }

    /// <summary>The cooldown clock's text for a number of seconds.</summary>
    public static string CooldownText(float seconds) => $"{seconds:F1}";

    /// <summary>
    /// A projected raw value in the units the tooltip renders: the game converts millisecond
    /// attributes to seconds before it formats them.
    /// </summary>
    public static float ToRenderedUnits(float value, bool inSeconds) =>
        inSeconds ? (float)((double)value * 0.001) : value;

    /// <summary>The game's tooltip number format: one decimal for a fraction, else the integer.</summary>
    public static string Format(float value) =>
        value % 1f != 0f
            ? value.ToString("F1", CultureInfo.CurrentCulture)
            : value.ToString(CultureInfo.CurrentCulture);

    public static bool HaveSameFormattedValue(float currentValue, float projectedValue) =>
        string.Equals(Format(currentValue), Format(projectedValue), StringComparison.Ordinal);
}

/// <summary>
/// The product of the multiplier auras on one attribute, for the item as it is
/// (<see cref="Current"/>) and as projected to its next tier (<see cref="Projected"/>).
/// </summary>
public readonly record struct UpgradePreviewMultipliers(float Current, float Projected)
{
    public static UpgradePreviewMultipliers Identity => new(1f, 1f);

    public UpgradePreviewMultipliers Times(UpgradePreviewMultipliers other) =>
        new(Current * other.Current, Projected * other.Projected);

    public UpgradePreviewMultipliers TimesCurrent(float factor) => new(Current * factor, Projected);

    public UpgradePreviewMultipliers TimesProjected(float factor) =>
        new(Current, Projected * factor);
}
