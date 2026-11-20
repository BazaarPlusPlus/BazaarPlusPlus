#nullable enable
using System.Globalization;

namespace BazaarPlusPlus.Game.Tooltips;

public static class UpgradePreviewValueMath
{
    private const float IdentityTolerance = 0.0001f;

    public static int ProjectAttribute(int currentValue, int? currentBase, int nextBase) =>
        currentBase.HasValue ? nextBase + currentValue - currentBase.Value : nextBase;

    public static float CooldownMultiplier(int percentCooldownReduction) =>
        Math.Max(0, 100 - percentCooldownReduction) / 100f;

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

    // CooldownMax already includes runtime auras such as Kitchen Scale. Applying PercentCooldownReduction would count them twice.
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

    public static string CooldownText(float seconds) => $"{seconds:F1}";

    // The game renders millisecond attributes as seconds.
    public static float ToRenderedUnits(float value, bool inSeconds) =>
        inSeconds ? (float)((double)value * 0.001) : value;

    public static string Format(float value) =>
        value % 1f != 0f
            ? value.ToString("F1", CultureInfo.CurrentCulture)
            : value.ToString(CultureInfo.CurrentCulture);

    public static bool HaveSameFormattedValue(float currentValue, float projectedValue) =>
        string.Equals(Format(currentValue), Format(projectedValue), StringComparison.Ordinal);
}

public readonly record struct UpgradePreviewMultipliers(float Current, float Projected)
{
    public static UpgradePreviewMultipliers Identity => new(1f, 1f);

    public UpgradePreviewMultipliers Times(UpgradePreviewMultipliers other) =>
        new(Current * other.Current, Projected * other.Projected);

    public UpgradePreviewMultipliers TimesCurrent(float factor) => new(Current * factor, Projected);

    public UpgradePreviewMultipliers TimesProjected(float factor) =>
        new(Current, Projected * factor);
}
