#nullable enable
namespace BazaarPlusPlus.Core.GameState;

public enum EncounterProbeFailureReason
{
    None,
    EncounterIdsReadException,
    ChoiceResolutionException,
}

public readonly record struct ChoicePedestalProbeOutcome(
    bool IsSuccess,
    ChoicePedestalSnapshot Snapshot,
    EncounterProbeFailureReason FailureReason,
    Exception? Exception
)
{
    public static ChoicePedestalProbeOutcome Success(ChoicePedestalSnapshot snapshot) =>
        new(true, snapshot, EncounterProbeFailureReason.None, null);

    public static ChoicePedestalProbeOutcome Failure(
        EncounterProbeFailureReason reason,
        Exception? exception
    ) => new(false, ChoicePedestalSnapshot.Empty, reason, exception);
}
