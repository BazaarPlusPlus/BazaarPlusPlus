#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;

namespace BazaarPlusPlus.GameInterop.CombatSimulation;

internal static class OptionalCombatTempoTypes
{
    private static readonly EActionCommandType? PlayerTempoApply = Parse<EActionCommandType>(
        "PlayerTempoApply"
    );
    private static readonly EActionCommandType? PlayerTempoRemove = Parse<EActionCommandType>(
        "PlayerTempoRemove"
    );
    private static readonly ECardAttributeType? TempoApplyAmount = Parse<ECardAttributeType>(
        "TempoApplyAmount"
    );
    private static readonly ECardAttributeType? TempoRemoveAmount = Parse<ECardAttributeType>(
        "TempoRemoveAmount"
    );
    private static readonly ECardStats? TempoAdded = Parse<ECardStats>("TempoAdded");
    private static readonly ECardStats? TempoSpent = Parse<ECardStats>("TempoSpent");

    internal static bool IsApplyAction(EActionCommandType action) =>
        PlayerTempoApply.HasValue && action.Equals(PlayerTempoApply.Value);

    internal static bool IsRemoveAction(EActionCommandType action) =>
        PlayerTempoRemove.HasValue && action.Equals(PlayerTempoRemove.Value);

    internal static bool IsAction(EActionCommandType action) =>
        IsApplyAction(action) || IsRemoveAction(action);

    internal static bool IsAmountAttribute(ECardAttributeType attribute) =>
        (TempoApplyAmount.HasValue && attribute.Equals(TempoApplyAmount.Value))
        || (TempoRemoveAmount.HasValue && attribute.Equals(TempoRemoveAmount.Value));

    internal static bool TryGetCardAttribute(
        EActionCommandType action,
        out ECardAttributeType attribute
    )
    {
        if (IsApplyAction(action) && TempoApplyAmount.HasValue)
        {
            attribute = TempoApplyAmount.Value;
            return true;
        }

        if (IsRemoveAction(action) && TempoRemoveAmount.HasValue)
        {
            attribute = TempoRemoveAmount.Value;
            return true;
        }

        attribute = default;
        return false;
    }

    internal static bool TryGetAddedStat(out ECardStats statistic) =>
        TryGet(TempoAdded, out statistic);

    internal static bool TryGetSpentStat(out ECardStats statistic) =>
        TryGet(TempoSpent, out statistic);

    private static bool TryGet<TEnum>(TEnum? candidate, out TEnum value)
        where TEnum : struct, Enum
    {
        if (candidate.HasValue)
        {
            value = candidate.Value;
            return true;
        }

        value = default;
        return false;
    }

    private static TEnum? Parse<TEnum>(string name)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(name, ignoreCase: false, out var value)
        && Enum.IsDefined(typeof(TEnum), value)
            ? value
            : null;
}
