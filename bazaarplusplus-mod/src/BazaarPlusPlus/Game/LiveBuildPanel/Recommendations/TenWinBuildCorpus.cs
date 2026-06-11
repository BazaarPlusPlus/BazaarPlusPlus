#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

/// <summary>
/// Parsed, in-memory view of the analyzer-v4 ten-win build corpus
/// (<c>analyzer-v4/mod/tenwin_builds.json</c>, emitted by
/// <c>bazaarplusplus-analyzers/src/bpp/stages/analyze/mod_builds.py</c>).
///
/// The payload is a compact, schema-driven format: top-level string tables (<c>cards</c>,
/// <c>enchantments</c>) plus per-hero builds encoded as positional array rows whose column order
/// comes from <c>schemas</c>. Build IDs are implicit zero-based indices into a hero's
/// <c>builds</c> array; <c>card_index</c> maps a card ref to the build IDs that contain it.
/// The wire format is whole-tree snake_case at <c>schema_version</c> 2.
///
/// This type is pure (System + Newtonsoft only, zero game/Unity references): it owns parsing,
/// recall, and scoring. Projection of a matched build onto a renderable board lives in
/// <see cref="BuildRecommendationRepository"/>, which couples to the game item-board types.
/// </summary>
internal sealed class TenWinBuildCorpus
{
    private const int ExpectedSchemaVersion = 2;

    private static readonly string[] DefaultBuildSchema =
    {
        "card_refs",
        "layout",
        "stats",
        "selection",
    };
    private static readonly string[] DefaultLayoutSchema =
    {
        "card_ref",
        "slot",
        "tier",
        "enchant_ref",
        "size",
    };
    private static readonly string[] DefaultStatsSchema =
    {
        "completed_run_count",
        "ten_win_run_count",
        "ten_win_rate_bps",
        "avg_ten_win_final_day_tenth",
        "p75_ten_win_final_day",
        "avg_ten_win_final_losses_tenth",
        "elite_completed_run_count",
        "elite_ten_win_run_count",
        "elite_ten_win_rate_bps",
        "elite_avg_ten_win_final_day_tenth",
        "score",
    };

    private readonly IReadOnlyList<Guid?> _cards;
    private readonly IReadOnlyDictionary<Guid, int> _refByTemplateId;
    private readonly IReadOnlyDictionary<string, TenWinHero> _heroes;

    private TenWinBuildCorpus(
        IReadOnlyList<Guid?> cards,
        IReadOnlyDictionary<Guid, int> refByTemplateId,
        IReadOnlyDictionary<string, TenWinHero> heroes,
        DateTimeOffset? generatedAtUtc
    )
    {
        _cards = cards;
        _refByTemplateId = refByTemplateId;
        _heroes = heroes;
        GeneratedAtUtc = generatedAtUtc;
        BuildCount = heroes.Values.Sum(hero => hero.Builds.Count);
        HeroBuildCounts = heroes
            .Select(pair => new TenWinHeroBuildCount(pair.Key, pair.Value.Builds.Count))
            .OrderByDescending(pair => pair.BuildCount)
            .ThenBy(pair => pair.Hero, StringComparer.Ordinal)
            .ToArray();
    }

    public int HeroCount => _heroes.Count;

    /// <summary>Analyzer emission time (top-level <c>generatedAt</c>); null when absent/invalid.</summary>
    public DateTimeOffset? GeneratedAtUtc { get; }

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
            root = JObject.Parse(json);
        }
        catch
        {
            return null;
        }

        // Gate on the wire schema version so a future format change fails loudly
        // instead of silently mis-decoding. Greenfield: only v2 is accepted.
        if (root["schema_version"]?.Value<int?>() != ExpectedSchemaVersion)
            return null;

        if (root["heroes"] is not JObject heroesObj)
            return null;

        var cards = ParseCards(root["cards"] as JArray);
        var enchantments = ParseEnchantments(root["enchantments"] as JArray);

        if (root["schemas"] is not JObject schemas)
            return null;

        var buildSchema = ResolveSchema(schemas, "build", DefaultBuildSchema);
        var layoutSchema = ResolveSchema(schemas, "layout", DefaultLayoutSchema);
        var statsSchema = ResolveSchema(schemas, "stats", DefaultStatsSchema);
        if (buildSchema == null || layoutSchema == null || statsSchema == null)
            return null;

        var build = new BuildColumns(buildSchema);
        var layout = new LayoutColumns(layoutSchema);
        var stats = new StatsColumns(statsSchema);
        if (!build.IsComplete || !layout.IsComplete || !stats.IsComplete)
            return null;

        var refByTemplateId = new Dictionary<Guid, int>();
        for (var ref_ = 0; ref_ < cards.Count; ref_++)
        {
            if (cards[ref_] is { } templateId && !refByTemplateId.ContainsKey(templateId))
                refByTemplateId[templateId] = ref_;
        }

        var heroes = new Dictionary<string, TenWinHero>(StringComparer.Ordinal);
        foreach (var heroProperty in heroesObj.Properties())
        {
            if (heroProperty.Value is not JObject heroObj)
                continue;

            var builds = ParseBuilds(
                heroObj["builds"] as JArray,
                cards,
                enchantments,
                build,
                layout,
                stats
            );
            var cardIndex = ParseCardIndex(heroObj["card_index"] as JArray);
            heroes[heroProperty.Name] = new TenWinHero(builds, cardIndex);
        }

        return new TenWinBuildCorpus(cards, refByTemplateId, heroes, ParseGeneratedAt(root));
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
        if (string.IsNullOrWhiteSpace(hero) || !_heroes.TryGetValue(hero!, out var heroData))
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

    private static List<Guid?> ParseCards(JArray? cards)
    {
        var result = new List<Guid?>();
        if (cards == null)
            return result;

        foreach (var token in cards)
        {
            var text = token.Type == JTokenType.Null ? null : token.Value<string?>();
            result.Add(Guid.TryParse(text, out var guid) ? guid : (Guid?)null);
        }

        return result;
    }

    private static List<string?> ParseEnchantments(JArray? enchantments)
    {
        var result = new List<string?>();
        if (enchantments == null)
            return result;

        foreach (var token in enchantments)
        {
            var text = token.Type == JTokenType.Null ? null : token.Value<string?>();
            result.Add(CleanEnchant(text));
        }

        return result;
    }

    private static string? CleanEnchant(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value!.Trim();
        return string.Equals(trimmed, "None", StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }

    private static List<string>? ResolveSchema(JObject schemas, string key, string[] fallback)
    {
        if (schemas[key] is not JArray array)
            return null;

        var names = array
            .Where(token => token.Type == JTokenType.String)
            .Select(token => token.Value<string>()!)
            .ToList();
        return names.Count > 0 ? names : fallback.ToList();
    }

    private static List<TenWinBuild> ParseBuilds(
        JArray? builds,
        IReadOnlyList<Guid?> cards,
        IReadOnlyList<string?> enchantments,
        BuildColumns buildColumns,
        LayoutColumns layoutColumns,
        StatsColumns statsColumns
    )
    {
        var result = new List<TenWinBuild>();
        if (builds == null)
            return result;

        for (var buildId = 0; buildId < builds.Count; buildId++)
        {
            if (builds[buildId] is not JArray row)
                continue;

            var cardRefs = TokenAt(row, buildColumns.CardRefs) as JArray;
            var layoutRows = TokenAt(row, buildColumns.Layout) as JArray;
            var statsRow = TokenAt(row, buildColumns.Stats) as JArray;

            var templateIdSet = new HashSet<Guid>();
            if (cardRefs != null)
            {
                foreach (var refToken in cardRefs)
                {
                    var resolved = ResolveCard(cards, refToken.Value<int?>());
                    if (resolved != Guid.Empty)
                        templateIdSet.Add(resolved);
                }
            }

            var layout = ParseLayout(layoutRows, cards, enchantments, layoutColumns);
            var stats = ParseStats(statsRow, statsColumns);
            result.Add(new TenWinBuild(buildId, templateIdSet, layout, stats));
        }

        return result;
    }

    private static List<TenWinLayoutItem> ParseLayout(
        JArray? layoutRows,
        IReadOnlyList<Guid?> cards,
        IReadOnlyList<string?> enchantments,
        LayoutColumns columns
    )
    {
        var result = new List<TenWinLayoutItem>();
        if (layoutRows == null)
            return result;

        foreach (var token in layoutRows)
        {
            if (token is not JArray row)
                continue;

            var templateId = ResolveCard(cards, IntAt(row, columns.CardRef));
            if (templateId == Guid.Empty)
                continue;

            var enchantRef = IntAt(row, columns.EnchantRef) ?? 0;
            result.Add(
                new TenWinLayoutItem(
                    templateId,
                    IntAt(row, columns.Slot),
                    IntAt(row, columns.Tier),
                    ResolveEnchant(enchantments, enchantRef),
                    IntAt(row, columns.Size)
                )
            );
        }

        return result;
    }

    private static TenWinStats ParseStats(JArray? stats, StatsColumns columns)
    {
        if (stats == null)
            return TenWinStats.Empty;

        return new TenWinStats(
            IntAt(stats, columns.CompletedRunCount) ?? 0,
            IntAt(stats, columns.TenWinRunCount) ?? 0,
            IntAt(stats, columns.TenWinRateBps),
            IntAt(stats, columns.P75TenWinFinalDay),
            LongAt(stats, columns.Score) ?? 0
        );
    }

    private static Dictionary<int, IReadOnlyList<int>> ParseCardIndex(JArray? cardIndex)
    {
        var result = new Dictionary<int, IReadOnlyList<int>>();
        if (cardIndex == null)
            return result;

        foreach (var token in cardIndex)
        {
            if (token is not JArray pair || pair.Count < 2)
                continue;

            var cardRef = pair[0].Value<int?>();
            if (cardRef is not { } refValue || pair[1] is not JArray ids)
                continue;

            var buildIds = ids.Select(id => id.Value<int?>())
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();
            if (buildIds.Count > 0)
                result[refValue] = buildIds;
        }

        return result;
    }

    private static Guid ResolveCard(IReadOnlyList<Guid?> cards, int? cardRef) =>
        cardRef is { } ref_ && ref_ >= 0 && ref_ < cards.Count && cards[ref_] is { } guid
            ? guid
            : Guid.Empty;

    private static string? ResolveEnchant(IReadOnlyList<string?> enchantments, int enchantRef) =>
        enchantRef > 0 && enchantRef < enchantments.Count ? enchantments[enchantRef] : null;

    private static JToken? TokenAt(JArray array, int index) =>
        index >= 0 && index < array.Count ? array[index] : null;

    private static int? IntAt(JArray array, int index) =>
        index >= 0 && index < array.Count ? array[index].Value<int?>() : null;

    private static long? LongAt(JArray array, int index) =>
        index >= 0 && index < array.Count ? array[index].Value<long?>() : null;

    private readonly struct BuildColumns
    {
        public BuildColumns(List<string> schema)
        {
            CardRefs = schema.IndexOf("card_refs");
            Layout = schema.IndexOf("layout");
            Stats = schema.IndexOf("stats");
        }

        public int CardRefs { get; }
        public int Layout { get; }
        public int Stats { get; }

        public bool IsComplete => CardRefs >= 0 && Layout >= 0 && Stats >= 0;
    }

    private readonly struct LayoutColumns
    {
        public LayoutColumns(List<string> schema)
        {
            CardRef = schema.IndexOf("card_ref");
            Slot = schema.IndexOf("slot");
            Tier = schema.IndexOf("tier");
            EnchantRef = schema.IndexOf("enchant_ref");
            Size = schema.IndexOf("size");
        }

        public int CardRef { get; }
        public int Slot { get; }
        public int Tier { get; }
        public int EnchantRef { get; }
        public int Size { get; }

        public bool IsComplete => CardRef >= 0;
    }

    private readonly struct StatsColumns
    {
        public StatsColumns(List<string> schema)
        {
            CompletedRunCount = schema.IndexOf("completed_run_count");
            TenWinRunCount = schema.IndexOf("ten_win_run_count");
            TenWinRateBps = schema.IndexOf("ten_win_rate_bps");
            P75TenWinFinalDay = schema.IndexOf("p75_ten_win_final_day");
            Score = schema.IndexOf("score");
        }

        public int CompletedRunCount { get; }
        public int TenWinRunCount { get; }
        public int TenWinRateBps { get; }
        public int P75TenWinFinalDay { get; }
        public int Score { get; }

        public bool IsComplete => Score >= 0;
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
