#nullable enable
namespace BazaarPlusPlus.Game.EventPreview;

internal sealed class EncounterOption
{
    public EncounterOption(
        Guid templateId,
        string displayName,
        string resultText,
        EncounterRewardFilter? rewardFilter,
        IReadOnlyList<EncounterChoiceDetail>? choiceDetails = null,
        IReadOnlyList<EncounterOutcomeView>? outcomeGroups = null
    )
    {
        TemplateId = templateId;
        DisplayName = displayName;
        ResultText = resultText ?? string.Empty;
        RewardFilter = rewardFilter;
        ChoiceDetails = choiceDetails ?? Array.Empty<EncounterChoiceDetail>();
        OutcomeGroups = outcomeGroups;
    }

    public Guid TemplateId { get; }

    public string DisplayName { get; }

    public string ResultText { get; }

    public EncounterRewardFilter? RewardFilter { get; }

    public IReadOnlyList<EncounterChoiceDetail> ChoiceDetails { get; }

    public bool HasRewardFilter => RewardFilter != null;

    public bool HasChoiceDetails => ChoiceDetails.Count > 0;

    // Non-null for random-outcome events (the event rolls one weighted group instead
    // of presenting choices).
    public IReadOnlyList<EncounterOutcomeView>? OutcomeGroups { get; }

    public bool HasOutcomeGroups => OutcomeGroups is { Count: > 0 };
}
