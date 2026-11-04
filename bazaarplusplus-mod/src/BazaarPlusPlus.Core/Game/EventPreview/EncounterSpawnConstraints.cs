#nullable enable
using System.Collections;
using BazaarPlusPlus.Core.Cards;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.EventPreview;

/// <summary>
/// What a spawn context's groups, filters and behaviors constrain a dealt reward to, gathered by
/// either host's route and turned into its <see cref="EncounterRewardFilter"/>. Values arrive as
/// game member names or, from runtime objects, as the game's integers, so each goes through the
/// host's <c>normalize</c> (kinds <c>CardType</c>, <c>Size</c>, <c>Tier</c>, <c>Tag</c>,
/// <c>HiddenTag</c>) to a member name before it is read as a facet; a facet's own integer means
/// nothing (ADR-0015).
/// </summary>
public sealed class EncounterSpawnConstraints(Func<string, string?, string?> normalize)
{
    private static readonly CardSize[] KnownSizes =
    {
        CardSize.Small,
        CardSize.Medium,
        CardSize.Large,
    };

    private static readonly CardTier[] KnownTiers =
    {
        CardTier.Bronze,
        CardTier.Silver,
        CardTier.Gold,
        CardTier.Diamond,
        CardTier.Legendary,
    };

    private CardType? _cardType;
    private bool _usesDayTierTable = true;
    private bool _usesDayTierDistribution = true;
    private readonly List<CardSize> _sizes = new();
    private readonly List<CardSize> _excludedSizes = new();
    private readonly List<CardTier> _tiers = new();
    private readonly List<CardTier> _excludedTiers = new();
    private readonly List<CardTag> _tags = new();
    private readonly List<CardTag> _excludedTags = new();
    private readonly List<CardHiddenTag> _keywords = new();
    private readonly List<CardHiddenTag> _excludedKeywords = new();

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
    public static EncounterSpawnConstraints? TryMergeCompatible(
        IReadOnlyList<EncounterSpawnConstraints> candidates
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

    public EncounterSpawnConstraints Clone()
    {
        var clone = new EncounterSpawnConstraints(normalize)
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
        ApplyTierTableBehavior(
            fixedTier: typeHint.Contains("spawnbehaviortier") && !ReadBool(obj["IsNot"]),
            downShift: typeHint.Contains("downshifttier"),
            ignoreDayTable: IsEnabledIgnoreTierTable(typeHint, obj),
            inheritTier: IsEnabledInheritTier(typeHint, obj)
        );
        if (!typeHint.Contains("spawnbehaviortier"))
            return;

        var excluded = ReadBool(obj["IsNot"]);
        foreach (var property in obj.Properties())
            if (IsTierProperty(NormalizeName(property.Name)))
                AddTierValues(property.Value, excluded);
    }

    public void AddTierValues(JToken value, bool excluded)
    {
        foreach (var raw in EnumerateScalarValues(value))
            AddFacet("Tier", excluded ? _excludedTiers : _tiers, raw);
    }

    // An exclusion-only tier behavior (IsNot: "any tier except X") leaves the pool
    // day-table-driven; only an inclusive tier list pins the pool to fixed tiers.
    public void ApplyTierTableBehavior(
        bool fixedTier,
        bool downShift,
        bool ignoreDayTable,
        bool inheritTier
    )
    {
        if (fixedTier || downShift || ignoreDayTable || inheritTier)
            _usesDayTierTable = false;
        if (fixedTier || ignoreDayTable || inheritTier)
            _usesDayTierDistribution = false;
    }

    public void AddValues(string propertyName, JToken value, bool excluded, string typeHint)
    {
        var normalized = NormalizeName(propertyName);
        var values = EnumerateScalarValues(value);
        if (typeHint.Contains("cardtype") || IsCardTypeProperty(normalized))
        {
            foreach (var raw in values)
                if (TryReadFacet("CardType", raw, out CardType parsed))
                    _cardType = parsed;
            return;
        }

        if (typeHint.Contains("size") || normalized.Contains("size"))
        {
            foreach (var raw in values)
                AddFacet("Size", excluded ? _excludedSizes : _sizes, raw);
            return;
        }

        if (typeHint.Contains("tier") || normalized.Contains("tier"))
        {
            foreach (var raw in values)
                AddFacet("Tier", excluded ? _excludedTiers : _tiers, raw);
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
                AddFacet("HiddenTag", excluded ? _excludedKeywords : _keywords, raw);
            return;
        }

        if (typeHint.Contains("tag") || normalized.Contains("tag"))
        {
            foreach (var raw in values)
                AddFacet("Tag", excluded ? _excludedTags : _tags, raw);
        }
    }

    public EncounterRewardFilter? ToRewardFilter(int? quantity)
    {
        var inferred = _cardType ?? InferCardType();
        if (inferred is not { } cardType)
            return null;

        var sizes = ApplyExclusions(_sizes, _excludedSizes, KnownSizes);
        var tiers = ApplyExclusions(_tiers, _excludedTiers, KnownTiers);
        IReadOnlyList<CardTag> tags = cardType == CardType.Skill ? Array.Empty<CardTag>() : _tags;
        var summary = BuildSummary(
            cardType,
            sizes,
            tiers,
            tags,
            _keywords,
            _excludedTags,
            _excludedKeywords
        );
        return new EncounterRewardFilter(
            cardType,
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

    private CardType? InferCardType()
    {
        if (
            _tags.Count > 0
            || _excludedTags.Count > 0
            || _sizes.Count > 0
            || _excludedSizes.Count > 0
        )
            return CardType.Item;
        if (_keywords.Count > 0 || _excludedKeywords.Count > 0)
            return CardType.Skill;
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

    private void AddFacet<TFacet>(string kind, List<TFacet> target, string raw)
        where TFacet : struct, Enum
    {
        if (TryReadFacet(kind, raw, out TFacet parsed) && !target.Contains(parsed))
            target.Add(parsed);
    }

    private bool TryReadFacet<TFacet>(string kind, string raw, out TFacet value)
        where TFacet : struct, Enum
    {
        value = default;
        var trimmed = raw.Trim();
        var dot = trimmed.LastIndexOf('.');
        if (dot >= 0 && dot + 1 < trimmed.Length)
            trimmed = trimmed[(dot + 1)..];
        if (trimmed.Length == 0)
            return false;
        var name = normalize(kind, trimmed) ?? trimmed;
        return !long.TryParse(name, out _)
            && Enum.TryParse(name, ignoreCase: true, out value)
            && Enum.IsDefined(typeof(TFacet), value);
    }

    private static bool IsEnabledIgnoreTierTable(string typeHint, JObject obj) =>
        typeHint.Contains("ignoretier")
        && (
            !obj.TryGetValue("IgnoreTierTable", StringComparison.OrdinalIgnoreCase, out var enabled)
            || ReadBool(enabled)
        );

    private static bool IsEnabledInheritTier(string typeHint, JObject obj) =>
        typeHint.Contains("inherittier")
        && (
            !obj.TryGetValue("Inherits", StringComparison.OrdinalIgnoreCase, out var inherits)
            || ReadBool(inherits)
        );

    public static string NormalizeName(string name) =>
        name.Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();

    public static bool IsExcludedPropertyName(string normalized) =>
        normalized.Contains("none")
        || normalized.Contains("exclude")
        || normalized.Contains("excluded")
        || normalized.Contains("without")
        || normalized.Contains("not");

    public static bool IsTierProperty(string normalized) =>
        normalized == "tier" || normalized == "tiers" || normalized == "cardtier";

    public static bool IsCardTypeProperty(string normalized) =>
        normalized == "type"
        || normalized == "types"
        || normalized == "cardtype"
        || normalized == "cardtypes"
        || normalized == "cardtypesany";

    private static bool ReadBool(JToken? token) =>
        token != null
        && (
            token.Type == JTokenType.Boolean
                ? token.Value<bool>()
                : bool.TryParse(token.ToString(), out var parsed) && parsed
        );

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
}
