#nullable enable
namespace BazaarPlusPlus.Game.EncounterPreview;

public sealed class EncounterChoiceDetail
{
    public EncounterChoiceDetail(
        Guid templateId,
        string displayName,
        string resultText,
        EncounterRewardFilter? rewardFilter,
        bool isEligible = true,
        EncounterChoicePool? pool = null
    )
    {
        TemplateId = templateId;
        DisplayName = displayName;
        ResultText = resultText ?? string.Empty;
        RewardFilter = rewardFilter;
        IsEligible = isEligible;
        Pool = pool;
    }

    public Guid TemplateId { get; }

    public string DisplayName { get; }

    public string ResultText { get; }

    public EncounterRewardFilter? RewardFilter { get; }

    public bool IsEligible { get; }

    // A choice event can contain a random spawn group, so one choice can stand for an entire pool.
    public EncounterChoicePool? Pool { get; }
}

public sealed class EncounterChoicePool
{
    public EncounterChoicePool(
        bool isCombat,
        int optionCount,
        System.Collections.Generic.IReadOnlyList<EncounterChoiceDetail> entries
    )
    {
        IsCombat = isCombat;
        OptionCount = optionCount;
        Entries = entries;
    }

    public bool IsCombat { get; }

    public int OptionCount { get; }

    public System.Collections.Generic.IReadOnlyList<EncounterChoiceDetail> Entries { get; }
}
