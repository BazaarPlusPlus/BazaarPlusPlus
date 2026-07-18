#nullable enable
namespace BazaarPlusPlus.Core.GameState;

internal readonly record struct EncounterIdsProbeOutcome(
    bool IsSuccess,
    EncounterIdsSnapshot Snapshot,
    EncounterProbeFailureReason FailureReason,
    Exception? Exception
)
{
    internal static EncounterIdsProbeOutcome Success(EncounterIdsSnapshot snapshot) =>
        new(true, snapshot, EncounterProbeFailureReason.None, null);

    internal static EncounterIdsProbeOutcome Failure(Exception exception) =>
        new(
            false,
            EncounterIdsSnapshot.Empty,
            EncounterProbeFailureReason.EncounterIdsReadException,
            exception
        );
}
