#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.Encounter;

namespace BazaarPlusPlus.GameInterop;

/// <summary>
/// BazaarPlusPlus-side implementation of <see cref="IBazaarAgentGameProbe"/>. Wraps the
/// internal encounter probe, type resolver, and hero identity adapter behind the public bridge
/// contract. Replay state reads live on <see cref="IBazaarAgentReplayRecorder"/>.
/// </summary>
internal sealed class BazaarAgentGameProbe : IBazaarAgentGameProbe, IBazaarAgentTypedGameProbe
{
    private readonly IEncounterStateProbe _encounterState;
    private readonly IRunContext _runContext;

    public BazaarAgentGameProbe(IEncounterStateProbe encounterState, IRunContext runContext)
    {
        _encounterState = encounterState ?? throw new ArgumentNullException(nameof(encounterState));
        _runContext = runContext ?? throw new ArgumentNullException(nameof(runContext));
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

    public BazaarAgentHeroResolution ResolveHero(string? heroId) =>
        BazaarAgentHeroIdentity.Resolve(heroId);

    public string ToAgentHeroId(EHero hero) => BazaarAgentHeroIdentity.ToAgentContextId(hero);

    public string? GetCurrentServerRunId() => _runContext.CurrentServerRunId;

    public string ResolveCardDisplayName(Card card)
    {
        if (card is null)
            return string.Empty;

        var name = CardDisplayNameResolver.Resolve(card.Template as TCardBase);
        return string.IsNullOrWhiteSpace(name) ? card.TemplateId.ToString("D") : name;
    }

    public string? ResolveCardDescription(Card card) => CardDescriptionResolver.Resolve(card);
}
