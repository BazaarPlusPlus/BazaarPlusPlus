#nullable enable
namespace BazaarPlusPlus.Game.EventPreview;

public readonly struct EncounterDayCondition
{
    public EncounterDayCondition(
        int day,
        string comparison,
        IReadOnlyList<EncounterDayCondition>? additionalConditions = null
    )
    {
        Day = day;
        Comparison = comparison;
        AdditionalConditions = additionalConditions ?? Array.Empty<EncounterDayCondition>();
    }

    public int Day { get; }

    public string Comparison { get; }

    public IReadOnlyList<EncounterDayCondition> AdditionalConditions { get; }

    public bool Matches(int currentDay) =>
        (
            Comparison switch
            {
                "Equal" => currentDay == Day,
                "NotEqual" => currentDay != Day,
                "GreaterThan" => currentDay > Day,
                "GreaterThanOrEqual" => currentDay >= Day,
                "LessThan" => currentDay < Day,
                "LessThanOrEqual" => currentDay <= Day,
                _ => true,
            }
        ) && AdditionalConditions.All(condition => condition.Matches(currentDay));
}
