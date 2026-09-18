#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;

namespace PostCombatImpact.Tests;

internal readonly record struct OptionalCombatTempoTestValues(
    EActionCommandType ApplyAction,
    EActionCommandType RemoveAction,
    ECardAttributeType ApplyAmountAttribute,
    ECardAttributeType RemoveAmountAttribute,
    ECardStats AddedStatistic,
    ECardStats SpentStatistic
)
{
    internal static bool TryResolve(out OptionalCombatTempoTestValues values)
    {
        if (
            !TryParse("PlayerTempoApply", out EActionCommandType applyAction)
            || !TryParse("PlayerTempoRemove", out EActionCommandType removeAction)
            || !TryParse("TempoApplyAmount", out ECardAttributeType applyAmountAttribute)
            || !TryParse("TempoRemoveAmount", out ECardAttributeType removeAmountAttribute)
            || !TryParse("TempoAdded", out ECardStats addedStatistic)
            || !TryParse("TempoSpent", out ECardStats spentStatistic)
        )
        {
            values = default;
            return false;
        }

        values = new OptionalCombatTempoTestValues(
            applyAction,
            removeAction,
            applyAmountAttribute,
            removeAmountAttribute,
            addedStatistic,
            spentStatistic
        );
        return true;
    }

    private static bool TryParse<TEnum>(string name, out TEnum value)
        where TEnum : struct, Enum =>
        Enum.TryParse(name, ignoreCase: false, out value) && Enum.IsDefined(typeof(TEnum), value);
}
