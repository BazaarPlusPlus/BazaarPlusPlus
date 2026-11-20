#nullable enable
namespace BazaarPlusPlus.Core.GameState;

public readonly struct EncounterIdsSnapshot
{
    public string? CurrentEncounterId { get; init; }
    public Guid? CurrentEncounterTemplateId { get; init; }
    public bool IsChoiceState { get; init; }
    public bool IsSelectionState { get; init; }
    public IReadOnlyList<string> ChoiceSelectionEntryIds { get; init; }
    public IReadOnlyList<Guid> ChoiceSelectionTemplateIds { get; init; }

    public static EncounterIdsSnapshot Empty { get; } =
        new()
        {
            CurrentEncounterId = null,
            CurrentEncounterTemplateId = null,
            IsChoiceState = false,
            IsSelectionState = false,
            ChoiceSelectionEntryIds = Array.Empty<string>(),
            ChoiceSelectionTemplateIds = Array.Empty<Guid>(),
        };
}
