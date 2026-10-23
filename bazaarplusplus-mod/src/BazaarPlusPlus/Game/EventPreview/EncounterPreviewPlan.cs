#nullable enable
using System.Collections.ObjectModel;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.EventPreview;

internal sealed class EncounterPreviewTemplatePlan
{
    public EncounterPreviewTemplatePlan(
        Guid templateId,
        EncounterPreviewTemplateKind kind,
        IReadOnlyCollection<EHero>? heroes,
        string? internalName,
        EncounterPreviewLocalizedText? title,
        EncounterPreviewLocalizedText? description,
        IReadOnlyDictionary<string, EncounterPreviewAbilityValue>? abilityValues,
        EncounterRewardFilter? rewardFilter
    )
    {
        TemplateId = templateId;
        Kind = kind;
        Heroes = CopyHeroes(heroes);
        InternalName = internalName ?? string.Empty;
        Title = title ?? new EncounterPreviewLocalizedText(null, null);
        Description = description ?? new EncounterPreviewLocalizedText(null, null);
        AbilityValues = CopyAbilityValues(abilityValues);
        RewardFilter = EncounterPreviewPlanCopies.CopyRewardFilter(rewardFilter);
    }

    public Guid TemplateId { get; }

    public EncounterPreviewTemplateKind Kind { get; }

    public IReadOnlyList<EHero> Heroes { get; }

    public string InternalName { get; }

    public EncounterPreviewLocalizedText Title { get; }

    public EncounterPreviewLocalizedText Description { get; }

    public IReadOnlyDictionary<string, EncounterPreviewAbilityValue> AbilityValues { get; }

    public EncounterRewardFilter? RewardFilter { get; }

    private static IReadOnlyList<EHero> CopyHeroes(IReadOnlyCollection<EHero>? heroes)
    {
        if (heroes == null || heroes.Count == 0)
            return Array.Empty<EHero>();

        var result = new EHero[heroes.Count];
        var index = 0;
        foreach (var hero in heroes)
            result[index++] = hero;
        return Array.AsReadOnly(result);
    }

    private static IReadOnlyDictionary<string, EncounterPreviewAbilityValue> CopyAbilityValues(
        IReadOnlyDictionary<string, EncounterPreviewAbilityValue>? abilityValues
    )
    {
        if (abilityValues == null || abilityValues.Count == 0)
        {
            return new ReadOnlyDictionary<string, EncounterPreviewAbilityValue>(
                new Dictionary<string, EncounterPreviewAbilityValue>(StringComparer.Ordinal)
            );
        }

        var result = new Dictionary<string, EncounterPreviewAbilityValue>(
            abilityValues.Count,
            StringComparer.Ordinal
        );
        foreach (var pair in abilityValues)
        {
            if (string.IsNullOrEmpty(pair.Key) || pair.Value == null)
                continue;
            result[pair.Key] = new EncounterPreviewAbilityValue(
                pair.Value.ValueText,
                pair.Value.Unit
            );
        }

        return new ReadOnlyDictionary<string, EncounterPreviewAbilityValue>(result);
    }
}

internal sealed class EncounterPreviewEventPlan
{
    public EncounterPreviewEventPlan(
        Guid templateId,
        bool isRandomSelectionEvent,
        bool suppressRandomOutcome,
        int? choiceLimit,
        IReadOnlyList<EncounterOutcomeGroupData>? outcomeGroups,
        IReadOnlyList<EncounterChoiceGroupData>? choiceGroups
    )
    {
        TemplateId = templateId;
        IsRandomSelectionEvent = isRandomSelectionEvent;
        SuppressRandomOutcome = suppressRandomOutcome;
        ChoiceLimit = choiceLimit;
        OutcomeGroups = EncounterPreviewPlanCopies.CopyOutcomeGroups(outcomeGroups);
        ChoiceGroups = EncounterPreviewPlanCopies.CopyChoiceGroups(choiceGroups);
    }

    public Guid TemplateId { get; }

    public bool IsRandomSelectionEvent { get; }

    public bool SuppressRandomOutcome { get; }

    public int? ChoiceLimit { get; }

    public IReadOnlyList<EncounterOutcomeGroupData> OutcomeGroups { get; }

    public IReadOnlyList<EncounterChoiceGroupData> ChoiceGroups { get; }
}

internal sealed class LevelUpPreviewHeroCondition
{
    public LevelUpPreviewHeroCondition(
        IReadOnlyCollection<EHero>? heroes,
        string? comparisonOperator
    )
    {
        Heroes = new List<EHero>(heroes ?? Array.Empty<EHero>()).AsReadOnly();
        ComparisonOperator = comparisonOperator ?? string.Empty;
    }

    public IReadOnlyList<EHero> Heroes { get; }

    public string ComparisonOperator { get; }
}

internal sealed class LevelUpPreviewGroup
{
    public LevelUpPreviewGroup(
        uint randomWeight,
        int limit,
        IReadOnlyCollection<Guid>? templateIds,
        IReadOnlyCollection<LevelUpPreviewHeroCondition>? heroConditions
    )
    {
        RandomWeight = randomWeight;
        Limit = Math.Max(1, limit);
        TemplateIds = new List<Guid>(templateIds ?? Array.Empty<Guid>()).AsReadOnly();
        HeroConditions = new List<LevelUpPreviewHeroCondition>(
            heroConditions ?? Array.Empty<LevelUpPreviewHeroCondition>()
        ).AsReadOnly();
    }

    public uint RandomWeight { get; }

    public int Limit { get; }

    public IReadOnlyList<Guid> TemplateIds { get; }

    public IReadOnlyList<LevelUpPreviewHeroCondition> HeroConditions { get; }
}

internal sealed class LevelUpPreviewPlan
{
    public LevelUpPreviewPlan(
        int level,
        int healthIncrease,
        bool isRandomSelection,
        IReadOnlyCollection<LevelUpPreviewGroup>? groups
    )
    {
        Level = level;
        HealthIncrease = healthIncrease;
        IsRandomSelection = isRandomSelection;
        Groups = new List<LevelUpPreviewGroup>(
            groups ?? Array.Empty<LevelUpPreviewGroup>()
        ).AsReadOnly();
    }

    public int Level { get; }

    public int HealthIncrease { get; }

    public bool IsRandomSelection { get; }

    public IReadOnlyList<LevelUpPreviewGroup> Groups { get; }
}

internal sealed class EncounterPreviewSnapshot
{
    private readonly Dictionary<Guid, EncounterPreviewEventPlan> _eventsById;
    private readonly Dictionary<int, LevelUpPreviewPlan> _levelUpsByLevel;
    private readonly Dictionary<Guid, EncounterPreviewTemplatePlan> _templatesById;

    public EncounterPreviewSnapshot(
        IEnumerable<EncounterPreviewEventPlan>? events,
        IEnumerable<EncounterPreviewTemplatePlan>? templates,
        IEnumerable<LevelUpPreviewPlan>? levelUps = null,
        EventPreviewCoverage? coverage = null
    )
    {
        _eventsById = new Dictionary<Guid, EncounterPreviewEventPlan>();
        _levelUpsByLevel = new Dictionary<int, LevelUpPreviewPlan>();
        _templatesById = new Dictionary<Guid, EncounterPreviewTemplatePlan>();

        var eventList = new List<EncounterPreviewEventPlan>();
        if (events != null)
        {
            foreach (var eventPlan in events)
            {
                if (eventPlan == null)
                    continue;
                if (!_eventsById.TryAdd(eventPlan.TemplateId, eventPlan))
                {
                    throw new ArgumentException(
                        $"Duplicate encounter-preview event template id '{eventPlan.TemplateId:D}'.",
                        nameof(events)
                    );
                }
                eventList.Add(eventPlan);
            }
        }

        var levelUpList = new List<LevelUpPreviewPlan>();
        if (levelUps != null)
        {
            foreach (var levelUpPlan in levelUps)
            {
                if (levelUpPlan == null)
                    continue;
                if (!_levelUpsByLevel.TryAdd(levelUpPlan.Level, levelUpPlan))
                {
                    throw new ArgumentException(
                        $"Duplicate level-up preview level '{levelUpPlan.Level}'.",
                        nameof(levelUps)
                    );
                }
                levelUpList.Add(levelUpPlan);
            }
        }

        var templateList = new List<EncounterPreviewTemplatePlan>();
        if (templates != null)
        {
            foreach (var templatePlan in templates)
            {
                if (templatePlan == null)
                    continue;
                if (!_templatesById.TryAdd(templatePlan.TemplateId, templatePlan))
                {
                    throw new ArgumentException(
                        $"Duplicate encounter-preview template id '{templatePlan.TemplateId:D}'.",
                        nameof(templates)
                    );
                }
                templateList.Add(templatePlan);
            }
        }

        Events = eventList.AsReadOnly();
        LevelUps = levelUpList.AsReadOnly();
        Templates = templateList.AsReadOnly();
        Coverage = coverage ?? new EventPreviewCoverage(0, 0, 0, 0);
    }

    public IReadOnlyList<EncounterPreviewEventPlan> Events { get; }

    public IReadOnlyList<EncounterPreviewTemplatePlan> Templates { get; }

    public IReadOnlyList<LevelUpPreviewPlan> LevelUps { get; }

    public EventPreviewCoverage Coverage { get; }

    public int EventCount => _eventsById.Count;

    public int TemplateCount => _templatesById.Count;

    public int LevelUpCount => _levelUpsByLevel.Count;

    public bool TryGetEvent(Guid templateId, out EncounterPreviewEventPlan eventPlan) =>
        _eventsById.TryGetValue(templateId, out eventPlan!);

    public bool TryGetTemplate(Guid templateId, out EncounterPreviewTemplatePlan templatePlan) =>
        _templatesById.TryGetValue(templateId, out templatePlan!);

    public bool TryGetLevelUp(int level, out LevelUpPreviewPlan levelUpPlan) =>
        _levelUpsByLevel.TryGetValue(level, out levelUpPlan!);
}

internal static class EncounterPreviewPlanCopies
{
    public static IReadOnlyList<EncounterOutcomeGroupData> CopyOutcomeGroups(
        IReadOnlyList<EncounterOutcomeGroupData>? groups
    )
    {
        if (groups == null || groups.Count == 0)
            return Array.Empty<EncounterOutcomeGroupData>();

        var result = new EncounterOutcomeGroupData[groups.Count];
        for (var i = 0; i < groups.Count; i++)
            result[i] = CopyOutcomeGroup(groups[i]);
        return Array.AsReadOnly(result);
    }

    public static IReadOnlyList<EncounterChoiceGroupData> CopyChoiceGroups(
        IReadOnlyList<EncounterChoiceGroupData>? groups
    )
    {
        if (groups == null || groups.Count == 0)
            return Array.Empty<EncounterChoiceGroupData>();

        var result = new EncounterChoiceGroupData[groups.Count];
        for (var i = 0; i < groups.Count; i++)
            result[i] = CopyChoiceGroup(groups[i]);
        return Array.AsReadOnly(result);
    }

    public static EncounterRewardFilter? CopyRewardFilter(EncounterRewardFilter? filter)
    {
        if (filter == null)
            return null;

        return new EncounterRewardFilter(
            filter.CardType,
            filter.Quantity,
            filter.FromAnyHero,
            CopyList(filter.Sizes),
            CopyList(filter.Tiers),
            CopyList(filter.Tags),
            CopyList(filter.Keywords),
            filter.FilterSummary,
            CopyList(filter.ExcludedTags),
            CopyList(filter.ExcludedKeywords),
            filter.UsesDayTierTable,
            filter.UsesDayTierDistribution
        );
    }

    public static IReadOnlyList<EncounterCardRequirement> CopyRequirements(
        IReadOnlyList<EncounterCardRequirement>? requirements
    )
    {
        if (requirements == null || requirements.Count == 0)
            return Array.Empty<EncounterCardRequirement>();

        var result = new EncounterCardRequirement[requirements.Count];
        for (var i = 0; i < requirements.Count; i++)
            result[i] = CopyRequirement(requirements[i]);
        return Array.AsReadOnly(result);
    }

    private static EncounterOutcomeGroupData CopyOutcomeGroup(EncounterOutcomeGroupData group)
    {
        var queryPools = new EncounterOutcomeQueryPool[group.QueryPools.Count];
        for (var i = 0; i < queryPools.Length; i++)
        {
            var pool = group.QueryPools[i];
            queryPools[i] = new EncounterOutcomeQueryPool(
                CopyRewardFilter(pool.Filter),
                pool.Quantity
            );
        }

        return new EncounterOutcomeGroupData(
            group.Weight,
            CopyList(group.Ids),
            Array.AsReadOnly(queryPools),
            CopyRequirements(group.Requirements),
            CopyDayCondition(group.DayCondition)
        );
    }

    private static EncounterChoiceGroupData CopyChoiceGroup(EncounterChoiceGroupData group)
    {
        var members = new EncounterStepReference[group.Members.Count];
        for (var i = 0; i < members.Length; i++)
        {
            var member = group.Members[i];
            members[i] = new EncounterStepReference(
                member.TemplateId,
                CopyRequirements(member.Requirements)
            );
        }

        return new EncounterChoiceGroupData(
            group.IsRandomPool,
            Array.AsReadOnly(members),
            CopyDayCondition(group.DayCondition)
        );
    }

    private static EncounterCardRequirement CopyRequirement(EncounterCardRequirement requirement)
    {
        var tagGroups = new IReadOnlyList<string>[requirement.TagCandidateGroups.Count];
        for (var i = 0; i < tagGroups.Length; i++)
            tagGroups[i] = CopyList(requirement.TagCandidateGroups[i]);

        return new EncounterCardRequirement(
            CopyList(requirement.Ids),
            Array.AsReadOnly(tagGroups),
            requirement.TagOperator,
            requirement.Comparison,
            requirement.Amount
        );
    }

    private static EncounterDayCondition? CopyDayCondition(EncounterDayCondition? condition) =>
        condition is { } value
            ? new EncounterDayCondition(
                value.Day,
                value.Comparison,
                CopyList(
                    value
                        .AdditionalConditions.Select(item => CopyDayCondition(item)!.Value)
                        .ToArray()
                )
            )
            : null;

    private static IReadOnlyList<T> CopyList<T>(IReadOnlyList<T> values)
    {
        if (values.Count == 0)
            return Array.Empty<T>();
        var result = new T[values.Count];
        for (var i = 0; i < result.Length; i++)
            result[i] = values[i];
        return Array.AsReadOnly(result);
    }
}
