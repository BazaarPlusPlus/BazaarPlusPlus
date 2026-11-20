#nullable enable
namespace BazaarPlusPlus.Game.EncounterPreview;

public sealed class EncounterOutcomeGroupData
{
    public EncounterOutcomeGroupData(
        uint weight,
        IReadOnlyList<Guid> ids,
        IReadOnlyList<EncounterOutcomeQueryPool> queryPools,
        IReadOnlyList<EncounterCardRequirement> requirements,
        EncounterDayCondition? dayCondition
    )
    {
        Weight = weight;
        Ids = ids;
        QueryPools = queryPools;
        Requirements = requirements;
        DayCondition = dayCondition;
    }

    public uint Weight { get; }

    public IReadOnlyList<Guid> Ids { get; }

    // Keep a dynamic pool's weight in the roll even when its constraints cannot be parsed.
    public IReadOnlyList<EncounterOutcomeQueryPool> QueryPools { get; }

    public IReadOnlyList<EncounterCardRequirement> Requirements { get; }

    public EncounterDayCondition? DayCondition { get; }
}

public sealed class EncounterOutcomeQueryPool
{
    public EncounterOutcomeQueryPool(EncounterRewardFilter? filter, int? quantity)
    {
        Filter = filter;
        Quantity = quantity;
    }

    public EncounterRewardFilter? Filter { get; }

    public int? Quantity { get; }
}

public sealed class EncounterOutcomeView
{
    public EncounterOutcomeView(
        int? percent,
        bool isEligible,
        bool isCombatPool,
        int optionCount,
        IReadOnlyList<EncounterChoiceDetail> details
    )
    {
        Percent = percent;
        IsEligible = isEligible;
        IsCombatPool = isCombatPool;
        OptionCount = optionCount;
        Details = details;
    }

    public int? Percent { get; }

    public bool IsEligible { get; }

    public bool IsCombatPool { get; }

    public int OptionCount { get; }

    public IReadOnlyList<EncounterChoiceDetail> Details { get; }
}
