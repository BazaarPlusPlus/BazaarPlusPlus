#nullable enable
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.EventPreview;

/// <summary>
/// A card's dealt reward read off its JSON, as static data stores it and as the desktop's
/// <c>EncounterStructuredParser</c> reads the typed template: the first
/// <c>TActionGameDealCards</c> whose spawn context constrains its reward. A context of groups
/// takes its constraints from those groups' filters and behaviors alone, so a list of fixed ids is
/// no filter even when a prerequisite beside it names a tag, and the branches of a
/// <c>ConstraintOr</c> constrain nothing.
/// </summary>
public sealed class EncounterRewardFilterParser(Func<string, string?, string?> normalize)
{
    public EncounterRewardFilter? TryParse(JToken? card)
    {
        if (card == null)
            return null;

        foreach (var action in EnumerateObjects(card))
        {
            if (!IsType(action, "TActionGameDealCards"))
                continue;

            var spawnContext = action["SpawnContext"];
            if (spawnContext == null)
                continue;

            var reward = TryParseSpawnContext(spawnContext);
            if (reward != null)
                return reward;
        }

        return null;
    }

    /// <summary>
    /// A random outcome's dynamic pool: the context's and group's behaviors with the filter's
    /// constraints.
    /// </summary>
    public EncounterRewardFilter? ParsePool(
        JToken context,
        JToken group,
        JToken filter,
        int? quantity
    )
    {
        var constraints = new EncounterSpawnConstraints(normalize);
        AddBehaviors(constraints, context["Behaviors"]);
        AddBehaviors(constraints, group["Behaviors"]);
        foreach (var obj in EnumerateObjects(filter["Constraints"]!))
            if (LooksLikeConstraintObject(obj))
                constraints.AddTokenConstraintObject(obj);
        return constraints.ToRewardFilter(quantity);
    }

    private EncounterRewardFilter? TryParseSpawnContext(JToken spawnContext)
    {
        var merged = EncounterSpawnConstraints.TryMergeCompatible(
            ParseSpawnConstraints(spawnContext)
        );
        return merged?.ToRewardFilter(ReadQuantity(spawnContext));
    }

    private IReadOnlyList<EncounterSpawnConstraints> ParseSpawnConstraints(JToken spawnContext)
    {
        var result = new List<EncounterSpawnConstraints>();
        var contextConstraints = new EncounterSpawnConstraints(normalize);
        AddBehaviors(contextConstraints, spawnContext["Behaviors"]);
        var hasGroups = false;
        foreach (var groupsToken in FindProperties(spawnContext, "Groups"))
        {
            if (groupsToken is not JArray groups)
                continue;

            hasGroups = true;
            foreach (var group in groups)
            {
                var groupConstraints = contextConstraints.Clone();
                AddConstraints(groupConstraints, group["Constraints"]);
                AddBehaviors(groupConstraints, group["Behaviors"]);
                if (group["Filters"] is not JArray filters)
                    continue;

                foreach (var filter in filters)
                {
                    var constraints = groupConstraints.Clone();
                    var structuredConstraints = filter["Constraints"];
                    if (structuredConstraints != null)
                        AddConstraints(constraints, structuredConstraints);
                    else if (filter is JObject filterObject)
                        constraints.AddTokenFilterProperties(filterObject);

                    if (constraints.HasAny)
                        result.Add(constraints);
                }
            }
        }

        if (result.Count > 0)
            return result;

        var fallback = contextConstraints.Clone();
        if (hasGroups)
        {
            if (spawnContext is JObject contextObject)
                fallback.AddTokenFilterProperties(contextObject);
        }
        else
        {
            foreach (var obj in EnumerateObjects(spawnContext))
            {
                if (LooksLikeSpawnBehaviorObject(obj))
                    fallback.AddTokenSpawnBehaviorObject(obj);
                else if (LooksLikeConstraintObject(obj))
                    fallback.AddTokenConstraintObject(obj);
                else
                    fallback.AddTokenFilterProperties(obj);
            }
        }

        return fallback.HasAny ? new[] { fallback } : Array.Empty<EncounterSpawnConstraints>();
    }

    private static void AddConstraints(EncounterSpawnConstraints constraints, JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return;

        if (token is JObject obj)
        {
            var typeHint = EncounterSpawnConstraints.NormalizeName(
                obj["$type"]?.ToString() ?? string.Empty
            );
            if (typeHint.Contains("constraintor"))
                return;
            if (LooksLikeConstraintObject(obj))
                constraints.AddTokenConstraintObject(obj);
        }

        foreach (var child in token.Children())
            AddConstraints(constraints, child);
    }

    private static void AddBehaviors(EncounterSpawnConstraints constraints, JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return;

        foreach (var behaviorObject in EnumerateObjects(token))
            if (LooksLikeSpawnBehaviorObject(behaviorObject))
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

    private static bool LooksLikeConstraintObject(JObject obj) =>
        obj.TryGetValue("IsNot", StringComparison.OrdinalIgnoreCase, out _)
        || !string.IsNullOrWhiteSpace(obj["$type"]?.ToString())
        || HasKnownConstraintProperty(obj);

    private static bool LooksLikeSpawnBehaviorObject(JObject obj)
    {
        var typeHint = EncounterSpawnConstraints.NormalizeName(
            obj["$type"]?.ToString() ?? string.Empty
        );
        return typeHint.Contains("spawnbehavior");
    }

    private static bool HasKnownConstraintProperty(JObject obj)
    {
        foreach (var property in obj.Properties())
        {
            var normalized = EncounterSpawnConstraints.NormalizeName(property.Name);
            if (
                EncounterSpawnConstraints.IsCardTypeProperty(normalized)
                || EncounterSpawnConstraints.IsTierProperty(normalized)
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
}
