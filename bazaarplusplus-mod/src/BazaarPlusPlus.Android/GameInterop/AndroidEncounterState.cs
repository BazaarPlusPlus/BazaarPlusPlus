using BazaarPlusPlus.Core.GameState;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The encounter state the touch Collection opens on, the desktop's <c>EncounterStateProbe</c> on
/// IL2CPP: the run's current encounter and selection set, so both hosts pre-select the same
/// source for the same game state. Main thread only; Core's services keep
/// <see cref="AndroidComposition.NoEncounterState"/>, which is safe from any thread.
/// </summary>
internal sealed class AndroidEncounterState : IEncounterStateProbe
{
    public EncounterIdsProbeOutcome GetEncounterIdsOutcome()
    {
        try
        {
            var runState = Data.CurrentState;
            var appState = AppState.CurrentState;
            var isChoice = appState?.TryCast<ChoiceState>() != null;
            var isSelection =
                isChoice
                || appState?.TryCast<EncounterState>() != null
                || appState?.TryCast<LevelUpState>() != null;
            var entries = new List<string>();
            var selection = isSelection ? runState?.SelectionSet : null;
            if (selection != null)
            {
                foreach (var entry in selection)
                    entries.Add(entry);
            }
            return EncounterIdsProbeOutcome.Success(
                AndroidEncounterIds.Build(
                    runState?.CurrentEncounterId,
                    isChoice,
                    isSelection,
                    entries,
                    TemplateIds(entries)
                )
            );
        }
        catch (Exception exception)
        {
            return EncounterIdsProbeOutcome.Failure(exception);
        }
    }

    public ChoicePedestalProbeOutcome GetChoicePedestalOutcome() => AndroidChoicePedestals.Read();

    private static Dictionary<string, Guid> TemplateIds(List<string> entries)
    {
        var templateIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var entities = entries.Count == 0 ? null : Data.Entities;
        if (entities == null)
            return templateIds;
        var wanted = new HashSet<string>(entries, StringComparer.Ordinal);
        foreach (var card in entities.Values)
        {
            var instanceId = card?.InstanceId?.Value;
            if (
                instanceId != null
                && wanted.Remove(instanceId)
                && Guid.TryParse(card!.TemplateId.ToString(), out var templateId)
            )
                templateIds[instanceId] = templateId;
            if (wanted.Count == 0)
                break;
        }
        return templateIds;
    }
}
