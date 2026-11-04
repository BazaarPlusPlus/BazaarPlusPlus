#nullable enable
using System.Collections;
using System.Reflection;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Spawning;
using BazaarGameShared.Domain.Spawning.SpawnBehaviors;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.EventPreview;

internal static class EncounterStructuredParser
{
    private static readonly EncounterChoicePlanParser ChoiceParser = new(NormalizeChoiceEnum);

    private static readonly EncounterRewardFilterParser RewardParser = new(NormalizeChoiceEnum);

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
            "Tier" => Name<ETier>(raw),
            "Size" => Name<ECardSize>(raw),
            "CardType" => Name<ECardType>(raw),
            _ => null,
        };

    private static string? Name<T>(string? raw)
        where T : struct, Enum =>
        Enum.TryParse<T>(raw, true, out var value) ? value.ToString() : null;

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

        return RewardParser.TryParse(fallbackToken());
    }

    private static EncounterRewardFilter? TryParseRuntimeDealCardAction(object action)
    {
        var spawnContext = ReadMemberValue(action, "SpawnContext");
        if (spawnContext == null)
            return null;

        var merged = EncounterSpawnConstraints.TryMergeCompatible(
            ParseRuntimeSpawnConstraints(spawnContext)
        );
        return merged?.ToRewardFilter(ReadRuntimeQuantity(spawnContext));
    }

    private static IReadOnlyList<EncounterSpawnConstraints> ParseRuntimeSpawnConstraints(
        object spawnContext
    )
    {
        var result = new List<EncounterSpawnConstraints>();
        var spawnContextConstraints = new EncounterSpawnConstraints(NormalizeChoiceEnum);
        foreach (var behavior in ReadMemberSequence(spawnContext, "Behaviors"))
            AddRuntimeSpawnBehaviorObject(spawnContextConstraints, behavior);

        foreach (var group in ReadMemberSequence(spawnContext, "Groups"))
        {
            var groupConstraints = spawnContextConstraints.Clone();
            foreach (var constraint in ReadMemberSequence(group, "Constraints"))
                AddRuntimeConstraintObject(groupConstraints, constraint);
            foreach (var behavior in ReadMemberSequence(group, "Behaviors"))
                AddRuntimeSpawnBehaviorObject(groupConstraints, behavior);

            foreach (var filter in ReadMemberSequence(group, "Filters"))
            {
                var constraints = groupConstraints.Clone();
                var structuredConstraints = ReadMemberSequence(filter, "Constraints");
                var addedStructuredConstraint = false;
                foreach (var constraint in structuredConstraints)
                {
                    AddRuntimeConstraintObject(constraints, constraint);
                    addedStructuredConstraint = true;
                }

                if (!addedStructuredConstraint)
                    AddRuntimeFilterProperties(constraints, filter);

                if (constraints.HasAny)
                    result.Add(constraints);
            }
        }

        if (result.Count > 0)
            return result;

        var fallback = spawnContextConstraints.Clone();
        AddRuntimeFilterProperties(fallback, spawnContext);
        return fallback.HasAny ? new[] { fallback } : Array.Empty<EncounterSpawnConstraints>();
    }

    private static void AddRuntimeFilterProperties(
        EncounterSpawnConstraints constraints,
        object source
    )
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
                var normalized = EncounterSpawnConstraints.NormalizeName(property.Name);
                constraints.AddValues(
                    property.Name,
                    token,
                    EncounterSpawnConstraints.IsExcludedPropertyName(normalized),
                    string.Empty
                );
            }
        }
    }

    private static void AddRuntimeConstraintObject(
        EncounterSpawnConstraints constraints,
        object source
    )
    {
        var typeHint = EncounterSpawnConstraints.NormalizeName(source.GetType().Name);
        if (typeHint.Contains("constraintor"))
            return;

        if (typeHint.Contains("constraintand"))
        {
            foreach (var child in ReadMemberSequence(source, "Constraints"))
                AddRuntimeConstraintObject(constraints, child);
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
                    AddRuntimeConstraintObject(constraints, child);
                continue;
            }

            var token = ToToken(value);
            if (token != null)
                constraints.AddValues(property.Name, token, excluded, typeHint);
        }
    }

    private static void AddRuntimeSpawnBehaviorObject(
        EncounterSpawnConstraints constraints,
        object source
    )
    {
        var typeHint = EncounterSpawnConstraints.NormalizeName(source.GetType().Name);
        var excluded = ReadRuntimeBool(ReadMemberValue(source, "IsNot"));
        constraints.ApplyTierTableBehavior(
            fixedTier: typeHint.Contains("spawnbehaviortier") && !excluded,
            downShift: typeHint.Contains("downshifttier"),
            ignoreDayTable: IsEnabledRuntimeIgnoreTierTable(typeHint, source),
            inheritTier: IsEnabledRuntimeInheritTier(typeHint, source)
        );
        if (!typeHint.Contains("spawnbehaviortier"))
            return;

        foreach (
            var property in source
                .GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        )
        {
            if (
                !property.CanRead
                || property.GetIndexParameters().Length > 0
                || !EncounterSpawnConstraints.IsTierProperty(
                    EncounterSpawnConstraints.NormalizeName(property.Name)
                )
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
            if (token != null)
                constraints.AddTierValues(token, excluded);
        }
    }

    private static bool IsEnabledRuntimeIgnoreTierTable(string typeHint, object source) =>
        typeHint.Contains("ignoretier")
        && (
            ReadMemberValue(source, "IgnoreTierTable") is not { } enabled
            || ReadRuntimeBool(enabled)
        );

    private static bool IsEnabledRuntimeInheritTier(string typeHint, object source) =>
        typeHint.Contains("inherittier")
        && (ReadMemberValue(source, "Inherits") is not { } inherits || ReadRuntimeBool(inherits));

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

    private static bool ReadRuntimeBool(object? value) =>
        value switch
        {
            bool boolValue => boolValue,
            _ => value != null && bool.TryParse(value.ToString(), out var parsed) && parsed,
        };

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
