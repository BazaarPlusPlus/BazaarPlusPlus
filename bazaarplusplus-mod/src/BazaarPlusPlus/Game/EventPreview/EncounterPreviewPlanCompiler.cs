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
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.GameInterop.Cards;
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

    public int LevelUpFailureCount => Snapshot.Coverage.LevelUpFailureCount;
}

internal sealed class EncounterPreviewPlanCompiler
{
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
                if (
                    TryAddTemplate(eventTemplate, templates) is not { } token
                    || EncounterStructuredParser.Reader.ReadEvent(token) is not { } eventPlan
                )
                {
                    failures.Add(eventTemplate.Id);
                    continue;
                }

                foreach (var group in eventPlan.OutcomeGroups)
                foreach (var id in group.Ids)
                    TryAddReferencedTemplate(
                        id,
                        cardMap,
                        templates,
                        failures,
                        missingReferencedTemplates
                    );
                foreach (var group in eventPlan.ChoiceGroups)
                foreach (var member in group.Members)
                    TryAddReferencedTemplate(
                        member.TemplateId,
                        cardMap,
                        templates,
                        failures,
                        missingReferencedTemplates
                    );

                events.Add(eventPlan);
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
            if (TryAddTemplate(template, templates) == null)
                failures.Add(templateId);
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
                                                ToHeroFacets(heroCondition.Heroes),
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

    // Reads the template's plan off its game-serialized JSON, once, and returns that JSON; null
    // when it does not serialize or read.
    private static JObject? TryAddTemplate(
        TCardBase template,
        Dictionary<Guid, EncounterPreviewTemplatePlan> templates
    )
    {
        if (EncounterStructuredParser.TryPrepareToken(template) is not JObject token)
            return null;
        if (templates.ContainsKey(template.Id))
            return token;
        if (
            EncounterStructuredParser.Reader.ReadTemplate(
                token,
                EventPreviewGameText.CaptureAbilityValues(template)
            )
            is not { } plan
        )
            return null;
        templates.Add(template.Id, plan);
        return token;
    }

    // Game order, then by name onto the facet (ADR-0015): the legacy and current Dragons ids
    // fold into one facet, and an unmapped hero drops out.
    private static IReadOnlyList<CardHero> ToHeroFacets(IEnumerable<EHero>? heroes)
    {
        if (heroes == null)
            return Array.Empty<CardHero>();
        var result = new List<CardHero>();
        foreach (var hero in heroes.OrderBy(hero => (int)hero))
        {
            if (DesktopCardFacets.Hero.TryToFacet(hero, out var facet) && !result.Contains(facet))
                result.Add(facet);
        }
        return result;
    }
}
