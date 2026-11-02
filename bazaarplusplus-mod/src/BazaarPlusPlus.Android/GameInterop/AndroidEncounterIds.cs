#nullable enable
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The encounter ids snapshot, the rules of the desktop's <c>EncounterStateProbe</c> read over
/// values, so they run in a test without IL2CPP. A selection entry is a live instance id (e.g.
/// <c>ped_XtbgKux</c>), resolved to its template id, or already a template id. Template ids keep
/// the selection's order, because the first match decides the Collection's opening source.
/// </summary>
internal static class AndroidEncounterIds
{
    public static EncounterIdsSnapshot Build(
        string? currentEncounterId,
        bool isChoiceState,
        bool isSelectionState,
        IReadOnlyList<string> selection,
        IReadOnlyDictionary<string, Guid> templateIdsByInstanceId
    )
    {
        Guid? currentTemplateId = Guid.TryParse(currentEncounterId, out var current)
            ? current
            : null;
        var entryIds = new List<string>();
        var templateIds = new List<Guid>();
        if (isSelectionState)
        {
            foreach (var entry in selection)
            {
                if (string.IsNullOrEmpty(entry))
                    continue;
                entryIds.Add(entry);
                if (
                    templateIdsByInstanceId.TryGetValue(entry, out var templateId)
                    || Guid.TryParse(entry, out templateId)
                )
                {
                    if (templateId != Guid.Empty)
                        templateIds.Add(templateId);
                }
            }
        }
        return new EncounterIdsSnapshot
        {
            CurrentEncounterId = currentEncounterId,
            CurrentEncounterTemplateId = currentTemplateId,
            IsChoiceState = isChoiceState,
            IsSelectionState = isSelectionState,
            ChoiceSelectionEntryIds = entryIds.ToArray(),
            ChoiceSelectionTemplateIds = templateIds.ToArray(),
        };
    }
}
