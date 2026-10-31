#nullable enable
using BazaarPlusPlus.Core.Cards;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.EventPreview;

/// <summary>Choice/outcome structure shared by hosts; native enum names and pool constraints are host adapters.</summary>
public sealed class EncounterChoicePlanParser(
    Func<string, string?, string?> normalize,
    Func<JToken, JToken, JToken, int?, EncounterRewardFilter?> rewardPool
)
{
    private string? Normalize(string kind, string? raw) => normalize(kind, raw);

    /// <summary>
    /// A merchant event's quality rule, read off its JSON as the desktop's
    /// <c>EncounterMerchantTierResolver</c> reads the typed template: no query spawn context or no
    /// behaviours sells by the day's distribution; a <c>TSpawnBehaviorTier</c> that is not an
    /// exclusion and names one tier pins it; an enabled <c>TSpawnBehaviorIgnoreTierTable</c>
    /// without a pinned tier leaves no rule. The tier goes through the host's <c>"Tier"</c>
    /// normalization, since runtime objects serialize enums numerically.
    /// </summary>
    public EncounterMerchantTierRule TryParseMerchantTierRule(JToken? card)
    {
        if (
            card?.SelectToken("SelectionContext.SpawnContext") is not JObject spawnContext
            || !TypeIs(spawnContext, "TSpawnContextQuery", whenUntyped: true)
            || spawnContext["Behaviors"] is not JArray behaviors
        )
            return EncounterMerchantTierRule.DayDistribution;

        var pinned = false;
        CardTier? fixedTier = null;
        var ignoresDayTable = false;
        foreach (var behavior in behaviors.OfType<JObject>())
        {
            if (
                TypeIs(behavior, "TSpawnBehaviorIgnoreTierTable", whenUntyped: false)
                && ReadFlag(behavior["IgnoreTierTable"])
            )
                ignoresDayTable = true;
            if (
                TypeIs(behavior, "TSpawnBehaviorTier", whenUntyped: false)
                && !ReadFlag(behavior["IsNot"])
                && behavior["Tiers"] is JArray { Count: 1 } tiers
            )
            {
                pinned = true;
                fixedTier = ReadTier(tiers[0]);
            }
        }

        // A pinned tier with no facet is still pinned: neither a fixed tier line nor the day
        // distribution describes it.
        return pinned
            ? new EncounterMerchantTierRule(fixedTier, UsesDayDistribution: false)
            : new EncounterMerchantTierRule(null, UsesDayDistribution: !ignoresDayTable);
    }

    /// <summary>
    /// A static-data <c>level_ups</c> row's rewards, read off its JSON as the desktop's
    /// <c>EncounterPreviewPlanCompiler.CompileLevelUps</c> reads the typed <c>TLevelUp</c>: a
    /// group gated on board cards (<c>TPrerequisiteCardCount</c>) is left out, a
    /// <c>TRunConditionalPlayerHero</c> run prerequisite becomes a hero condition and any other
    /// prerequisite is ignored, only <c>TSpawnFilterIdList</c> filters contribute ids, a
    /// <c>TFixedValue</c> limit counts and any other limit is one. <paramref name="hero"/> maps a
    /// native hero name to its facet, null when it has none. Null when the row's
    /// <paramref name="key"/> disagrees with its level, as the compiler rejects it, or the row
    /// does not read.
    /// </summary>
    public LevelUpPreviewPlan? TryParseLevelUp(
        int key,
        JToken? levelUp,
        Func<string, CardHero?> hero
    )
    {
        try
        {
            var level = levelUp?["Level"]?.Value<int>();
            if (levelUp == null || level != key)
                return null;

            var groups = new List<LevelUpPreviewGroup>();
            var isRandomSelection = false;
            if (
                levelUp["Rewards"] is JObject rewards
                && TypeIs(rewards, "TSpawnContextQuery", whenUntyped: true)
            )
            {
                var selection = rewards["SelectionMethod"]?.ToString();
                isRandomSelection =
                    (Normalize("SelectionMethod", selection) ?? selection) == "Random";
                if (rewards["Groups"] is JArray groupArray)
                    foreach (var group in groupArray.OfType<JObject>())
                        if (ReadLevelUpGroup(group, hero) is { } parsed)
                            groups.Add(parsed);
            }

            return new LevelUpPreviewPlan(
                key,
                checked((int)(levelUp["HealthIncrease"]?.Value<long>() ?? 0)),
                isRandomSelection,
                groups
            );
        }
        catch (Exception exception)
            when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private LevelUpPreviewGroup? ReadLevelUpGroup(JObject group, Func<string, CardHero?> hero)
    {
        var heroConditions = new List<LevelUpPreviewHeroCondition>();
        if (group["Prerequisites"] is JArray prerequisites)
            foreach (var prerequisite in prerequisites.OfType<JObject>())
            {
                if (TypeIs(prerequisite, "TPrerequisiteCardCount", whenUntyped: false))
                    return null;
                if (
                    TypeIs(prerequisite, "TPrerequisiteRun", whenUntyped: false)
                    && prerequisite["Conditions"] is JObject condition
                    && TypeIs(condition, "TRunConditionalPlayerHero", whenUntyped: false)
                )
                {
                    var heroes = new List<CardHero>();
                    if (condition["Heroes"] is JArray names)
                        foreach (var name in names)
                            if (hero(name.ToString()) is { } facet && !heroes.Contains(facet))
                                heroes.Add(facet);
                    var comparison = condition["Operator"]?.ToString();
                    heroConditions.Add(
                        new LevelUpPreviewHeroCondition(
                            heroes,
                            Normalize("ListComparison", comparison) ?? comparison ?? string.Empty
                        )
                    );
                }
            }

        var ids = new List<Guid>();
        if (group["Filters"] is JArray filters)
            foreach (var filter in filters.OfType<JObject>())
                if (TypeIs(filter, "TSpawnFilterIdList", whenUntyped: false))
                    ids.AddRange(ReadGuids(filter["Ids"]));
        if (ids.Count == 0)
            return null;

        var limit = 1;
        if (
            group["Limit"] is JObject limitToken
            && TypeIs(limitToken, "TFixedValue", whenUntyped: false)
            && limitToken["Value"] is { } value
        )
            limit = Math.Max(1, checked((int)value.Value<double>()));

        return new LevelUpPreviewGroup(ReadUInt(group["RandomWeight"]), limit, ids, heroConditions);
    }

    // "$type" names the short type, or the full one with its assembly after a comma.
    private static bool TypeIs(JObject token, string typeName, bool whenUntyped)
    {
        var type = token["$type"]?.ToString();
        if (string.IsNullOrWhiteSpace(type))
            return whenUntyped;
        var comma = type!.IndexOf(',');
        var name = (comma >= 0 ? type.Substring(0, comma) : type).Trim();
        return name == typeName || name.EndsWith("." + typeName, StringComparison.Ordinal);
    }

    private static bool ReadFlag(JToken? token) =>
        token != null
        && (
            token.Type == JTokenType.Boolean
                ? token.Value<bool>()
                : bool.TryParse(token.ToString(), out var parsed) && parsed
        );

    private CardTier? ReadTier(JToken token)
    {
        var raw = token.ToString();
        var name = Normalize("Tier", raw) ?? raw;
        return !long.TryParse(name, out _) && Enum.TryParse<CardTier>(name, true, out var tier)
            ? tier
            : null;
    }

    public IReadOnlyList<EncounterChoiceGroupData> TryParseEventChoiceGroups(JToken? source)
    {
        var token = source;
        if (token == null)
            return Array.Empty<EncounterChoiceGroupData>();

        var spawnContext = token.SelectToken("SelectionContext.SpawnContext") ?? token;
        var result = new List<EncounterChoiceGroupData>();
        var seen = new HashSet<Guid>();
        foreach (var groupsToken in FindProperties(spawnContext, "Groups"))
        {
            if (groupsToken is not JArray groups)
                continue;
            foreach (var group in groups)
            {
                var members = new List<EncounterStepReference>();
                AppendStepReferencesFromGroup(group, members, seen);
                if (members.Count == 0)
                    continue;

                var isRandomPool =
                    Normalize("SelectionMethod", group["SelectionMethod"]?.ToString()) == "Random";
                result.Add(
                    new EncounterChoiceGroupData(
                        isRandomPool,
                        members,
                        ReadDayCondition(group["Prerequisites"])
                    )
                );
            }
        }

        return result;
    }

    public int? TryParseEventChoiceLimit(JToken? source)
    {
        var token = source;
        if (token == null)
            return null;

        var spawnContext = token.SelectToken("SelectionContext.SpawnContext") ?? token;
        return ReadQuantity(spawnContext);
    }

    public bool TryParseEventOutcomeGroups(
        JToken? source,
        out IReadOnlyList<EncounterOutcomeGroupData> groups
    )
    {
        groups = Array.Empty<EncounterOutcomeGroupData>();
        var token = source;
        if (token == null)
            return false;

        var spawnContext = token.SelectToken("SelectionContext.SpawnContext");
        if (spawnContext == null)
            return false;
        // Runtime objects serialize enums numerically; raw JSON uses names.
        if (Normalize("SelectionMethod", spawnContext["SelectionMethod"]?.ToString()) != "Random")
            return false;

        var result = new List<EncounterOutcomeGroupData>();
        if (spawnContext["Groups"] is JArray groupArray)
        {
            foreach (var group in groupArray)
            {
                var ids = new List<Guid>();
                var queryPools = new List<EncounterOutcomeQueryPool>();
                if (group["Filters"] is JArray filters)
                    foreach (var filter in filters)
                    {
                        foreach (var id in ReadGuids(filter["Ids"]))
                            if (!ids.Contains(id))
                                ids.Add(id);

                        // TSpawnFilterQuery: a dynamic pool instead of fixed ids
                        // (e.g. Treasure Chest's 9-weight small/medium item pool).
                        // Dropping these groups would corrupt the probability
                        // normalization of everything else, so they are kept as
                        // pool summaries even when the constraints don't parse.
                        var constraintsToken = filter["Constraints"];
                        if (constraintsToken != null && constraintsToken.Type != JTokenType.Null)
                        {
                            // Constraints arrive as a single (possibly ConstraintAnd)
                            // object; enumerate to its leaf constraint objects.
                            var quantity = ReadQuantity(group);
                            queryPools.Add(
                                new EncounterOutcomeQueryPool(
                                    rewardPool(spawnContext, group, filter, quantity),
                                    quantity
                                )
                            );
                        }
                    }
                if (ids.Count == 0 && queryPools.Count == 0)
                    continue;

                var weight = ReadUInt(group["RandomWeight"]);
                result.Add(
                    new EncounterOutcomeGroupData(
                        weight,
                        ids,
                        queryPools,
                        ReadCardRequirements(group["Prerequisites"]),
                        ReadDayCondition(group["Prerequisites"])
                    )
                );
            }
        }

        groups = result;
        return result.Count > 0;
    }

    private uint ReadUInt(JToken? token) =>
        token != null && uint.TryParse(token.ToString(), out var value) ? value : 0;

    private EncounterDayCondition? ReadDayCondition(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return null;

        var conditions = new List<EncounterDayCondition>();
        foreach (var obj in EnumerateObjects(token))
        {
            if (obj["CurrentDay"] == null)
                continue;
            if (!int.TryParse(obj["CurrentDay"]!.ToString(), out var day))
                continue;
            var comparison =
                Normalize("Comparison", obj["ComparisonOperator"]?.ToString()) ?? "Equal";
            conditions.Add(new EncounterDayCondition(day, comparison));
        }
        return conditions.Count == 0
            ? null
            : new EncounterDayCondition(
                conditions[0].Day,
                conditions[0].Comparison,
                conditions.Skip(1).ToArray()
            );
    }

    private void AppendStepReferencesFromGroup(
        JToken group,
        List<EncounterStepReference> result,
        HashSet<Guid> seen
    )
    {
        var groupRequirements = ReadCardRequirements(group["Prerequisites"]);
        var filters = group["Filters"] as JArray;
        if (filters == null)
            return;

        foreach (var filter in filters)
        {
            var requirements = new List<EncounterCardRequirement>(groupRequirements);
            requirements.AddRange(ReadCardRequirements(filter["Prerequisites"]));

            foreach (var id in ReadGuids(filter["Ids"]))
            {
                if (!seen.Add(id))
                    continue;
                result.Add(new EncounterStepReference(id, requirements));
            }
        }
    }

    public IReadOnlyList<EncounterCardRequirement> ReadCardRequirements(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return Array.Empty<EncounterCardRequirement>();

        var requirements = new List<EncounterCardRequirement>();
        if (token is JArray prerequisites)
        {
            foreach (var prerequisite in prerequisites)
                if (TryReadCardRequirement(prerequisite) is { } requirement)
                    requirements.Add(requirement);
        }
        else if (TryReadCardRequirement(token) is { } requirement)
        {
            requirements.Add(requirement);
        }
        return requirements;
    }

    private EncounterCardRequirement? TryReadCardRequirement(JToken entry)
    {
        if (entry is not JObject obj)
            return null;

        var comparisonToken = obj["Comparison"];
        var amountToken = obj["Amount"];
        if (comparisonToken == null || amountToken == null)
            return null;
        // Runtime objects serialize enums numerically; raw JSON uses names.
        var comparison = Normalize("Comparison", comparisonToken.ToString());
        if (comparison == null)
            return null;
        if (!int.TryParse(amountToken.ToString(), out var amount))
            return null;

        var tagGroups = new List<IReadOnlyList<string>>();
        var tagOperator = "Any";
        foreach (var conditional in EnumerateObjects(entry))
        {
            if (
                string.Equals(
                    conditional["IsNot"]?.ToString(),
                    "true",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return null;
            // Tier conditionals (Grandmaster's "no Bronze/Silver/Gold skills") need
            // per-card tier data the inventory snapshot doesn't carry.
            if (conditional["Tiers"] is JArray { Count: > 0 })
                return null;
            if (conditional["Tags"] is not JArray tags || tags.Count == 0)
                continue;

            if (
                Normalize("ListComparison", conditional["Operator"]?.ToString()) is
                { } parsedOperator
            )
                tagOperator = parsedOperator;

            foreach (var tag in tags)
            {
                var candidates = ResolveTagCandidates(tag, conditional);
                if (candidates.Count > 0)
                    tagGroups.Add(candidates);
            }
        }

        var ids = ReadPrerequisiteIds(entry);
        if (ids.Count == 0 && tagGroups.Count == 0)
            return null;
        // A conditional mixing id and tag terms would need the source's exact
        // and/or structure; skip rather than misjudge.
        if (ids.Count > 0 && tagGroups.Count > 0)
            return null;

        return new EncounterCardRequirement(
            ids,
            tagGroups,
            tagOperator,
            comparison.ToString(),
            amount
        );
    }

    private IReadOnlyList<string> ResolveTagCandidates(JToken tag, JObject conditional)
    {
        var raw = tag.ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();
        if (!long.TryParse(raw, out _))
            return new[] { raw };

        var type = conditional["$type"]?.ToString();
        var candidates = new List<string>();
        var allowHidden = type == null || type.Contains("HiddenTag");
        var allowCard =
            type == null || (type.Contains("ConditionalTag") && !type.Contains("HiddenTag"));
        if (allowHidden && Normalize("HiddenTag", raw) is { } hiddenTag)
            candidates.Add(hiddenTag);
        if (allowCard && Normalize("Tag", raw) is { } cardTag)
            candidates.Add(cardTag);
        return candidates;
    }

    private IReadOnlyList<Guid> ReadGuids(JToken? token)
    {
        var result = new List<Guid>();
        if (token == null)
            return result;

        if (token is JArray array)
        {
            foreach (var child in array)
                AddGuid(result, child);
            return result;
        }

        AddGuid(result, token);
        return result;
    }

    private void AddGuid(List<Guid> result, JToken token)
    {
        if (Guid.TryParse(token.ToString(), out var id))
            result.Add(id);
    }

    private IReadOnlyList<Guid> ReadPrerequisiteIds(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return Array.Empty<Guid>();

        var ids = new List<Guid>();
        foreach (var obj in EnumerateObjects(token))
        {
            foreach (var property in obj.Properties())
            {
                if (!LooksLikeIdProperty(property.Name))
                    continue;
                foreach (var id in ReadGuids(property.Value))
                    if (!ids.Contains(id))
                        ids.Add(id);
            }
        }

        return ids;
    }

    private bool LooksLikeIdProperty(string name) =>
        string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Ids", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("Ids", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("Id", StringComparison.OrdinalIgnoreCase);

    private IEnumerable<JObject> EnumerateObjects(JToken token)
    {
        if (token is JObject obj)
            yield return obj;

        foreach (var child in token.Children())
        {
            foreach (var nested in EnumerateObjects(child))
                yield return nested;
        }
    }

    private IEnumerable<JToken> FindProperties(JToken token, string propertyName)
    {
        foreach (var obj in EnumerateObjects(token))
        {
            if (obj.TryGetValue(propertyName, StringComparison.OrdinalIgnoreCase, out var value))
                yield return value;
        }
    }

    private int? ReadQuantity(JToken spawnContext)
    {
        var value = spawnContext.SelectToken("Limit.Value") ?? spawnContext.SelectToken("Limit");
        if (value == null)
            return null;
        if (value.Type == JTokenType.Integer)
            return value.Value<int>();
        if (value.Type == JTokenType.Float)
            return (int)Math.Round(value.Value<double>());
        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }
}
