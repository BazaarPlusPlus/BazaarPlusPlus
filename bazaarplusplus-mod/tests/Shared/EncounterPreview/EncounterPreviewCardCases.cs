#nullable enable
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.TestSupport.EncounterPreview;

/// <summary>
/// One table of static-data cards and the plans Core's card reader must read off them. The Android
/// host reads each card's JSON as it is; the desktop reads the JSON the game's serializer writes
/// for the typed template, so both hosts are held to the same expectations.
/// </summary>
internal static class EncounterPreviewCardCases
{
    private static readonly Lazy<JArray> Cases = new(() =>
        (JArray)
            JObject.Parse(TestInputs.Fixture("tests/Shared/fixtures/encounter-preview-cards.json"))[
                "cases"
            ]!
    );

    public static IEnumerable<object[]> Names() =>
        Cases.Value.Select(entry => new object[] { (string)entry["name"]! });

    public static JObject Card(string name) => (JObject)Case(name)["card"]!;

    public static JObject Expected(string name) => (JObject)Case(name)["expect"]!;

    /// <summary>What a host's preview can show of the card's plans.</summary>
    public static JObject Describe(
        EncounterPreviewTemplatePlan template,
        EncounterPreviewEventPlan? plan
    ) =>
        new()
        {
            ["kind"] = template.Kind.ToString(),
            ["heroes"] = new JArray(template.Heroes.Select(hero => hero.ToString())),
            ["reward"] = Reward(template.RewardFilter),
            ["event"] =
                plan == null
                    ? JValue.CreateNull()
                    : new JObject
                    {
                        ["merchant"] = plan.Merchant is { } merchant
                            ? new JObject
                            {
                                ["fixedTier"] = merchant.FixedTier is { } tier
                                    ? tier.ToString()
                                    : JValue.CreateNull(),
                                ["usesDayDistribution"] = merchant.UsesDayDistribution,
                            }
                            : JValue.CreateNull(),
                        ["random"] = plan.IsRandomSelectionEvent,
                        ["choiceLimit"] = plan.ChoiceLimit,
                        ["choices"] = new JArray(
                            plan.ChoiceGroups.Select(group => new JObject
                            {
                                ["randomPool"] = group.IsRandomPool,
                                ["day"] = Day(group.DayCondition),
                                ["members"] = new JArray(
                                    group.Members.Select(member => new JObject
                                    {
                                        ["id"] = member.TemplateId.ToString(),
                                        ["requirements"] = Requirements(member.Requirements),
                                    })
                                ),
                            })
                        ),
                        ["outcomes"] = new JArray(
                            plan.OutcomeGroups.Select(group => new JObject
                            {
                                ["weight"] = group.Weight,
                                ["ids"] = new JArray(group.Ids.Select(id => id.ToString())),
                                ["pools"] = new JArray(
                                    group.QueryPools.Select(pool => Reward(pool.Filter))
                                ),
                                ["requirements"] = Requirements(group.Requirements),
                                ["day"] = Day(group.DayCondition),
                            })
                        ),
                    },
        };

    private static JObject Case(string name) =>
        (JObject)Cases.Value.Single(entry => (string)entry["name"]! == name);

    private static JToken Reward(EncounterRewardFilter? filter) =>
        filter == null
            ? JValue.CreateNull()
            : new JObject
            {
                ["cardType"] = filter.CardType.ToString(),
                ["summary"] = filter.FilterSummary,
                ["quantity"] = filter.Quantity,
                ["tiers"] = new JArray(filter.Tiers.Select(tier => tier.ToString())),
                ["keywords"] = new JArray(filter.Keywords.Select(keyword => keyword.ToString())),
                ["usesDayTierTable"] = filter.UsesDayTierTable,
                ["usesDayTierDistribution"] = filter.UsesDayTierDistribution,
            };

    private static JArray Requirements(IEnumerable<EncounterCardRequirement> requirements) =>
        new(
            requirements.Select(requirement => new JObject
            {
                ["ids"] = new JArray(requirement.Ids.Select(id => id.ToString())),
                ["tags"] = new JArray(
                    requirement.TagCandidateGroups.Select(group => new JArray(group))
                ),
                ["comparison"] = requirement.Comparison,
                ["amount"] = requirement.Amount,
            })
        );

    private static JToken Day(EncounterDayCondition? condition) =>
        condition is not { } day
            ? JValue.CreateNull()
            : new JArray(
                new[] { day }
                    .Concat(day.AdditionalConditions)
                    .Select(part => $"{part.Comparison} {part.Day}")
            );
}
