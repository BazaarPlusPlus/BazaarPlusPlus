#nullable enable
namespace BazaarPlusPlus.Game.EventPreview;

internal sealed class EncounterStepReference
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

    // Card-count ownership prerequisites; all must be satisfied for the step to be
    // offered ("if you have Powder Keg or the Big One" is one requirement whose ids
    // are alternatives).
    public IReadOnlyList<EncounterCardRequirement> Requirements { get; }
}

// One spawn group of a choice event: fixed always-offered steps, or — when the
// group itself selects randomly — a pool the event rolls members from.
internal sealed class EncounterChoiceGroupData
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

    // Day-gated groups (Wishing Fountain carries one price tier per day); inactive
    // groups are not offered at all, so they must not render as choices.
    public EncounterDayCondition? DayCondition { get; }
}
