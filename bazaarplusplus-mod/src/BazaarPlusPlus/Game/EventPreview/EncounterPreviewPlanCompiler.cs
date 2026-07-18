#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Game;
using BazaarGameShared.Domain.Prerequisites;
using BazaarGameShared.Domain.Prerequisites.Conditionals;
using BazaarGameShared.Domain.Spawning;
using BazaarGameShared.Domain.Spawning.SpawnFilters;
using BazaarGameShared.Domain.Spawning.SpawningContexts;
using BazaarGameShared.Domain.Values;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.EventPreview;

internal sealed class EncounterPreviewCompileResult
{
    public EncounterPreviewCompileResult(
        EncounterPreviewSnapshot snapshot,
        IReadOnlyList<Guid> failedTemplateIds
    )
    {
        Snapshot = snapshot;
        FailedTemplateIds = failedTemplateIds;
    }

    public EncounterPreviewSnapshot Snapshot { get; }

    public IReadOnlyList<Guid> FailedTemplateIds { get; }

    public int FailureCount => FailedTemplateIds.Count;

    public int LevelUpFailureCount => Snapshot.Coverage.LevelUpFailureCount;
}

internal sealed class EncounterPreviewPlanCompiler
{
    private readonly Func<object, JToken?> _prepareToken;

    public EncounterPreviewPlanCompiler()
        : this(EncounterStructuredParser.TryPrepareToken) { }

    internal EncounterPreviewPlanCompiler(Func<object, JToken?> prepareToken)
    {
        _prepareToken = prepareToken ?? throw new ArgumentNullException(nameof(prepareToken));
    }

    public EncounterPreviewCompileResult Compile(IReadOnlyDictionary<Guid, ITCard> cardMap) =>
        Compile(cardMap, new Dictionary<int, TLevelUp>());

    public EncounterPreviewCompileResult Compile(
        IReadOnlyDictionary<Guid, ITCard> cardMap,
        IReadOnlyDictionary<int, TLevelUp> levelUpMap
    )
    {
        if (cardMap == null)
            throw new ArgumentNullException(nameof(cardMap));
        if (levelUpMap == null)
            throw new ArgumentNullException(nameof(levelUpMap));

        var events = new List<EncounterPreviewEventPlan>();
        var templates = new Dictionary<Guid, EncounterPreviewTemplatePlan>();
        var failures = new HashSet<Guid>();
        var missingReferencedTemplates = new HashSet<Guid>();
        var eventTemplates = cardMap
            .Values.OfType<TCardBase>()
            .Where(template => template.Type == ECardType.EventEncounter)
            .OrderBy(template => template.Id)
            .ToArray();

        foreach (var eventTemplate in eventTemplates)
        {
            try
            {
                var token = _prepareToken(eventTemplate);
                if (token == null)
                {
                    failures.Add(eventTemplate.Id);
                    continue;
                }

                if (!TryAddTemplate(eventTemplate, token, templates, failures))
                    continue;

                var isRandomSelectionEvent = EncounterStructuredParser.TryParseEventOutcomeGroups(
                    token,
                    out var outcomeGroups
                );
                var choiceGroups = isRandomSelectionEvent
                    ? Array.Empty<EncounterChoiceGroupData>()
                    : EncounterStructuredParser.TryParseEventChoiceGroups(token);
                var choiceLimit = EncounterStructuredParser.TryParseEventChoiceLimit(token);

                foreach (var group in outcomeGroups)
                foreach (var id in group.Ids)
                    TryAddReferencedTemplate(
                        id,
                        cardMap,
                        templates,
                        failures,
                        missingReferencedTemplates
                    );
                foreach (var group in choiceGroups)
                foreach (var member in group.Members)
                    TryAddReferencedTemplate(
                        member.TemplateId,
                        cardMap,
                        templates,
                        failures,
                        missingReferencedTemplates
                    );

                events.Add(
                    new EncounterPreviewEventPlan(
                        eventTemplate.Id,
                        isRandomSelectionEvent,
                        suppressRandomOutcome: eventTemplate.Tags?.Contains(ECardTag.Merchant)
                            == true,
                        choiceLimit,
                        outcomeGroups,
                        choiceGroups
                    )
                );
            }
            catch (Exception)
            {
                failures.Add(eventTemplate.Id);
            }
        }

        var eventFailureCount = failures.Count;
        var levelUps = CompileLevelUps(
            levelUpMap,
            cardMap,
            templates,
            failures,
            missingReferencedTemplates,
            out var levelUpFailureCount,
            out var unsupportedLevelUpPartCount
        );

        var snapshot = new EncounterPreviewSnapshot(
            events.OrderBy(plan => plan.TemplateId),
            templates.Values.OrderBy(plan => plan.TemplateId),
            levelUps.OrderBy(plan => plan.Level),
            new EventPreviewCoverage(
                eventFailureCount,
                levelUpFailureCount,
                unsupportedLevelUpPartCount,
                missingReferencedTemplates.Count
            )
        );
        return new EncounterPreviewCompileResult(snapshot, failures.OrderBy(id => id).ToArray());
    }

    private void TryAddReferencedTemplate(
        Guid templateId,
        IReadOnlyDictionary<Guid, ITCard> cardMap,
        Dictionary<Guid, EncounterPreviewTemplatePlan> templates,
        HashSet<Guid> failures,
        HashSet<Guid> missingReferencedTemplates
    )
    {
        if (templateId == Guid.Empty || templates.ContainsKey(templateId))
            return;
        if (!cardMap.TryGetValue(templateId, out var card) || card is not TCardBase template)
        {
            missingReferencedTemplates.Add(templateId);
            return;
        }

        try
        {
            JToken? preparedToken = null;
            if (
                template.Type == ECardType.EventEncounter
                || template.Type == ECardType.EncounterStep
            )
            {
                preparedToken = _prepareToken(template);
            }
            TryAddTemplate(template, preparedToken, templates, failures);
        }
        catch (Exception)
        {
            failures.Add(templateId);
        }
    }

    private void TryAddLevelUpReferencedTemplate(
        Guid templateId,
        IReadOnlyDictionary<Guid, ITCard> cardMap,
        Dictionary<Guid, EncounterPreviewTemplatePlan> templates,
        HashSet<Guid> failures,
        HashSet<Guid> missingReferencedTemplates
    ) =>
        TryAddReferencedTemplate(
            templateId,
            cardMap,
            templates,
            failures,
            missingReferencedTemplates
        );

    private List<LevelUpPreviewPlan> CompileLevelUps(
        IReadOnlyDictionary<int, TLevelUp> levelUpMap,
        IReadOnlyDictionary<Guid, ITCard> cardMap,
        Dictionary<Guid, EncounterPreviewTemplatePlan> templates,
        HashSet<Guid> failures,
        HashSet<Guid> missingReferencedTemplates,
        out int levelUpFailureCount,
        out int unsupportedPartCount
    )
    {
        var plans = new List<LevelUpPreviewPlan>();
        levelUpFailureCount = 0;
        unsupportedPartCount = 0;

        foreach (var (key, levelUp) in levelUpMap.OrderBy(pair => pair.Key))
        {
            try
            {
                var level = checked((int)levelUp.Level);
                if (key != level)
                {
                    levelUpFailureCount++;
                    continue;
                }

                var groups = new List<LevelUpPreviewGroup>();
                var isRandomSelection = false;
                if (levelUp.Rewards is TSpawnContextQuery query)
                {
                    isRandomSelection = query.SelectionMethod == ESpawnSelectionMethod.Random;
                    foreach (var group in query.Groups)
                    {
                        var heroConditions = new List<LevelUpPreviewHeroCondition>();
                        var skipBoardConditionalGroup = false;
                        if (group.Prerequisites != null)
                        {
                            foreach (var prerequisite in group.Prerequisites)
                            {
                                switch (prerequisite)
                                {
                                    case TPrerequisiteCardCount:
                                        skipBoardConditionalGroup = true;
                                        unsupportedPartCount++;
                                        break;
                                    case TPrerequisiteRun
                                    {
                                        Conditions: TRunConditionalPlayerHero heroCondition
                                    }:
                                        heroConditions.Add(
                                            new LevelUpPreviewHeroCondition(
                                                heroCondition.Heroes,
                                                heroCondition.Operator.ToString()
                                            )
                                        );
                                        break;
                                    default:
                                        unsupportedPartCount++;
                                        break;
                                }
                            }
                        }
                        if (skipBoardConditionalGroup)
                            continue;

                        var ids = new List<Guid>();
                        foreach (var filter in group.Filters)
                        {
                            if (filter is TSpawnFilterIdList idList)
                                ids.AddRange(idList.Ids);
                            else
                                unsupportedPartCount++;
                        }
                        if (ids.Count == 0)
                            continue;

                        var limit = 1;
                        if (group.Limit is TFixedValue fixedLimit)
                            limit = Math.Max(1, checked((int)fixedLimit.Value));
                        else if (group.Limit != null)
                            unsupportedPartCount++;

                        foreach (var id in ids)
                        {
                            TryAddLevelUpReferencedTemplate(
                                id,
                                cardMap,
                                templates,
                                failures,
                                missingReferencedTemplates
                            );
                        }
                        groups.Add(
                            new LevelUpPreviewGroup(group.RandomWeight, limit, ids, heroConditions)
                        );
                    }
                }
                else
                {
                    unsupportedPartCount++;
                }

                if (levelUp.TutorialRewards != null)
                    unsupportedPartCount++;

                plans.Add(
                    new LevelUpPreviewPlan(
                        level,
                        checked((int)levelUp.HealthIncrease),
                        isRandomSelection,
                        groups
                    )
                );
            }
            catch (Exception)
            {
                levelUpFailureCount++;
            }
        }

        return plans;
    }

    private static bool TryAddTemplate(
        TCardBase template,
        JToken? preparedToken,
        Dictionary<Guid, EncounterPreviewTemplatePlan> templates,
        HashSet<Guid> failures
    )
    {
        if (templates.ContainsKey(template.Id))
            return true;

        try
        {
            EncounterRewardFilter? rewardFilter = null;
            if (
                template.Type == ECardType.EventEncounter
                || template.Type == ECardType.EncounterStep
            )
            {
                rewardFilter = EncounterStructuredParser.TryParseRewardFilterWithPreparedToken(
                    template,
                    preparedToken
                );
            }

            var localization = template.Localization;
            templates.Add(
                template.Id,
                new EncounterPreviewTemplatePlan(
                    template.Id,
                    Classify(template.Type),
                    template.Heroes?.OrderBy(hero => (int)hero).ToArray() ?? Array.Empty<EHero>(),
                    template.InternalName,
                    new EncounterPreviewLocalizedText(
                        localization?.Title?.Key,
                        localization?.Title?.Text
                    ),
                    new EncounterPreviewLocalizedText(
                        localization?.Description?.Key,
                        localization?.Description?.Text
                    ),
                    EventPreviewLocalization.CaptureAbilityValues(template),
                    rewardFilter
                )
            );
            return true;
        }
        catch (Exception)
        {
            failures.Add(template.Id);
            return false;
        }
    }

    private static EncounterPreviewTemplateKind Classify(ECardType type) =>
        type switch
        {
            ECardType.EventEncounter => EncounterPreviewTemplateKind.Event,
            ECardType.EncounterStep => EncounterPreviewTemplateKind.EncounterStep,
            ECardType.CombatEncounter => EncounterPreviewTemplateKind.CombatEncounter,
            ECardType.Skill => EncounterPreviewTemplateKind.Skill,
            ECardType.Item => EncounterPreviewTemplateKind.Item,
            _ => EncounterPreviewTemplateKind.Other,
        };
}
