#nullable enable
using System.Collections;
using System.Reflection;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Spawning;
using BazaarGameShared.Domain.Spawning.SpawnBehaviors;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.GameInterop.Cards;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.EventPreview;

internal static class EncounterStructuredParser
{
    private static readonly EncounterChoicePlanParser ChoiceParser = new(
        NormalizeChoiceEnum,
        ParseChoicePool
    );

    public static IReadOnlyList<EncounterChoiceGroupData> TryParseEventChoiceGroups(
        object? source
    ) => ChoiceParser.TryParseEventChoiceGroups(ToToken(source));

    public static int? TryParseEventChoiceLimit(object? source) =>
        ChoiceParser.TryParseEventChoiceLimit(ToToken(source));

    public static bool TryParseEventOutcomeGroups(
        object? source,
        out IReadOnlyList<EncounterOutcomeGroupData> groups
    ) => ChoiceParser.TryParseEventOutcomeGroups(ToToken(source), out groups);

    internal static IReadOnlyList<EncounterCardRequirement> ReadCardRequirements(JToken? token) =>
        ChoiceParser.ReadCardRequirements(token);

    private static string? NormalizeChoiceEnum(string kind, string? raw) =>
        kind switch
        {
            "SelectionMethod" => Name<ESpawnSelectionMethod>(raw),
            "Comparison" => Name<EComparisonOperator>(raw),
            "ListComparison" => Name<EListComparisonOperator>(raw),
            "HiddenTag" => Name<EHiddenTag>(raw),
            "Tag" => Name<ECardTag>(raw),
            _ => null,
        };

    private static string? Name<T>(string? raw)
        where T : struct, Enum =>
        Enum.TryParse<T>(raw, true, out var value) ? value.ToString() : null;

    private static EncounterRewardFilter? ParseChoicePool(
        JToken context,
        JToken group,
        JToken filter,
        int? quantity
    )
    {
        var constraints = new SpawnConstraints();
        AddTokenBehaviors(constraints, context["Behaviors"]);
        AddTokenBehaviors(constraints, group["Behaviors"]);
        foreach (var obj in EnumerateObjects(filter["Constraints"]!))
            if (LooksLikeTokenConstraintObject(obj))
                constraints.AddTokenConstraintObject(obj);
        return constraints.ToRewardFilter(quantity);
    }

    // The factory is invoked only if the runtime route fails, so a caller that has no token yet
    // can hand over a lazy one instead of paying for JToken.FromObject up front.
    internal static EncounterRewardFilter? TryParseRewardFilterWithPreparedToken(
        object? source,
        Func<JToken?> preparedToken
    )
    {
        return TryParseRewardFilterCore(source, preparedToken);
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
                _ => JToken.FromObject(
                    source,
                    JsonSerializer.Create(
                        new JsonSerializerSettings
                        {
                            Converters = { new SpawnBehaviorTokenConverter() },
                        }
                    )
                ),
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

    // Prepared plans must retain behavior identity: parameterless behaviors and
    // fixed-tier behaviors cannot be recovered from ordinary runtime JSON alone.
    private sealed class SpawnBehaviorTokenConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) =>
            typeof(ITSpawnBehavior).IsAssignableFrom(objectType);

        public override bool CanRead => false;

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }
            var token = JObject.FromObject(value);
            token["$type"] = value.GetType().Name;
            token.WriteTo(writer);
        }

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer
        ) => throw new NotSupportedException();
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

        // The constraints hold the game's values, which arrive as names or, from runtime
        // objects, as the game's integers; the filter carries their facets (ADR-0015).
        public EncounterRewardFilter? ToRewardFilter(int? quantity)
        {
            var gameCardType = _cardType ?? InferCardType();
            if (
                gameCardType == null
                || !DesktopCardFacets.Type.TryToFacet(gameCardType.Value, out var cardType)
            )
                return null;

            var sizes = ToFacets(
                DesktopCardFacets.Size,
                ApplyExclusions(_sizes, _excludedSizes, KnownSizes)
            );
            var tiers = ToFacets(
                DesktopCardFacets.Tier,
                ApplyExclusions(_tiers, _excludedTiers, KnownTiers)
            );
            IReadOnlyList<CardTag> tags =
                cardType == CardType.Skill
                    ? Array.Empty<CardTag>()
                    : ToFacets(DesktopCardFacets.Tag, _tags);
            var keywords = ToFacets(DesktopCardFacets.HiddenTag, _keywords);
            var excludedTags = ToFacets(DesktopCardFacets.Tag, _excludedTags);
            var excludedKeywords = ToFacets(DesktopCardFacets.HiddenTag, _excludedKeywords);
            var summary = BuildSummary(
                cardType,
                sizes,
                tiers,
                tags,
                keywords,
                excludedTags,
                excludedKeywords
            );
            return new EncounterRewardFilter(
                cardType,
                quantity,
                fromAnyHero: false,
                sizes,
                tiers,
                tags,
                keywords,
                summary,
                excludedTags,
                excludedKeywords,
                _usesDayTierTable,
                _usesDayTierDistribution
            );
        }

        private static IReadOnlyList<TFacet> ToFacets<TGame, TFacet>(
            CardFacetNameMap<TGame, TFacet> map,
            IReadOnlyList<TGame> values
        )
            where TGame : struct, Enum
            where TFacet : struct, Enum
        {
            var result = new List<TFacet>(values.Count);
            foreach (var value in values)
            {
                if (map.TryToFacet(value, out var facet) && !result.Contains(facet))
                    result.Add(facet);
            }
            return result;
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
        CardType cardType,
        IReadOnlyList<CardSize> sizes,
        IReadOnlyList<CardTier> tiers,
        IReadOnlyList<CardTag> tags,
        IReadOnlyList<CardHiddenTag> keywords,
        IReadOnlyList<CardTag> excludedTags,
        IReadOnlyList<CardHiddenTag> excludedKeywords
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

    private static string CompactTierSummary(IReadOnlyList<CardTier> tiers)
    {
        if (tiers.Count == 0)
            return string.Empty;
        if (tiers.Count == 1)
            return tiers[0].ToString();
        var ordered = new List<CardTier>(tiers);
        ordered.Sort(
            (left, right) => CardFacetOrder.TierRank(left).CompareTo(CardFacetOrder.TierRank(right))
        );
        if (IsContiguous(ordered))
            return $"{ordered[0]}-{ordered[^1]}";
        return string.Join("/", ordered);
    }

    private static bool IsContiguous(IReadOnlyList<CardTier> tiers)
    {
        for (var i = 1; i < tiers.Count; i++)
            if (CardFacetOrder.TierRank(tiers[i]) != CardFacetOrder.TierRank(tiers[i - 1]) + 1)
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
