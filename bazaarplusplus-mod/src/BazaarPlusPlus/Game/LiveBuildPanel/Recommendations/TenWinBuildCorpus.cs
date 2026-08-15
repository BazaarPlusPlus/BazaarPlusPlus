#nullable enable
using System.Globalization;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.GameInterop.Heroes;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

/// <summary>
/// Parsed, in-memory view of the analyzer-v5 ten-win build corpus
/// (<c>analyzer-v5/builds/latest.json</c>).
///
/// The payload is a compact, schema-driven format: top-level string tables (<c>cards</c>,
/// <c>enchantments</c>) plus per-hero builds encoded as positional array rows whose column order
/// comes from <c>schemas</c>. Build IDs are implicit zero-based indices into a hero's
/// <c>builds</c> array; <c>card_index</c> maps a card ref to the build IDs that contain it.
/// The wire format is whole-tree snake_case at <c>schema_version</c> 2.
///
/// This type owns parsing, recall, and scoring. Its only game-facing dependency is the shared
/// compatibility boundary that canonicalizes The Dragons corpus keys. Projection of a matched build
/// onto a renderable board lives in <see cref="BuildRecommendationRepository"/>, which couples to
/// the game item-board types.
/// </summary>
internal sealed class TenWinBuildCorpus
{
    private const int ExpectedSchemaVersion = 2;
    private const string ExpectedKind = "ten_win_builds";
    private const int BoardSlotCount = 10;
    private const int MaximumWindowDays = 7;

    private static readonly string[] ExpectedBuildSchema = { "card_refs", "layout", "stats" };
    private static readonly string[] ExpectedLayoutSchema =
    {
        "card_ref",
        "slot",
        "tier",
        "enchant_ref",
        "size",
    };
    private static readonly string[] ExpectedStatsSchema =
    {
        "completed_run_count",
        "ten_win_run_count",
        "ten_win_rate_bps",
        "p75_ten_win_final_day",
        "score",
    };

    private readonly IReadOnlyList<Guid?> _cards;
    private readonly IReadOnlyDictionary<Guid, int> _refByTemplateId;
    private readonly IReadOnlyDictionary<string, TenWinHero> _heroes;

    private TenWinBuildCorpus(
        IReadOnlyList<Guid?> cards,
        IReadOnlyDictionary<Guid, int> refByTemplateId,
        IReadOnlyDictionary<string, TenWinHero> heroes,
        DateTimeOffset generatedAtUtc,
        DateTimeOffset windowEndUtc
    )
    {
        _cards = cards;
        _refByTemplateId = refByTemplateId;
        _heroes = heroes;
        GeneratedAtUtc = generatedAtUtc;
        WindowEndUtc = windowEndUtc;
        BuildCount = heroes.Values.Sum(hero => hero.Builds.Count);
        HeroBuildCounts = heroes
            .Select(pair => new TenWinHeroBuildCount(pair.Key, pair.Value.Builds.Count))
            .OrderByDescending(pair => pair.BuildCount)
            .ThenBy(pair => pair.Hero, StringComparer.Ordinal)
            .ToArray();
    }

    public int HeroCount => _heroes.Count;

    /// <summary>Analyzer emission time from top-level <c>generated_at</c>.</summary>
    public DateTimeOffset GeneratedAtUtc { get; }

    /// <summary>Last included data day from <c>window.end</c>, used for corpus freshness.</summary>
    public DateTimeOffset WindowEndUtc { get; }

    public int BuildCount { get; }

    public IReadOnlyList<TenWinHeroBuildCount> HeroBuildCounts { get; }

    /// <summary>
    /// Parses the compact payload. Returns <c>null</c> on any structural problem (unparseable JSON,
    /// missing <c>schemas</c>, missing required columns). The analyzer always emits <c>schemas</c>,
    /// so an absent schema signals a malformed/stale payload and is rejected rather than guessed.
    /// </summary>
    public static TenWinBuildCorpus? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        JObject root;
        try
        {
            root = JObject.Parse(
                json,
                new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                }
            );
        }
        catch
        {
            return null;
        }

        // Gate on the wire schema version so a future format change fails loudly
        // instead of silently mis-decoding. Greenfield: only v2 is accepted.
        if (
            !TryReadInt(root["schema_version"], out var schemaVersion)
            || schemaVersion != ExpectedSchemaVersion
            || root["kind"]?.Type != JTokenType.String
            || !string.Equals(root["kind"]!.Value<string>(), ExpectedKind, StringComparison.Ordinal)
        )
            return null;

        if (root["heroes"] is not JObject heroesObj)
            return null;

        var generatedAtUtc = ParseGeneratedAt(root);
        if (!generatedAtUtc.HasValue || !TryParseWindow(root["window"], out var windowEndUtc))
            return null;

        if (
            !TryParseCards(root["cards"], out var cards)
            || !TryParseEnchantments(root["enchantments"], out var enchantments)
        )
            return null;

        if (root["schemas"] is not JObject schemas)
            return null;

        if (
            !HasExactSchema(schemas, "build", ExpectedBuildSchema)
            || !HasExactSchema(schemas, "layout", ExpectedLayoutSchema)
            || !HasExactSchema(schemas, "stats", ExpectedStatsSchema)
        )
            return null;

        var refByTemplateId = new Dictionary<Guid, int>();
        for (var ref_ = 0; ref_ < cards.Count; ref_++)
        {
            if (cards[ref_] is { } templateId && !refByTemplateId.ContainsKey(templateId))
                refByTemplateId[templateId] = ref_;
        }

        var heroes = new Dictionary<string, TenWinHero>(StringComparer.Ordinal);
        foreach (
            var heroProperty in heroesObj
                .Properties()
                .OrderBy(property => CanonicalizeHeroId(property.Name), StringComparer.Ordinal)
                .ThenBy(AliasMergePriority)
                .ThenBy(property => property.Name, StringComparer.Ordinal)
        )
        {
            if (
                string.IsNullOrWhiteSpace(heroProperty.Name)
                || heroProperty.Value is not JObject heroObj
                || !TryParseBuilds(
                    heroObj["builds"],
                    cards,
                    enchantments,
                    out var builds,
                    out var buildCardRefs
                )
                || !TryParseCardIndex(
                    heroObj["card_index"],
                    cards.Count,
                    buildCardRefs,
                    out var cardIndex
                )
            )
                return null;

            var canonicalHero = CanonicalizeHeroId(heroProperty.Name);
            var parsedHero = new TenWinHero(builds, cardIndex);
            heroes[canonicalHero] = heroes.TryGetValue(canonicalHero, out var existing)
                ? MergeHeroes(existing, parsedHero, refByTemplateId)
                : parsedHero;
        }

        return new TenWinBuildCorpus(
            cards,
            refByTemplateId,
            heroes,
            generatedAtUtc.Value,
            windowEndUtc
        );
    }

    private static bool TryParseWindow(JToken? token, out DateTimeOffset windowEndUtc)
    {
        windowEndUtc = default;
        if (
            token is not JObject window
            || !TryReadDate(window["start"], out var start)
            || !TryReadDate(window["end"], out var end)
            || !TryReadInt(window["days"], out var days)
            || days is < 1 or > MaximumWindowDays
            || end < start
            || (end - start).Days + 1 != days
        )
            return false;

        windowEndUtc = new DateTimeOffset(
            DateTime.SpecifyKind(end, DateTimeKind.Unspecified),
            TimeSpan.Zero
        );
        return true;
    }

    private static bool TryReadDate(JToken? token, out DateTime date)
    {
        date = default;
        if (token?.Type == JTokenType.Date)
        {
            date = token.Value<DateTime>().Date;
            return true;
        }

        return token?.Type == JTokenType.String
            && DateTime.TryParseExact(
                token.Value<string>(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date
            );
    }

    // Json.NET eagerly converts ISO-8601 strings to Date tokens during JObject.Parse, so both
    // token shapes must be accepted; anything else degrades to null rather than failing the parse.
    private static DateTimeOffset? ParseGeneratedAt(JObject root)
    {
        var token = root["generated_at"];
        switch (token?.Type)
        {
            case JTokenType.Date:
                var dateTime = token.Value<DateTime>();
                return dateTime.Kind == DateTimeKind.Unspecified
                    ? new DateTimeOffset(dateTime, TimeSpan.Zero)
                    : new DateTimeOffset(dateTime.ToUniversalTime());
            case JTokenType.String:
                return DateTimeOffset.TryParse(
                    token.Value<string>(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var generatedAt
                )
                    ? generatedAt
                    : (DateTimeOffset?)null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Recall + ranking. Recall is keyed on the selected candidate cards only: the literal
    /// intersection of their indexed build IDs, falling back to the union when the intersection is
    /// empty (an uncovered selected card empties the intersection but must not empty the result
    /// while another selected card has coverage). Ranking is by matched-selected-card count, then
    /// live-state weight, then the analyzer score, then build ID for stability. An empty selection
    /// returns nothing — live state never drives recall, only ranking.
    /// </summary>
    public IReadOnlyList<TenWinBuildMatch> FindBuilds(
        string? hero,
        IReadOnlyCollection<Guid> selectedTemplateIds,
        BuildLiveState liveState
    )
    {
        if (
            string.IsNullOrWhiteSpace(hero)
            || !_heroes.TryGetValue(CanonicalizeHeroId(hero!), out var heroData)
        )
            return Array.Empty<TenWinBuildMatch>();

        var distinctSelected = selectedTemplateIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (distinctSelected.Count == 0)
            return Array.Empty<TenWinBuildMatch>();

        var coveredSets = new List<IReadOnlyList<int>>();
        var anyUncovered = false;
        foreach (var templateId in distinctSelected)
        {
            if (
                _refByTemplateId.TryGetValue(templateId, out var cardRef)
                && heroData.CardIndex.TryGetValue(cardRef, out var buildIds)
                && buildIds.Count > 0
            )
            {
                coveredSets.Add(buildIds);
            }
            else
            {
                anyUncovered = true;
            }
        }

        if (coveredSets.Count == 0)
            return Array.Empty<TenWinBuildMatch>();

        var candidateIds = !anyUncovered ? IntersectAll(coveredSets) : UnionAll(coveredSets);
        if (candidateIds.Count == 0)
            candidateIds = UnionAll(coveredSets);

        var selectedSet = new HashSet<Guid>(distinctSelected);
        return candidateIds
            .Where(id => id >= 0 && id < heroData.Builds.Count)
            .Select(id => heroData.Builds[id])
            .Select(build => new TenWinBuildMatch(
                build,
                build.TemplateIdSet.Count(selectedSet.Contains),
                build.TemplateIdSet.Sum(liveState.WeightFor)
            ))
            .OrderByDescending(match => match.MatchedSelectedCount)
            .ThenByDescending(match => match.LiveStateScore)
            .ThenByDescending(match => match.Build.Stats.Score)
            .ThenBy(match => match.Build.BuildId)
            .ToList();
    }

    private static string CanonicalizeHeroId(string heroId) =>
        TheDragonsHeroIdentity.IsAlias(heroId) ? TheDragonsHeroIdentity.CanonicalId : heroId;

    // A mixed transition corpus is merged independently of JObject/dictionary enumeration order:
    // the exact canonical key wins a shared implicit BuildId, then any other alias contributes only
    // IDs that were absent. The final ordinal tie-break keeps malformed case variants deterministic.
    private static int AliasMergePriority(JProperty property) =>
        string.Equals(property.Name, TheDragonsHeroIdentity.CanonicalId, StringComparison.Ordinal)
            ? 0
        : TheDragonsHeroIdentity.IsAlias(property.Name) ? 1
        : 0;

    private static TenWinHero MergeHeroes(
        TenWinHero preferred,
        TenWinHero fallback,
        IReadOnlyDictionary<Guid, int> refByTemplateId
    )
    {
        var buildsById = new Dictionary<int, TenWinBuild>();
        foreach (var build in preferred.Builds)
            buildsById.TryAdd(build.BuildId, build);
        foreach (var build in fallback.Builds)
            buildsById.TryAdd(build.BuildId, build);

        var builds = buildsById.Values.OrderBy(build => build.BuildId).ToArray();
        var cardIndex = new Dictionary<int, List<int>>();
        foreach (var build in builds)
        {
            foreach (var templateId in build.TemplateIdSet)
            {
                if (!refByTemplateId.TryGetValue(templateId, out var cardRef))
                    continue;

                if (!cardIndex.TryGetValue(cardRef, out var buildIds))
                {
                    buildIds = new List<int>();
                    cardIndex[cardRef] = buildIds;
                }
                buildIds.Add(build.BuildId);
            }
        }

        return new TenWinHero(
            builds,
            cardIndex.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<int>)pair.Value)
        );
    }

    private static HashSet<int> IntersectAll(IReadOnlyList<IReadOnlyList<int>> sets)
    {
        var result = new HashSet<int>(sets[0]);
        for (var i = 1; i < sets.Count; i++)
        {
            result.IntersectWith(sets[i]);
            if (result.Count == 0)
                break;
        }

        return result;
    }

    private static HashSet<int> UnionAll(IReadOnlyList<IReadOnlyList<int>> sets)
    {
        var result = new HashSet<int>();
        foreach (var set in sets)
            result.UnionWith(set);
        return result;
    }

    private static bool TryParseCards(JToken? token, out List<Guid?> result)
    {
        result = new List<Guid?>();
        if (token is not JArray cards)
            return false;

        var seen = new HashSet<Guid>();
        foreach (var cardToken in cards)
        {
            if (
                cardToken.Type != JTokenType.String
                || !Guid.TryParse(cardToken.Value<string>(), out var guid)
                || guid == Guid.Empty
                || !seen.Add(guid)
            )
                return false;
            result.Add(guid);
        }

        return true;
    }

    private static bool TryParseEnchantments(JToken? token, out List<string?> result)
    {
        result = new List<string?>();
        if (
            token is not JArray enchantments
            || enchantments.Count == 0
            || enchantments[0].Type != JTokenType.Null
        )
            return false;

        result.Add(null);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 1; index < enchantments.Count; index++)
        {
            var value = enchantments[index];
            if (value.Type != JTokenType.String)
                return false;

            var text = value.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(text) || !seen.Add(text!))
                return false;
            result.Add(text);
        }

        return true;
    }

    private static bool HasExactSchema(JObject schemas, string key, IReadOnlyList<string> expected)
    {
        if (schemas[key] is not JArray actual || actual.Count != expected.Count)
            return false;

        for (var index = 0; index < expected.Count; index++)
        {
            if (
                actual[index].Type != JTokenType.String
                || !string.Equals(
                    actual[index].Value<string>(),
                    expected[index],
                    StringComparison.Ordinal
                )
            )
                return false;
        }

        return true;
    }

    private static bool TryParseBuilds(
        JToken? token,
        IReadOnlyList<Guid?> cards,
        IReadOnlyList<string?> enchantments,
        out List<TenWinBuild> result,
        out List<HashSet<int>> buildCardRefs
    )
    {
        result = new List<TenWinBuild>();
        buildCardRefs = new List<HashSet<int>>();
        if (token is not JArray builds)
            return false;

        for (var buildId = 0; buildId < builds.Count; buildId++)
        {
            if (
                builds[buildId] is not JArray { Count: 3 } row
                || row[0] is not JArray cardRefs
                || row[1] is not JArray layoutRows
                || row[2] is not JArray statsRow
            )
                return false;

            var templateIdSet = new HashSet<Guid>();
            var refs = new List<int>(cardRefs.Count);
            var distinctRefs = new HashSet<int>();
            var previousCardRef = -1;
            foreach (var refToken in cardRefs)
            {
                if (
                    !TryReadRef(refToken, cards.Count, out var cardRef)
                    || cardRef < previousCardRef
                )
                    return false;

                refs.Add(cardRef);
                distinctRefs.Add(cardRef);
                templateIdSet.Add(cards[cardRef]!.Value);
                previousCardRef = cardRef;
            }

            if (
                !TryParseLayout(
                    layoutRows,
                    cards,
                    enchantments,
                    out var layout,
                    out var layoutCardRefs
                )
                || !refs.SequenceEqual(layoutCardRefs.OrderBy(cardRef => cardRef))
                || !TryParseStats(statsRow, out var stats)
            )
                return false;

            result.Add(new TenWinBuild(buildId, templateIdSet, layout, stats));
            buildCardRefs.Add(distinctRefs);
        }

        return true;
    }

    private static bool TryParseLayout(
        JArray layoutRows,
        IReadOnlyList<Guid?> cards,
        IReadOnlyList<string?> enchantments,
        out List<TenWinLayoutItem> result,
        out List<int> layoutCardRefs
    )
    {
        result = new List<TenWinLayoutItem>();
        layoutCardRefs = new List<int>();
        var occupied = new bool[BoardSlotCount];

        foreach (var token in layoutRows)
        {
            if (
                token is not JArray { Count: 5 } row
                || !TryReadRef(row[0], cards.Count, out var cardRef)
                || !TryReadInt(row[1], out var slot)
                || !TryReadInt(row[2], out var tier)
                || !TryReadRef(row[3], enchantments.Count, out var enchantRef)
                || !TryReadInt(row[4], out var size)
                || slot < 0
                || size is < 1 or > 3
                || slot + size > BoardSlotCount
                || tier is < 1 or > 5
            )
                return false;

            for (var socket = slot; socket < slot + size; socket++)
            {
                if (occupied[socket])
                    return false;
                occupied[socket] = true;
            }

            layoutCardRefs.Add(cardRef);
            result.Add(
                new TenWinLayoutItem(
                    cards[cardRef]!.Value,
                    slot,
                    tier,
                    ResolveEnchant(enchantments, enchantRef),
                    size
                )
            );
        }

        return occupied.All(value => value);
    }

    private static bool TryParseStats(JArray stats, out TenWinStats result)
    {
        result = TenWinStats.Empty;
        if (
            stats.Count != 5
            || !TryReadInt(stats[0], out var completedRunCount)
            || !TryReadInt(stats[1], out var tenWinRunCount)
            || !TryReadInt(stats[2], out var tenWinRateBps)
            || !TryReadNullableInt(stats[3], out var p75TenWinFinalDay)
            || !TryReadLong(stats[4], out var score)
            || completedRunCount < 0
            || tenWinRunCount < 0
            || tenWinRunCount > completedRunCount
            || tenWinRateBps is < 0 or > 10000
            || p75TenWinFinalDay < 0
            || score is < 0 or > 1000000
        )
            return false;

        result = new TenWinStats(
            completedRunCount,
            tenWinRunCount,
            tenWinRateBps,
            p75TenWinFinalDay,
            score
        );
        return true;
    }

    private static bool TryParseCardIndex(
        JToken? token,
        int cardCount,
        IReadOnlyList<HashSet<int>> buildCardRefs,
        out Dictionary<int, IReadOnlyList<int>> result
    )
    {
        result = new Dictionary<int, IReadOnlyList<int>>();
        if (token is not JArray cardIndex)
            return false;

        var previousCardRef = -1;
        foreach (var indexToken in cardIndex)
        {
            if (
                indexToken is not JArray { Count: 2 } pair
                || !TryReadRef(pair[0], cardCount, out var cardRef)
                || cardRef <= previousCardRef
                || pair[1] is not JArray { Count: > 0 } ids
            )
                return false;

            var buildIds = new List<int>(ids.Count);
            var previousBuildId = -1;
            foreach (var id in ids)
            {
                if (
                    !TryReadRef(id, buildCardRefs.Count, out var buildId)
                    || buildId <= previousBuildId
                    || !buildCardRefs[buildId].Contains(cardRef)
                )
                    return false;
                buildIds.Add(buildId);
                previousBuildId = buildId;
            }

            result[cardRef] = buildIds;
            previousCardRef = cardRef;
        }

        var expected = new Dictionary<int, List<int>>();
        for (var buildId = 0; buildId < buildCardRefs.Count; buildId++)
        {
            foreach (var cardRef in buildCardRefs[buildId])
            {
                if (!expected.TryGetValue(cardRef, out var buildIds))
                {
                    buildIds = new List<int>();
                    expected[cardRef] = buildIds;
                }
                buildIds.Add(buildId);
            }
        }

        var parsed = result;
        return expected.Count == parsed.Count
            && expected.All(pair =>
                parsed.TryGetValue(pair.Key, out var buildIds) && pair.Value.SequenceEqual(buildIds)
            );
    }

    private static string? ResolveEnchant(IReadOnlyList<string?> enchantments, int enchantRef) =>
        enchantRef == 0 ? null : enchantments[enchantRef];

    private static bool TryReadRef(JToken? token, int count, out int value) =>
        TryReadInt(token, out value) && value >= 0 && value < count;

    private static bool TryReadNullableInt(JToken? token, out int? value)
    {
        if (token?.Type == JTokenType.Null)
        {
            value = null;
            return true;
        }

        if (TryReadInt(token, out var integer))
        {
            value = integer;
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryReadInt(JToken? token, out int value)
    {
        value = default;
        if (token?.Type != JTokenType.Integer)
            return false;

        long integer;
        try
        {
            integer = token.Value<long>();
        }
        catch
        {
            return false;
        }
        if (integer < int.MinValue || integer > int.MaxValue)
            return false;
        value = (int)integer;
        return true;
    }

    private static bool TryReadLong(JToken? token, out long value)
    {
        value = default;
        if (token?.Type != JTokenType.Integer)
            return false;
        try
        {
            value = token.Value<long>();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class TenWinHero
{
    public TenWinHero(
        IReadOnlyList<TenWinBuild> builds,
        IReadOnlyDictionary<int, IReadOnlyList<int>> cardIndex
    )
    {
        Builds = builds;
        CardIndex = cardIndex;
    }

    public IReadOnlyList<TenWinBuild> Builds { get; }

    public IReadOnlyDictionary<int, IReadOnlyList<int>> CardIndex { get; }
}

internal sealed class TenWinBuild
{
    public TenWinBuild(
        int buildId,
        IReadOnlyCollection<Guid> templateIdSet,
        IReadOnlyList<TenWinLayoutItem> layout,
        TenWinStats stats
    )
    {
        BuildId = buildId;
        TemplateIdSet = templateIdSet;
        Layout = layout;
        Stats = stats;
    }

    public int BuildId { get; }

    /// <summary>Deduped template IDs from the build's <c>cardRefs</c> multiset (recall/scoring key).</summary>
    public IReadOnlyCollection<Guid> TemplateIdSet { get; }

    public IReadOnlyList<TenWinLayoutItem> Layout { get; }

    public TenWinStats Stats { get; }
}

internal sealed class TenWinLayoutItem
{
    public TenWinLayoutItem(Guid templateId, int? slot, int? tier, string? enchantName, int? size)
    {
        TemplateId = templateId;
        Slot = slot;
        Tier = tier;
        EnchantName = enchantName;
        Size = size;
    }

    public Guid TemplateId { get; }

    public int? Slot { get; }

    /// <summary>Mod tier value 1..5 (Bronze..Legendary), or null when absent/unknown.</summary>
    public int? Tier { get; }

    /// <summary>Display-only enchantment name, or null. Never participates in recall or scoring.</summary>
    public string? EnchantName { get; }

    /// <summary>Board slot span 1..3, or null when absent.</summary>
    public int? Size { get; }
}

internal readonly struct TenWinStats
{
    public static readonly TenWinStats Empty = new(0, 0, null, null, 0);

    public TenWinStats(
        int completedRunCount,
        int tenWinRunCount,
        int? tenWinRateBps,
        int? p75TenWinFinalDay,
        long score
    )
    {
        CompletedRunCount = completedRunCount;
        TenWinRunCount = tenWinRunCount;
        TenWinRateBps = tenWinRateBps;
        P75TenWinFinalDay = p75TenWinFinalDay;
        Score = score;
    }

    public int CompletedRunCount { get; }

    public int TenWinRunCount { get; }

    /// <summary>Ten-win rate in basis points (2667 == 26.67%); null only when never emitted.</summary>
    public int? TenWinRateBps { get; }

    /// <summary>p75 ten-win final day as a plain rounded day (not tenths); null when unavailable.</summary>
    public int? P75TenWinFinalDay { get; }

    public long Score { get; }
}

internal readonly struct TenWinBuildMatch
{
    public TenWinBuildMatch(TenWinBuild build, int matchedSelectedCount, int liveStateScore)
    {
        Build = build;
        MatchedSelectedCount = matchedSelectedCount;
        LiveStateScore = liveStateScore;
    }

    public TenWinBuild Build { get; }

    public int MatchedSelectedCount { get; }

    public int LiveStateScore { get; }
}
