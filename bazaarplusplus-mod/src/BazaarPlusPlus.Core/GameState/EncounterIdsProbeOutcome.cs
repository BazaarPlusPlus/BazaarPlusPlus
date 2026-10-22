#nullable enable
namespace BazaarPlusPlus.Core.GameState;

public readonly record struct EncounterIdsProbeOutcome(
    bool IsSuccess,
    EncounterIdsSnapshot Snapshot,
    EncounterProbeFailureReason FailureReason,
    Exception? Exception
)
{
    public static EncounterIdsProbeOutcome Success(EncounterIdsSnapshot snapshot) =>
        new(true, snapshot, EncounterProbeFailureReason.None, null);

    public static EncounterIdsProbeOutcome Failure(Exception exception) =>
        new(
            false,
            EncounterIdsSnapshot.Empty,
            EncounterProbeFailureReason.EncounterIdsReadException,
            exception
        );
}
