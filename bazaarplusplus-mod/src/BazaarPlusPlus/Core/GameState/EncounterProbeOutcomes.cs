#nullable enable
namespace BazaarPlusPlus.Core.GameState;

internal enum EncounterProbeFailureReason
{
    None,
    EncounterIdsReadException,
    ChoiceResolutionException,
    InteractionFilterReflectionUnavailable,
    InteractionFilterReadException,
    PedestalReflectionUnavailable,
    PedestalReadException,
    TargetingReadException,
}

internal readonly record struct ChoicePedestalProbeOutcome(
    bool IsSuccess,
    ChoicePedestalSnapshot Snapshot,
    EncounterProbeFailureReason FailureReason,
    Exception? Exception
)
{
    internal static ChoicePedestalProbeOutcome Success(ChoicePedestalSnapshot snapshot) =>
        new(true, snapshot, EncounterProbeFailureReason.None, null);

    internal static ChoicePedestalProbeOutcome Failure(
        EncounterProbeFailureReason reason,
        Exception? exception
    ) => new(false, ChoicePedestalSnapshot.Empty, reason, exception);
}

internal readonly record struct EncounterTargetingProbeOutcome(
    bool IsSuccess,
    EncounterTargetingSnapshot Snapshot,
    EncounterProbeFailureReason FailureReason,
    Exception? Exception
)
{
    internal static EncounterTargetingProbeOutcome Success(EncounterTargetingSnapshot snapshot) =>
        new(true, snapshot, EncounterProbeFailureReason.None, null);

    internal static EncounterTargetingProbeOutcome Failure(
        EncounterProbeFailureReason reason,
        Exception? exception
    ) => new(false, EncounterTargetingSnapshot.Empty, reason, exception);
}
