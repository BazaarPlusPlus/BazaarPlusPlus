#nullable enable
using System.Collections;
using System.Reflection;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Spawning;
using BazaarPlusPlus.Game.Encounters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.EventPreview;

internal static class EncounterStructuredParser
{
    public static IReadOnlyList<EncounterStepReference> TryParseEventStepReferences(object? source)
    {
        var result = new List<EncounterStepReference>();
        foreach (var group in TryParseEventChoiceGroups(source))
            result.AddRange(group.Members);
        return result;
    }

    // Choice-event spawn groups with their boundaries preserved: a group with its own
    // SelectionMethod=Random is a pool the event rolls members from (Advanced
    // Training's 16 trainings, Epic Battle's 14 monsters) rather than a fixed list
    // of always-offered steps.
    public static IReadOnlyList<EncounterChoiceGroupData> TryParseEventChoiceGroups(object? source)
    {
        var token = ToToken(source);
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
                    Enum.TryParse<ESpawnSelectionMethod>(
                        group["SelectionMethod"]?.ToString(),
                        ignoreCase: true,
                        out var selectionMethod
                    )
                    && selectionMethod == ESpawnSelectionMethod.Random;
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

    public static EncounterRewardFilter? TryParseRewardFilter(object? source)
    {
        return TryParseRewardFilterCore(source, () => ToToken(source));
    }

    internal static EncounterRewardFilter? TryParseRewardFilterWithPreparedToken(
        object? source,
        JToken? preparedToken
    )
    {
        return TryParseRewardFilterCore(source, () => preparedToken);
    }

    internal static JToken? TryPrepareToken(object? source) => ToToken(source);

    private static EncounterRewardFilter? TryParseRewardFilterCore(
        object? source,
        Func<JToken?> fallbackToken
    )
    {
        foreach (var runtimeAction in EnumerateRuntimeDealCardActions(source))
        {
            var reward = TryParseRuntimeDealCardAction(runtimeAction);
            if (reward != null)
                return reward;
        }

        var token = fallbackToken();
        if (token == null)
            return null;

        foreach (var action in EnumerateObjects(token))
        {
            if (!IsType(action, "TActionGameDealCards"))
                continue;

            var spawnContext = action["SpawnContext"];
            if (spawnContext == null)
                continue;

            var reward = TryParseTokenSpawnContext(spawnContext);
            if (reward != null)
                return reward;
        }

        return null;
    }

    public static bool HasDealCardRewardAction(object? source)
    {
        foreach (var _ in EnumerateRuntimeDealCardActions(source))
            return true;

        var token = ToToken(source);
        if (token == null)
            return false;

        foreach (var action in EnumerateObjects(token))
            if (IsType(action, "TActionGameDealCards"))
                return true;
        return false;
    }

    public static bool HasDealCardRewardConstraints(object? source)
    {
        foreach (var runtimeAction in EnumerateRuntimeDealCardActions(source))
        {
            var spawnContext = ReadMemberValue(runtimeAction, "SpawnContext");
            if (spawnContext != null && ParseRuntimeSpawnConstraints(spawnContext).Count > 0)
                return true;
        }

        var token = ToToken(source);
        if (token == null)
            return false;

        foreach (var action in EnumerateObjects(token))
        {
            if (!IsType(action, "TActionGameDealCards"))
                continue;

            var spawnContext = action["SpawnContext"];
            if (spawnContext != null && ParseTokenSpawnConstraints(spawnContext).Count > 0)
                return true;
        }

        return false;
    }

    // The number of choices the event actually presents (SelectionContext spawn limit);
    // groups beyond it never spawn even when their prerequisites hold.
    public static int? TryParseEventChoiceLimit(object? source)
    {
        var token = ToToken(source);
        if (token == null)
            return null;

        var spawnContext = token.SelectToken("SelectionContext.SpawnContext") ?? token;
        return ReadQuantity(spawnContext);
    }

    // Random-outcome events (outer SelectionMethod=Random) roll one weighted group
    // instead of presenting choices; returns the group data for probability display.
    public static bool TryParseEventOutcomeGroups(
        object? source,
        out IReadOnlyList<EncounterOutcomeGroupData> groups
    )
    {
        groups = Array.Empty<EncounterOutcomeGroupData>();
        var token = ToToken(source);
        if (token == null)
            return false;

        var spawnContext = token.SelectToken("SelectionContext.SpawnContext");
        if (spawnContext == null)
            return false;
        // Runtime objects serialize enums numerically; raw JSON uses names.
        if (
            !Enum.TryParse<ESpawnSelectionMethod>(
                spawnContext["SelectionMethod"]?.ToString(),
                ignoreCase: true,
                out var selectionMethod
            )
            || selectionMethod != ESpawnSelectionMethod.Random
        )
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
                            var constraints = new SpawnConstraints();
                            foreach (var constraintObject in EnumerateObjects(constraintsToken))
                                if (LooksLikeTokenConstraintObject(constraintObject))
                                    constraints.AddTokenConstraintObject(constraintObject);
                            var quantity = ReadQuantity(group);
                            queryPools.Add(
                                new EncounterOutcomeQueryPool(
                                    constraints.ToRewardFilter(quantity),
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

    private static uint ReadUInt(JToken? token) =>
        token != null && uint.TryParse(token.ToString(), out var value) ? value : 0;

    private static EncounterDayCondition? ReadDayCondition(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return null;

        foreach (var obj in EnumerateObjects(token))
        {
            if (obj["CurrentDay"] == null)
                continue;
            if (!int.TryParse(obj["CurrentDay"]!.ToString(), out var day))
                continue;
            var comparison = Enum.TryParse<EComparisonOperator>(
                obj["ComparisonOperator"]?.ToString(),
                ignoreCase: true,
                out var parsedComparison
            )
                ? parsedComparison.ToString()
                : "Equal";
            return new EncounterDayCondition(day, comparison);
        }
        return null;
    }

    private static void AppendStepReferencesFromGroup(
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

    // Card-count ownership prerequisites (TPrerequisiteCardCount): all must be
    // satisfied. Prerequisites without Comparison/Amount (run/day/hero conditions)
    // yield nothing here; shapes that cannot be evaluated against the inventory
    // snapshot (negated or tier-based conditionals) are skipped entirely — an
    // unknown prerequisite must count as met, never as a guess.
    internal static IReadOnlyList<EncounterCardRequirement> ReadCardRequirements(JToken? token)
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

    private static EncounterCardRequirement? TryReadCardRequirement(JToken entry)
    {
        if (entry is not JObject obj)
            return null;

        var comparisonToken = obj["Comparison"];
        var amountToken = obj["Amount"];
        if (comparisonToken == null || amountToken == null)
            return null;
        // Runtime objects serialize enums numerically; raw JSON uses names.
        if (
            !Enum.TryParse<EComparisonOperator>(
                comparisonToken.ToString(),
                ignoreCase: true,
                out var comparison
            )
        )
            return null;
        if (!int.TryParse(amountToken.ToString(), out var amount))
            return null;

        var tagGroups = new List<IReadOnlyList<string>>();
        var tagOperator = nameof(EListComparisonOperator.Any);
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
                conditional["Operator"] is { } operatorToken
                && Enum.TryParse<EListComparisonOperator>(
                    operatorToken.ToString(),
                    ignoreCase: true,
                    out var parsedOperator
                )
            )
                tagOperator = parsedOperator.ToString();

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

    // A tag token is a name in raw JSON but a bare number from runtime objects
    // (JToken.FromObject drops $type and serializes enums numerically). Numbers are
    // mapped back through the conditional's declared enum when the $type survives,
    // otherwise through both plausible tag enums.
    private static IReadOnlyList<string> ResolveTagCandidates(JToken tag, JObject conditional)
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
        if (allowHidden && Enum.TryParse<EHiddenTag>(raw, out var hiddenTag))
            candidates.Add(hiddenTag.ToString());
        if (allowCard && Enum.TryParse<ECardTag>(raw, out var cardTag))
            candidates.Add(cardTag.ToString());
        return candidates;
    }

    private static EncounterRewardFilter? TryParseTokenSpawnContext(JToken spawnContext)
    {
        var merged = SpawnConstraints.TryMergeCompatible(ParseTokenSpawnConstraints(spawnContext));
        return merged?.ToRewardFilter(ReadQuantity(spawnContext));
    }

    private static EncounterRewardFilter? TryParseRuntimeDealCardAction(object action)
    {
        var spawnContext = ReadMemberValue(action, "SpawnContext");
        if (spawnContext == null)
            return null;

        var merged = SpawnConstraints.TryMergeCompatible(
            ParseRuntimeSpawnConstraints(spawnContext)
        );
        return merged?.ToRewardFilter(ReadRuntimeQuantity(spawnContext));
    }

    private static IReadOnlyList<SpawnConstraints> ParseTokenSpawnConstraints(JToken spawnContext)
    {
        var result = new List<SpawnConstraints>();
        var spawnContextConstraints = new SpawnConstraints();
        AddTokenBehaviors(spawnContextConstraints, spawnContext["Behaviors"]);
        foreach (var groupsToken in FindProperties(spawnContext, "Groups"))
        {
            if (groupsToken is not JArray groups)
                continue;

            foreach (var group in groups)
            {
                var groupConstraints = spawnContextConstraints.Clone();
                AddTokenConstraints(groupConstraints, group["Constraints"]);
                AddTokenBehaviors(groupConstraints, group["Behaviors"]);
                var filters = group["Filters"] as JArray;
                if (filters == null)
                    continue;

                foreach (var filter in filters)
                {
                    var constraints = groupConstraints.Clone();
                    var structuredConstraints = filter["Constraints"];
                    if (structuredConstraints != null)
                        AddTokenConstraints(constraints, structuredConstraints);
                    else if (filter is JObject filterObject)
                        constraints.AddTokenFilterProperties(filterObject);

                    if (constraints.HasAny)
                        result.Add(constraints);
                }
            }
        }

        if (result.Count > 0)
            return result;

        var fallback = spawnContextConstraints.Clone();
        foreach (var obj in EnumerateObjects(spawnContext))
        {
            if (LooksLikeTokenSpawnBehaviorObject(obj))
                fallback.AddTokenSpawnBehaviorObject(obj);
            else if (LooksLikeTokenConstraintObject(obj))
                fallback.AddTokenConstraintObject(obj);
            else
                fallback.AddTokenFilterProperties(obj);
        }

        return fallback.HasAny ? new[] { fallback } : Array.Empty<SpawnConstraints>();
    }

    private static IReadOnlyList<SpawnConstraints> ParseRuntimeSpawnConstraints(object spawnContext)
    {
        var result = new List<SpawnConstraints>();
        var spawnContextConstraints = new SpawnConstraints();
        foreach (var behavior in ReadMemberSequence(spawnContext, "Behaviors"))
            spawnContextConstraints.AddRuntimeSpawnBehaviorObject(behavior);

        foreach (var group in ReadMemberSequence(spawnContext, "Groups"))
        {
            var groupConstraints = spawnContextConstraints.Clone();
            foreach (var constraint in ReadMemberSequence(group, "Constraints"))
                groupConstraints.AddRuntimeConstraintObject(constraint);
            foreach (var behavior in ReadMemberSequence(group, "Behaviors"))
                groupConstraints.AddRuntimeSpawnBehaviorObject(behavior);

            foreach (var filter in ReadMemberSequence(group, "Filters"))
            {
                var constraints = groupConstraints.Clone();
                var structuredConstraints = ReadMemberSequence(filter, "Constraints");
                var addedStructuredConstraint = false;
                foreach (var constraint in structuredConstraints)
                {
                    constraints.AddRuntimeConstraintObject(constraint);
                    addedStructuredConstraint = true;
                }

                if (!addedStructuredConstraint)
                    constraints.AddRuntimeFilterProperties(filter);

                if (constraints.HasAny)
                    result.Add(constraints);
            }
        }

        if (result.Count > 0)
            return result;

        var fallback = spawnContextConstraints.Clone();
        fallback.AddRuntimeFilterProperties(spawnContext);
        return fallback.HasAny ? new[] { fallback } : Array.Empty<SpawnConstraints>();
    }

    private static void AddTokenConstraints(SpawnConstraints constraints, JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return;

        foreach (var constraintObject in EnumerateObjects(token))
            if (LooksLikeTokenConstraintObject(constraintObject))
                constraints.AddTokenConstraintObject(constraintObject);
    }

    private static void AddTokenBehaviors(SpawnConstraints constraints, JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return;

        foreach (var behaviorObject in EnumerateObjects(token))
            if (LooksLikeTokenSpawnBehaviorObject(behaviorObject))
                constraints.AddTokenSpawnBehaviorObject(behaviorObject);
    }

    private static int? ReadQuantity(JToken spawnContext)
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

    private static JToken? ToToken(object? source)
    {
        if (source == null)
            return null;
        try
        {
            return source switch
            {
                JToken token => token,
                string json when !string.IsNullOrWhiteSpace(json) => JToken.Parse(json),
                _ => JToken.FromObject(source),
            };
        }
        catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsType(JObject obj, string typeName)
    {
        var type = obj["$type"]?.ToString();
        return !string.IsNullOrWhiteSpace(type)
            && type!.EndsWith(typeName, StringComparison.Ordinal);
    }

    private static IEnumerable<JObject> EnumerateObjects(JToken token)
    {
        if (token is JObject obj)
            yield return obj;

        foreach (var child in token.Children())
        {
            foreach (var nested in EnumerateObjects(child))
                yield return nested;
        }
    }

    private static IEnumerable<JToken> FindProperties(JToken token, string propertyName)
    {
        foreach (var obj in EnumerateObjects(token))
        {
            if (obj.TryGetValue(propertyName, StringComparison.OrdinalIgnoreCase, out var value))
                yield return value;
        }
    }

    private static IReadOnlyList<Guid> ReadGuids(JToken? token)
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

    private static void AddGuid(List<Guid> result, JToken token)
    {
        if (Guid.TryParse(token.ToString(), out var id))
            result.Add(id);
    }

    private static IReadOnlyList<Guid> ReadPrerequisiteIds(JToken? token)
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

    private static bool LooksLikeIdProperty(string name) =>
        string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Ids", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("Ids", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("Id", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeTokenConstraintObject(JObject obj) =>
        obj.TryGetValue("IsNot", StringComparison.OrdinalIgnoreCase, out _)
        || !string.IsNullOrWhiteSpace(obj["$type"]?.ToString())
        || HasKnownTokenConstraintProperty(obj);

    private static bool LooksLikeTokenSpawnBehaviorObject(JObject obj)
    {
        var typeHint = NormalizeName(obj["$type"]?.ToString() ?? string.Empty);
        return typeHint.Contains("spawnbehavior");
    }

    private static IEnumerable<object> EnumerateRuntimeDealCardActions(object? source)
    {
        if (source == null || source is string || source is JToken)
            yield break;

        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var value in EnumerateRuntimeObjects(source, seen, depth: 0))
        {
            if (value.GetType().Name == "TActionGameDealCards")
                yield return value;
        }
    }

    private static IEnumerable<object> EnumerateRuntimeObjects(
        object value,
        HashSet<object> seen,
        int depth
    )
    {
        if (depth > 12 || IsRuntimeLeaf(value) || !seen.Add(value))
            yield break;

        yield return value;

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Value == null)
                    continue;
                foreach (var nested in EnumerateRuntimeObjects(entry.Value, seen, depth + 1))
                    yield return nested;
            }
            yield break;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var child in enumerable)
            {
                if (child == null)
                    continue;
                foreach (var nested in EnumerateRuntimeObjects(child, seen, depth + 1))
                    yield return nested;
            }
            yield break;
        }

        foreach (var memberValue in ReadRuntimeMemberValues(value))
        foreach (var nested in EnumerateRuntimeObjects(memberValue, seen, depth + 1))
            yield return nested;
    }

    private static IEnumerable<object> ReadRuntimeMemberValues(object value)
    {
        foreach (
            var property in value
                .GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        )
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
                continue;

            object? memberValue;
            try
            {
                memberValue = property.GetValue(value);
            }
            catch
            {
                continue;
            }

            if (memberValue != null && !IsRuntimeLeaf(memberValue))
                yield return memberValue;
        }

        foreach (
            var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public)
        )
        {
            object? memberValue;
            try
            {
                memberValue = field.GetValue(value);
            }
            catch
            {
                continue;
            }

            if (memberValue != null && !IsRuntimeLeaf(memberValue))
                yield return memberValue;
        }
    }

    private static bool IsRuntimeLeaf(object value)
    {
        var type = value.GetType();
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(Guid)
            || type == typeof(DateTime)
            || value is JToken;
    }

    private static object? ReadMemberValue(object source, string name)
    {
        var type = source.GetType();
        var property = type.GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase
        );
        if (property is { CanRead: true } && property.GetIndexParameters().Length == 0)
        {
            try
            {
                return property.GetValue(source);
            }
            catch
            {
                return null;
            }
        }

        var field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase
        );
        if (field == null)
            return null;

        try
        {
            return field.GetValue(source);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<object> ReadMemberSequence(object source, string name)
    {
        var value = ReadMemberValue(source, name);
        if (value == null || value is string)
            yield break;

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
                if (entry.Value != null)
                    yield return entry.Value;
            yield break;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var child in enumerable)
                if (child != null)
                    yield return child;
            yield break;
        }

        yield return value;
    }

    private static int? ReadRuntimeQuantity(object spawnContext)
    {
        var limit = ReadMemberValue(spawnContext, "Limit");
        if (limit == null)
            return null;

        var value = ReadMemberValue(limit, "Value") ?? limit;
        return value switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            float floatValue => (int)Math.Round(floatValue),
            double doubleValue => (int)Math.Round(doubleValue),
            decimal decimalValue => (int)Math.Round(decimalValue),
            _ => int.TryParse(value.ToString(), out var parsed) ? parsed : null,
        };
    }

    private sealed class SpawnConstraints
    {
        private ECardType? _cardType;
        private bool _usesDayTierTable = true;
        private bool _usesDayTierDistribution = true;
        private readonly List<ECardSize> _sizes = new();
        private readonly List<ECardSize> _excludedSizes = new();
        private readonly List<ETier> _tiers = new();
        private readonly List<ETier> _excludedTiers = new();
        private readonly List<ECardTag> _tags = new();
        private readonly List<ECardTag> _excludedTags = new();
        private readonly List<EHiddenTag> _keywords = new();
        private readonly List<EHiddenTag> _excludedKeywords = new();

        private static readonly ECardSize[] KnownSizes = new[]
        {
            ECardSize.Small,
            ECardSize.Medium,
            ECardSize.Large,
        };

        private static readonly ETier[] KnownTiers = new[]
        {
            ETier.Bronze,
            ETier.Silver,
            ETier.Gold,
            ETier.Diamond,
            ETier.Legendary,
        };

        public bool HasAny =>
            _cardType.HasValue
            || _sizes.Count > 0
            || _excludedSizes.Count > 0
            || _tiers.Count > 0
            || _excludedTiers.Count > 0
            || _tags.Count > 0
            || _excludedTags.Count > 0
            || _keywords.Count > 0
            || _excludedKeywords.Count > 0;

        // Weighted spawn groups of the same card type (e.g. a 95%/5% split) describe one
        // pool for display purposes: union the inclusive facets, keep only exclusions
        // every group agrees on. Different card types stay ambiguous.
        public static SpawnConstraints? TryMergeCompatible(
            IReadOnlyList<SpawnConstraints> candidates
        )
        {
            if (candidates.Count == 0)
                return null;
            if (candidates.Count == 1)
                return candidates[0];

            var first = candidates[0];
            if (first._cardType == null)
                return null;

            var merged = first.Clone();
            for (var i = 1; i < candidates.Count; i++)
            {
                var other = candidates[i];
                if (other._cardType != first._cardType)
                    return null;

                UnionInto(merged._sizes, other._sizes);
                UnionInto(merged._tiers, other._tiers);
                UnionInto(merged._tags, other._tags);
                UnionInto(merged._keywords, other._keywords);
                IntersectInto(merged._excludedSizes, other._excludedSizes);
                IntersectInto(merged._excludedTiers, other._excludedTiers);
                IntersectInto(merged._excludedTags, other._excludedTags);
                IntersectInto(merged._excludedKeywords, other._excludedKeywords);
                merged._usesDayTierTable = merged._usesDayTierTable && other._usesDayTierTable;
                merged._usesDayTierDistribution =
                    merged._usesDayTierDistribution && other._usesDayTierDistribution;
            }
            return merged;
        }

        private static void UnionInto<T>(List<T> target, List<T> other)
            where T : struct
        {
            foreach (var value in other)
                if (!target.Contains(value))
                    target.Add(value);
        }

        private static void IntersectInto<T>(List<T> target, List<T> other)
            where T : struct
        {
            target.RemoveAll(value => !other.Contains(value));
        }

        public SpawnConstraints Clone()
        {
            var clone = new SpawnConstraints
            {
                _cardType = _cardType,
                _usesDayTierTable = _usesDayTierTable,
                _usesDayTierDistribution = _usesDayTierDistribution,
            };
            clone._sizes.AddRange(_sizes);
            clone._excludedSizes.AddRange(_excludedSizes);
            clone._tiers.AddRange(_tiers);
            clone._excludedTiers.AddRange(_excludedTiers);
            clone._tags.AddRange(_tags);
            clone._excludedTags.AddRange(_excludedTags);
            clone._keywords.AddRange(_keywords);
            clone._excludedKeywords.AddRange(_excludedKeywords);
            return clone;
        }

        public void AddTokenFilterProperties(JObject obj)
        {
            foreach (var property in obj.Properties())
            {
                if (string.Equals(property.Name, "$type", StringComparison.Ordinal))
                    continue;

                var normalized = NormalizeName(property.Name);
                AddValues(
                    property.Name,
                    property.Value,
                    IsExcludedPropertyName(normalized),
                    string.Empty
                );
            }
        }

        public void AddTokenConstraintObject(JObject obj)
        {
            var typeHint = NormalizeName(obj["$type"]?.ToString() ?? string.Empty);
            var excluded = ReadBool(obj["IsNot"]);
            foreach (var property in obj.Properties())
            {
                if (
                    string.Equals(property.Name, "$type", StringComparison.Ordinal)
                    || string.Equals(property.Name, "IsNot", StringComparison.OrdinalIgnoreCase)
                )
                    continue;
                AddValues(property.Name, property.Value, excluded, typeHint);
            }
        }

        public void AddTokenSpawnBehaviorObject(JObject obj)
        {
            var typeHint = NormalizeName(obj["$type"]?.ToString() ?? string.Empty);
            ApplyTierTableBehavior(typeHint, obj);
            if (typeHint.Contains("spawnbehaviortier"))
                AddTokenSpawnBehaviorTier(obj);
        }

        public void AddRuntimeFilterProperties(object source)
        {
            foreach (
                var property in source
                    .GetType()
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            )
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    continue;
                object? value;
                try
                {
                    value = property.GetValue(source);
                }
                catch
                {
                    continue;
                }

                if (value == null)
                    continue;
                var token = ToToken(value);
                if (token != null)
                {
                    var normalized = NormalizeName(property.Name);
                    AddValues(
                        property.Name,
                        token,
                        IsExcludedPropertyName(normalized),
                        string.Empty
                    );
                }
            }
        }

        public void AddRuntimeConstraintObject(object source)
        {
            var typeHint = NormalizeName(source.GetType().Name);
            if (typeHint.Contains("constraintor"))
                return;

            if (typeHint.Contains("constraintand"))
            {
                foreach (var child in ReadMemberSequence(source, "Constraints"))
                    AddRuntimeConstraintObject(child);
                return;
            }

            var excluded = ReadRuntimeBool(ReadMemberValue(source, "IsNot"));
            foreach (
                var property in source
                    .GetType()
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            )
            {
                if (
                    !property.CanRead
                    || property.GetIndexParameters().Length > 0
                    || string.Equals(property.Name, "IsNot", StringComparison.OrdinalIgnoreCase)
                )
                    continue;

                object? value;
                try
                {
                    value = property.GetValue(source);
                }
                catch
                {
                    continue;
                }

                if (value == null)
                    continue;

                if (
                    string.Equals(property.Name, "Constraints", StringComparison.OrdinalIgnoreCase)
                    && !IsRuntimeLeaf(value)
                )
                {
                    foreach (var child in ReadMemberSequence(source, property.Name))
                        AddRuntimeConstraintObject(child);
                    continue;
                }

                var token = ToToken(value);
                if (token != null)
                    AddValues(property.Name, token, excluded, typeHint);
            }
        }

        public void AddRuntimeSpawnBehaviorObject(object source)
        {
            var typeHint = NormalizeName(source.GetType().Name);
            ApplyRuntimeTierTableBehavior(typeHint, source);
            if (typeHint.Contains("spawnbehaviortier"))
                AddRuntimeSpawnBehaviorTier(source);
        }

        private void AddTokenSpawnBehaviorTier(JObject obj)
        {
            var excluded = ReadBool(obj["IsNot"]);
            foreach (var property in obj.Properties())
            {
                if (!IsTierProperty(NormalizeName(property.Name)))
                    continue;

                foreach (var raw in EnumerateScalarValues(property.Value))
                    AddEnum(excluded ? _excludedTiers : _tiers, raw);
            }
        }

        private void AddRuntimeSpawnBehaviorTier(object source)
        {
            var excluded = ReadRuntimeBool(ReadMemberValue(source, "IsNot"));
            foreach (
                var property in source
                    .GetType()
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            )
            {
                if (
                    !property.CanRead
                    || property.GetIndexParameters().Length > 0
                    || !IsTierProperty(NormalizeName(property.Name))
                )
                    continue;

                object? value;
                try
                {
                    value = property.GetValue(source);
                }
                catch
                {
                    continue;
                }

                var token = ToToken(value);
                if (token == null)
                    continue;

                foreach (var raw in EnumerateScalarValues(token))
                    AddEnum(excluded ? _excludedTiers : _tiers, raw);
            }
        }

        private void AddValues(string propertyName, JToken value, bool excluded, string typeHint)
        {
            var normalized = NormalizeName(propertyName);
            var values = EnumerateScalarValues(value);
            if (typeHint.Contains("cardtype") || IsCardTypeProperty(normalized))
            {
                foreach (var raw in values)
                    if (TryParseEnum(raw, out ECardType parsed))
                        _cardType = parsed;
                return;
            }

            if (typeHint.Contains("size") || normalized.Contains("size"))
            {
                foreach (var raw in values)
                    AddEnum(excluded ? _excludedSizes : _sizes, raw);
                return;
            }

            if (typeHint.Contains("tier") || normalized.Contains("tier"))
            {
                foreach (var raw in values)
                    AddEnum(excluded ? _excludedTiers : _tiers, raw);
                return;
            }

            if (
                typeHint.Contains("hidden")
                || typeHint.Contains("keyword")
                || normalized.Contains("hiddentag")
                || normalized.Contains("keyword")
            )
            {
                foreach (var raw in values)
                    AddEnum(excluded ? _excludedKeywords : _keywords, raw);
                return;
            }

            if (typeHint.Contains("tag") || normalized.Contains("tag"))
            {
                foreach (var raw in values)
                    AddEnum(excluded ? _excludedTags : _tags, raw);
            }
        }

        public EncounterRewardFilter? ToRewardFilter(int? quantity)
        {
            var cardType = _cardType ?? InferCardType();
            if (cardType == null)
                return null;

            var sizes = ApplyExclusions(_sizes, _excludedSizes, KnownSizes);
            var tiers = ApplyExclusions(_tiers, _excludedTiers, KnownTiers);
            IReadOnlyList<ECardTag> tags =
                cardType == ECardType.Skill ? Array.Empty<ECardTag>() : _tags;
            var summary = BuildSummary(
                cardType.Value,
                sizes,
                tiers,
                tags,
                _keywords,
                _excludedTags,
                _excludedKeywords
            );
            return new EncounterRewardFilter(
                cardType.Value,
                quantity,
                fromAnyHero: false,
                sizes,
                tiers,
                tags,
                _keywords,
                summary,
                _excludedTags,
                _excludedKeywords,
                _usesDayTierTable,
                _usesDayTierDistribution
            );
        }

        // An exclusion-only tier behavior (IsNot: "any tier except X") leaves the pool
        // day-table-driven; only an inclusive tier list pins the pool to fixed tiers.
        private void ApplyTierTableBehavior(string typeHint, JObject obj)
        {
            var fixedTier = typeHint.Contains("spawnbehaviortier") && !ReadBool(obj["IsNot"]);
            var downShift = typeHint.Contains("downshifttier");
            var ignoreDayTable = IsEnabledIgnoreTierTable(typeHint, obj);
            var inheritTier = IsEnabledInheritTier(typeHint, obj);
            if (fixedTier || downShift || ignoreDayTable || inheritTier)
                _usesDayTierTable = false;
            if (fixedTier || ignoreDayTable || inheritTier)
                _usesDayTierDistribution = false;
        }

        private void ApplyRuntimeTierTableBehavior(string typeHint, object source)
        {
            var fixedTier =
                typeHint.Contains("spawnbehaviortier")
                && !ReadRuntimeBool(ReadMemberValue(source, "IsNot"));
            var downShift = typeHint.Contains("downshifttier");
            var ignoreDayTable = IsEnabledRuntimeIgnoreTierTable(typeHint, source);
            var inheritTier = IsEnabledRuntimeInheritTier(typeHint, source);
            if (fixedTier || downShift || ignoreDayTable || inheritTier)
                _usesDayTierTable = false;
            if (fixedTier || ignoreDayTable || inheritTier)
                _usesDayTierDistribution = false;
        }

        private static bool IsEnabledIgnoreTierTable(string typeHint, JObject obj) =>
            typeHint.Contains("ignoretier")
            && (
                !obj.TryGetValue(
                    "IgnoreTierTable",
                    StringComparison.OrdinalIgnoreCase,
                    out var enabled
                ) || ReadBool(enabled)
            );

        private static bool IsEnabledInheritTier(string typeHint, JObject obj) =>
            typeHint.Contains("inherittier")
            && (
                !obj.TryGetValue("Inherits", StringComparison.OrdinalIgnoreCase, out var inherits)
                || ReadBool(inherits)
            );

        private static bool IsEnabledRuntimeIgnoreTierTable(string typeHint, object source) =>
            typeHint.Contains("ignoretier")
            && (
                ReadMemberValue(source, "IgnoreTierTable") is not { } enabled
                || ReadRuntimeBool(enabled)
            );

        private static bool IsEnabledRuntimeInheritTier(string typeHint, object source) =>
            typeHint.Contains("inherittier")
            && (
                ReadMemberValue(source, "Inherits") is not { } inherits || ReadRuntimeBool(inherits)
            );

        private ECardType? InferCardType()
        {
            if (
                _tags.Count > 0
                || _excludedTags.Count > 0
                || _sizes.Count > 0
                || _excludedSizes.Count > 0
            )
                return ECardType.Item;
            if (_keywords.Count > 0 || _excludedKeywords.Count > 0)
                return ECardType.Skill;
            return null;
        }

        private static IReadOnlyList<TEnum> ApplyExclusions<TEnum>(
            IReadOnlyList<TEnum> included,
            IReadOnlyList<TEnum> excluded,
            IReadOnlyList<TEnum> allValues
        )
            where TEnum : struct
        {
            if (excluded.Count == 0)
                return included;

            var source = included.Count > 0 ? included : allValues;
            var result = new List<TEnum>(source.Count);
            foreach (var value in source)
            {
                if (ContainsValue(excluded, value) || ContainsValue(result, value))
                    continue;
                result.Add(value);
            }
            return result;
        }

        private static bool ContainsValue<TEnum>(IReadOnlyList<TEnum> values, TEnum value)
            where TEnum : struct
        {
            var comparer = EqualityComparer<TEnum>.Default;
            for (var i = 0; i < values.Count; i++)
                if (comparer.Equals(values[i], value))
                    return true;
            return false;
        }
    }

    private static string NormalizeName(string name) =>
        name.Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();

    private static bool IsExcludedPropertyName(string normalized) =>
        normalized.Contains("none")
        || normalized.Contains("exclude")
        || normalized.Contains("excluded")
        || normalized.Contains("without")
        || normalized.Contains("not");

    private static bool IsCardTypeProperty(string normalized) =>
        normalized == "type"
        || normalized == "types"
        || normalized == "cardtype"
        || normalized == "cardtypes"
        || normalized == "cardtypesany";

    private static bool IsTierProperty(string normalized) =>
        normalized == "tier" || normalized == "tiers" || normalized == "cardtier";

    private static bool HasKnownTokenConstraintProperty(JObject obj)
    {
        foreach (var property in obj.Properties())
        {
            var normalized = NormalizeName(property.Name);
            if (
                IsCardTypeProperty(normalized)
                || IsTierProperty(normalized)
                || normalized == "size"
                || normalized == "sizes"
                || normalized == "tag"
                || normalized == "tags"
                || normalized == "hiddentag"
                || normalized == "hiddentags"
                || normalized == "keyword"
                || normalized == "keywords"
            )
                return true;
        }

        return false;
    }

    private static bool ReadBool(JToken? token) =>
        token != null
        && (
            token.Type == JTokenType.Boolean
                ? token.Value<bool>()
                : bool.TryParse(token.ToString(), out var parsed) && parsed
        );

    private static bool ReadRuntimeBool(object? value) =>
        value switch
        {
            bool boolValue => boolValue,
            _ => value != null && bool.TryParse(value.ToString(), out var parsed) && parsed,
        };

    private static IEnumerable<string> EnumerateScalarValues(JToken token)
    {
        if (token is JArray array)
        {
            foreach (var child in array)
            {
                foreach (var value in EnumerateScalarValues(child))
                    yield return value;
            }
            yield break;
        }

        if (token is JObject obj)
        {
            foreach (var property in obj.Properties())
            {
                foreach (var value in EnumerateScalarValues(property.Value))
                    yield return value;
            }
            yield break;
        }

        if (token is IEnumerable enumerable && token is not JValue)
        {
            foreach (var child in enumerable)
            {
                if (child != null)
                    yield return child.ToString() ?? string.Empty;
            }
            yield break;
        }

        yield return token.ToString();
    }

    private static void AddEnum<TEnum>(List<TEnum> target, string raw)
        where TEnum : struct
    {
        if (!TryParseEnum(raw, out TEnum parsed) || target.Contains(parsed))
            return;
        target.Add(parsed);
    }

    private static bool TryParseEnum<TEnum>(string raw, out TEnum value)
        where TEnum : struct
    {
        var normalized = raw.Trim();
        if (normalized.Length == 0)
        {
            value = default;
            return false;
        }
        var dot = normalized.LastIndexOf('.');
        if (dot >= 0 && dot + 1 < normalized.Length)
            normalized = normalized[(dot + 1)..];
        return Enum.TryParse(normalized, ignoreCase: true, out value);
    }

    private static string BuildSummary(
        ECardType cardType,
        IReadOnlyList<ECardSize> sizes,
        IReadOnlyList<ETier> tiers,
        IReadOnlyList<ECardTag> tags,
        IReadOnlyList<EHiddenTag> keywords,
        IReadOnlyList<ECardTag> excludedTags,
        IReadOnlyList<EHiddenTag> excludedKeywords
    )
    {
        var included = new List<string>();
        foreach (var size in sizes)
            included.Add(size.ToString());
        var tierSummary = CompactTierSummary(tiers);
        if (!string.IsNullOrWhiteSpace(tierSummary))
            included.Add(tierSummary);
        foreach (var tag in tags)
            included.Add(tag.ToString());
        foreach (var keyword in keywords)
            included.Add(keyword.ToString());
        if (tags.Count == 0 && keywords.Count == 0)
            included.Add(cardType.ToString());

        var excluded = new List<string>();
        foreach (var tag in excludedTags)
            excluded.Add(tag.ToString());
        foreach (var keyword in excludedKeywords)
            excluded.Add(keyword.ToString());

        return excluded.Count == 0
            ? string.Join(" ", included)
            : $"{string.Join(" ", included)}, not {string.Join("/", excluded)}";
    }

    private static string CompactTierSummary(IReadOnlyList<ETier> tiers)
    {
        if (tiers.Count == 0)
            return string.Empty;
        if (tiers.Count == 1)
            return tiers[0].ToString();
        var ordered = new List<ETier>(tiers);
        ordered.Sort((left, right) => TierOrder.Rank(left).CompareTo(TierOrder.Rank(right)));
        if (IsContiguous(ordered))
            return $"{ordered[0]}-{ordered[^1]}";
        return string.Join("/", ordered);
    }

    private static bool IsContiguous(IReadOnlyList<ETier> tiers)
    {
        for (var i = 1; i < tiers.Count; i++)
            if (TierOrder.Rank(tiers[i]) != TierOrder.Rank(tiers[i - 1]) + 1)
                return false;
        return true;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
