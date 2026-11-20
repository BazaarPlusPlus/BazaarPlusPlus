#nullable enable
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.Android.GameInterop;

// Selection order matters because the first match determines the Collection's opening source.
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
