#nullable enable
namespace BazaarPlusPlus.Game.EncounterPreview;

public sealed class EncounterStepReference
{
    public EncounterStepReference(
        Guid templateId,
        IReadOnlyList<EncounterCardRequirement>? requirements = null
    )
    {
        TemplateId = templateId;
        Requirements = requirements ?? Array.Empty<EncounterCardRequirement>();
    }

    public Guid TemplateId { get; }

    public IReadOnlyList<EncounterCardRequirement> Requirements { get; }
}

public sealed class EncounterChoiceGroupData
{
    public EncounterChoiceGroupData(
        bool isRandomPool,
        IReadOnlyList<EncounterStepReference> members,
        EncounterDayCondition? dayCondition = null
    )
    {
        IsRandomPool = isRandomPool;
        Members = members;
        DayCondition = dayCondition;
    }

    public bool IsRandomPool { get; }

    public IReadOnlyList<EncounterStepReference> Members { get; }

    public EncounterDayCondition? DayCondition { get; }
}
