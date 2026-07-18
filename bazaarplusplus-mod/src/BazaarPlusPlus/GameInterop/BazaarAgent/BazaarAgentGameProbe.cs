#nullable enable
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.GameInterop.Encounter;

namespace BazaarPlusPlus.GameInterop;

/// <summary>
/// BazaarPlusPlus-side implementation of <see cref="IBazaarAgentGameProbe"/>. Wraps the
/// internal encounter probe + type resolver, exposing only the public snapshot DTOs across
/// the assembly boundary. Replay state reads live on <see cref="IBazaarAgentReplayRecorder"/>.
/// </summary>
internal sealed class BazaarAgentGameProbe : IBazaarAgentGameProbe, IBazaarAgentTypedGameProbe
{
    private readonly IEncounterStateProbe _encounterState;

    public BazaarAgentGameProbe(IEncounterStateProbe encounterState)
    {
        _encounterState = encounterState ?? throw new ArgumentNullException(nameof(encounterState));
    }

    public EncounterIdsSnapshot GetEncounterIds() => _encounterState.GetEncounterIds();

    public BazaarAgentGameProbeOutcome<EncounterIdsSnapshot> GetEncounterIdsOutcome()
    {
        if (_encounterState is not ITypedEncounterStateProbe typed)
            return BazaarAgentGameProbeOutcome<EncounterIdsSnapshot>.Success(
                _encounterState.GetEncounterIds()
            );
        var outcome = typed.GetEncounterIdsOutcome();
        return outcome.IsSuccess
            ? BazaarAgentGameProbeOutcome<EncounterIdsSnapshot>.Success(outcome.Snapshot)
            : BazaarAgentGameProbeOutcome<EncounterIdsSnapshot>.Failure(
                outcome.Snapshot,
                outcome.Exception
            );
    }

    public EncounterTargetingSnapshot GetTargetingState() => _encounterState.GetTargetingState();

    public BazaarAgentGameProbeOutcome<EncounterTargetingSnapshot> GetTargetingStateOutcome()
    {
        if (_encounterState is not ITypedEncounterStateProbe typed)
            return BazaarAgentGameProbeOutcome<EncounterTargetingSnapshot>.Success(
                _encounterState.GetTargetingState()
            );
        var outcome = typed.GetTargetingStateOutcome();
        return outcome.IsSuccess
            ? BazaarAgentGameProbeOutcome<EncounterTargetingSnapshot>.Success(outcome.Snapshot)
            : BazaarAgentGameProbeOutcome<EncounterTargetingSnapshot>.Failure(
                outcome.Snapshot,
                outcome.Exception
            );
    }

    public string? ResolveEncounterType(string? encounterId) =>
        EncounterTypeResolver.Resolve(encounterId);
}
