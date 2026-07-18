#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Patches.NameOverride;

internal enum NameOverrideOperation
{
    ResolveProfile,
    DisplayUsername,
    UpdatePlayer,
    SetHeroName,
}

internal enum NameOverrideReasonCode
{
    ProfileUnavailable,
    Replaced,
}

[BppLogEventSource]
internal static class NameOverrideLogEvents
{
    internal static readonly BppLogFieldDefinition ValueAppliedOperation = PublicLow(
        0,
        "operation"
    );
    internal static readonly BppLogFieldDefinition ValueAppliedReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition ValueApplied = new(
        BppLogFeatureScope.NameOverride,
        "name_override.value.applied",
        [ValueAppliedOperation, ValueAppliedReasonCode]
    );
    internal static readonly BppLogFieldDefinition ValueSkippedOperation = PublicLow(
        0,
        "operation"
    );
    internal static readonly BppLogFieldDefinition ValueSkippedReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition ValueSkipped = new(
        BppLogFeatureScope.NameOverride,
        "name_override.value.skipped",
        [ValueSkippedOperation, ValueSkippedReasonCode]
    );

    private static BppLogFieldDefinition PublicLow(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.Public,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.Low
        );
}
