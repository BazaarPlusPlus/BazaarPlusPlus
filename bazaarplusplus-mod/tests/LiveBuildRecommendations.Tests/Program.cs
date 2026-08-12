#nullable enable

using System.Reflection;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LiveBuildRecommendations.Tests;

internal static class Program
{
    private const string ContractResourceName =
        "LiveBuildRecommendations.Tests.analyzer-v5-schema2-contract.json";
    private static readonly Guid FirstCard = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MissingCard = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly (string Name, Func<Task> Run)[] Tests =
    {
        ("parser accepts the analyzer-v5 schema-2 contract", AcceptsContract),
        ("parser accepts a one-day window", AcceptsOneDayWindow),
        ("parser accepts a five-day window", AcceptsFiveDayWindow),
        ("parser accepts a seven-day window", AcceptsSevenDayWindow),
        ("parser rejects a zero-day window", RejectsZeroDayWindow),
        ("parser rejects an eight-day window", RejectsEightDayWindow),
        ("parser rejects an inconsistent window", RejectsInconsistentWindow),
        ("parser handles null enchant refs and nullable p75", HandlesNullableFieldsAndEmptyHero),
        ("parser rejects unsupported schema versions and heroes arrays", RejectsWrongTopLevelShape),
        ("parser requires the exact positional schemas", RejectsNonExactSchemas),
        ("parser rejects illegal table and build indexes", RejectsIllegalIndexes),
        (
            "parser rejects rows and indexes that violate the embedded contract",
            RejectsSemanticMismatches
        ),
        ("recall behavior remains selected-card driven", PreservesRecallBehavior),
        ("recommendation projection preserves layout and stats", PreservesRecommendationProjection),
        ("recommendation freshness uses window.end", UsesWindowEndForFreshness),
        ("embedded fallback follows the same parser path", ParsesEmbeddedFallback),
        ("catalog warmup prefers a fresh cache", PreservesFreshCacheBehavior),
        ("catalog warmup rejects an old-schema cache", RejectsOldSchemaCache),
        ("catalog refresh publishes and caches valid remote data", PreservesRemoteRefreshBehavior),
        ("catalog refresh keeps fallback on invalid remote data", RejectsInvalidRemoteRefresh),
        ("remote URL configuration points at analyzer-v5 latest", UsesAnalyzerV5Url),
        ("optional live sample follows the same parser path", ParsesOptionalLiveSample),
    };

    private static async Task<int> Main()
    {
        var failures = new List<string>();
        foreach (var (name, run) in Tests)
        {
            try
            {
                await run().ConfigureAwait(false);
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception ex)
            {
                failures.Add(name);
                Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
            }
        }

        Console.WriteLine($"{Tests.Length - failures.Count}/{Tests.Length} tests passed.");
        if (failures.Count == 0)
            return 0;

        Console.Error.WriteLine("Failed tests:");
        foreach (var failure in failures)
            Console.Error.WriteLine($"  {failure}");
        return 1;
    }

    private static Task AcceptsContract()
    {
        var corpus = RequireCorpus(ContractJson());
        Equal(1, corpus.HeroCount, "hero count");
        Equal(1, corpus.BuildCount, "build count");
        Equal(DateTimeOffset.Parse("2026-08-12T02:00:00Z"), corpus.GeneratedAtUtc, "generated_at");

        var build = SingleMatch(corpus).Build;
        Equal(5, build.Layout.Count, "layout item count");
        Equal(123, build.Stats.CompletedRunCount, "completed runs");
        Equal(45, build.Stats.TenWinRunCount, "ten-win runs");
        Equal(3659, build.Stats.TenWinRateBps, "ten-win rate");
        Equal(13, build.Stats.P75TenWinFinalDay, "p75 day");
        Equal(421037L, build.Stats.Score, "score");
        Equal(null, build.Layout[0].EnchantName, "enchant ref zero");
        Equal("Burn", build.Layout[1].EnchantName, "Burn enchant ref");
        Equal("Shielded", build.Layout[4].EnchantName, "Shielded enchant ref");
        return Task.CompletedTask;
    }

    private static Task HandlesNullableFieldsAndEmptyHero()
    {
        var root = ContractObject();
        StatsRow(root)[3] = JValue.CreateNull();
        Heroes(root)["Dooley"] = new JObject
        {
            ["builds"] = new JArray(),
            ["card_index"] = new JArray(),
        };

        var corpus = RequireCorpus(root.ToString(Formatting.None));
        Equal(2, corpus.HeroCount, "empty heroes remain represented");
        Equal(null, SingleMatch(corpus).Build.Stats.P75TenWinFinalDay, "nullable p75");
        Equal(
            0,
            corpus.FindBuilds("Dooley", new[] { FirstCard }, BuildLiveState.Empty).Count,
            "empty hero recall"
        );
        return Task.CompletedTask;
    }

    private static Task AcceptsOneDayWindow()
    {
        AcceptWindow("2026-08-11", "2026-08-11", 1);
        return Task.CompletedTask;
    }

    private static Task AcceptsFiveDayWindow()
    {
        AcceptWindow("2026-08-07", "2026-08-11", 5);
        return Task.CompletedTask;
    }

    private static Task AcceptsSevenDayWindow()
    {
        AcceptWindow("2026-08-05", "2026-08-11", 7);
        return Task.CompletedTask;
    }

    private static Task RejectsZeroDayWindow()
    {
        RejectWindow("2026-08-11", "2026-08-11", 0);
        return Task.CompletedTask;
    }

    private static Task RejectsEightDayWindow()
    {
        RejectWindow("2026-08-04", "2026-08-11", 8);
        return Task.CompletedTask;
    }

    private static Task RejectsInconsistentWindow()
    {
        RejectWindow("2026-08-07", "2026-08-11", 4);
        return Task.CompletedTask;
    }

    private static Task RejectsWrongTopLevelShape()
    {
        Reject(root => root["schema_version"] = 1, "schema_version 1");
        Reject(root => root["schema_version"] = 3, "schema_version 3");
        Reject(root => root["heroes"] = new JArray(), "heroes array");
        var oversizedInteger = ContractJson()
            .Replace(
                "\"schema_version\": 2",
                "\"schema_version\": 999999999999999999999999",
                StringComparison.Ordinal
            );
        True(TenWinBuildCorpus.Parse(oversizedInteger) == null, "oversized schema integer");
        return Task.CompletedTask;
    }

    private static Task RejectsNonExactSchemas()
    {
        foreach (var schemaName in new[] { "build", "layout", "stats" })
        {
            Reject(
                root => Schema(root, schemaName).Add("unexpected"),
                $"extra {schemaName} column"
            );
        }

        Reject(
            root =>
            {
                var schema = Schema(root, "build");
                (schema[0], schema[1]) = (schema[1], schema[0]);
            },
            "reordered build columns"
        );
        return Task.CompletedTask;
    }

    private static Task RejectsIllegalIndexes()
    {
        var mutations = new (string Name, Action<JObject> Mutate)[]
        {
            ("card_refs card ref", root => CardRefs(root)[0] = 99),
            ("layout card ref", root => LayoutRow(root, 0)[0] = 99),
            ("layout enchant ref", root => LayoutRow(root, 0)[3] = 99),
            ("card_index card ref", root => CardIndexPair(root, 0)[0] = 99),
            ("card_index build ID", root => ((JArray)CardIndexPair(root, 0)[1]!)[0] = 99),
        };

        foreach (var (name, mutate) in mutations)
            Reject(mutate, name);
        return Task.CompletedTask;
    }

    private static Task RejectsSemanticMismatches()
    {
        Reject(root => root["kind"] = "other", "wrong kind");
        Reject(root => ((JArray)root["enchantments"]!)[0] = "Burn", "non-null ref zero");
        Reject(root => BuildRow(root).Add(new JArray()), "extra build value");
        Reject(root => StatsRow(root).Add(1), "extra stats value");
        Reject(root => LayoutRow(root, 0)[4] = 1, "layout does not occupy ten slots");
        Reject(root => ((JArray)Hero(root)["card_index"]!).RemoveAt(0), "incomplete card index");
        Reject(root => Hero(root)["builds"] = new JObject(), "builds object");
        Reject(root => Heroes(root)["Vanessa"] = new JArray(), "hero array");
        return Task.CompletedTask;
    }

    private static Task PreservesRecallBehavior()
    {
        var corpus = RequireCorpus(ContractJson());
        Equal(
            0,
            corpus.FindBuilds("Vanessa", Array.Empty<Guid>(), BuildLiveState.Empty).Count,
            "empty selection"
        );
        Equal(
            1,
            corpus
                .FindBuilds("Vanessa", new[] { FirstCard, MissingCard }, BuildLiveState.Empty)
                .Count,
            "covered-card union fallback"
        );
        Equal(
            0,
            corpus
                .FindBuilds(
                    "Vanessa",
                    Array.Empty<Guid>(),
                    BuildLiveState.From(new[] { FirstCard }, null, null)
                )
                .Count,
            "live state never drives recall"
        );
        return Task.CompletedTask;
    }

    private static Task PreservesRecommendationProjection()
    {
        var corpus = RequireCorpus(ContractJson());
        using var catalog = new SnapshotCatalog(corpus);
        var repository = new BuildRecommendationRepository(catalog);

        var recommendations = repository.FindRecommendations("Vanessa", new[] { FirstCard });
        Equal(1, recommendations.Count, "recommendation count");
        var recommendation = recommendations[0];
        Equal("Ten-Win Build", recommendation.ModeLabel, "mode label");
        Equal(45, recommendation.TenWinRunCount, "projected ten-win runs");
        Equal(3659, recommendation.TenWinRateBps, "projected ten-win rate");
        Equal(13, recommendation.P75TenWinFinalDay, "projected p75 day");
        Equal(5, recommendation.Board.Cards.Count, "projected card count");
        Equal(
            DateTimeOffset.Parse("2026-08-11T00:00:00Z"),
            repository.GetCorpusSummary()?.WindowEndUtc,
            "freshness uses window.end"
        );
        Equal(null, recommendation.Board.Cards[0].EnchantmentType, "null enchant projection");
        Equal(
            EEnchantmentType.Burn,
            recommendation.Board.Cards[1].EnchantmentType,
            "enchant projection"
        );
        Equal(
            EContainerSocketId.Slot8,
            recommendation.Board.Cards[4].DisplaySocketId,
            "slot projection"
        );
        return Task.CompletedTask;
    }

    private static Task ParsesEmbeddedFallback()
    {
        var seedPath = EmbeddedFallbackPath();
        True(File.Exists(seedPath), "embedded fallback file exists");
        var seedDocument = File.ReadAllText(seedPath);
        var corpus = RequireCorpus(seedDocument);
        Equal(7, corpus.HeroCount, "canonical hero count");
#if !REMOTE_EMBEDDED_DATA_PREPARED
        var expectedSeed = ContractObject();
        var contractHeroes = Heroes(expectedSeed);
        expectedSeed["heroes"] = new JObject(
            new[] { "Vanessa", "Pygmalien", "Dooley", "Mak", "Jules", "Karnok", "Stelle" }.Select(
                hero => new JProperty(
                    hero,
                    contractHeroes[hero]?.DeepClone()
                        ?? new JObject { ["builds"] = new JArray(), ["card_index"] = new JArray() }
                )
            )
        );
        True(
            JToken.DeepEquals(expectedSeed, JObject.Parse(seedDocument)),
            "fallback matches the deterministic generator input"
        );
        Equal(1, corpus.BuildCount, "fallback build count");
        Equal(
            0,
            corpus.FindBuilds("Karnok", new[] { FirstCard }, BuildLiveState.Empty).Count,
            "empty canonical hero"
        );
#endif
        return Task.CompletedTask;
    }

    private static Task UsesWindowEndForFreshness()
    {
        var summary = new TenWinCorpusSummary(DateTimeOffset.Parse("2026-08-11T00:00:00Z"), 1, 7);
        var line = LiveBuildPanelText.CorpusFreshnessLine(
            summary,
            DateTimeOffset.Parse("2026-08-12T02:00:00Z")
        );
        True(line.StartsWith("updated 1d ago", StringComparison.Ordinal), "relative window end");
        return Task.CompletedTask;
    }

    private static string EmbeddedFallbackPath()
    {
#if REMOTE_EMBEDDED_DATA_PREPARED
        return Path.Combine(
            AppContext.BaseDirectory,
            "PreparedData",
            "analyzer-v5-builds-latest.json"
        );
#else
        var root = RepositoryRoot();
        return Path.Combine(
            root,
            "src",
            "BazaarPlusPlus",
            "Data",
            "BuildRecommendations",
            "tenwin_builds.json"
        );
#endif
    }

    private static async Task PreservesFreshCacheBehavior()
    {
        var now = new DateTime(2026, 8, 12, 3, 0, 0, DateTimeKind.Utc);
        var cache = new MemoryCache(new CatalogCacheDocument(ContractJson(), now.AddHours(-1)));
        var scheduler = new RecordingScheduler();
        using var catalog = CreateCatalog(cache, ContractJson(), ContractJson(), now, scheduler);

        await catalog.WarmAsync().ConfigureAwait(false);
        True(catalog.TryGet(out var snapshot), "cache snapshot exists");
        Equal(CatalogSource.Cache, snapshot.Source, "fresh cache source");
        Equal(0, scheduler.Count, "fresh cache does not queue refresh");
    }

    private static async Task PreservesRemoteRefreshBehavior()
    {
        var now = new DateTime(2026, 8, 12, 3, 0, 0, DateTimeKind.Utc);
        var cache = new MemoryCache(null);
        var scheduler = new RecordingScheduler();
        var remoteDocument = ContractObject();
        remoteDocument["generated_at"] = "2026-08-12T03:00:00Z";
        using var catalog = CreateCatalog(
            cache,
            ContractJson(),
            remoteDocument.ToString(Formatting.None),
            now,
            scheduler
        );

        await catalog.WarmAsync().ConfigureAwait(false);
        True(catalog.TryGet(out var embedded), "embedded fallback snapshot exists");
        Equal(CatalogSource.Embedded, embedded.Source, "embedded fallback source");
        Equal(1, scheduler.Count, "missing cache queues background refresh");

        var refresh = await catalog.RefreshAsync().ConfigureAwait(false);
        True(refresh.Succeeded, "manual refresh succeeds");
        Equal(CatalogSource.Remote, refresh.Snapshot?.Source, "remote snapshot source");
        Equal(
            remoteDocument.ToString(Formatting.None),
            cache.WrittenDocument,
            "remote document cached"
        );
    }

    private static async Task RejectsOldSchemaCache()
    {
        var now = new DateTime(2026, 8, 12, 3, 0, 0, DateTimeKind.Utc);
        var oldSchema = ContractObject();
        oldSchema["schema_version"] = 1;
        var cache = new MemoryCache(
            new CatalogCacheDocument(oldSchema.ToString(Formatting.None), now.AddHours(-1))
        );
        var scheduler = new RecordingScheduler();
        using var catalog = CreateCatalog(cache, ContractJson(), ContractJson(), now, scheduler);

        await catalog.WarmAsync().ConfigureAwait(false);
        True(catalog.TryGet(out var snapshot), "fallback snapshot exists");
        Equal(CatalogSource.Embedded, snapshot.Source, "old-schema cache fallback source");
        Equal(CatalogIssueKind.CacheInvalid, snapshot.Issue?.Kind, "old-schema cache issue");
        Equal(1, scheduler.Count, "invalid cache queues refresh");
    }

    private static async Task RejectsInvalidRemoteRefresh()
    {
        var now = new DateTime(2026, 8, 12, 3, 0, 0, DateTimeKind.Utc);
        var cache = new MemoryCache(null);
        var scheduler = new RecordingScheduler();
        using var catalog = CreateCatalog(cache, ContractJson(), "{}", now, scheduler);

        await catalog.WarmAsync().ConfigureAwait(false);
        var refresh = await catalog.RefreshAsync().ConfigureAwait(false);
        True(!refresh.Succeeded, "invalid remote refresh fails");
        Equal(CatalogIssueKind.RemoteInvalid, refresh.Issue?.Kind, "invalid remote issue");
        Equal(null, cache.WrittenDocument, "invalid remote is not cached");
        True(catalog.TryGet(out var snapshot), "fallback remains available");
        Equal(CatalogSource.Embedded, snapshot.Source, "fallback remains published");
    }

    private static Task UsesAnalyzerV5Url()
    {
        const string expected =
            "https://bpp-metrics.bazaarplusplus.com/analyzer-v5/builds/latest.json";
        var root = RepositoryRoot();
        var factory = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "BazaarPlusPlus",
                "Game",
                "LiveBuildPanel",
                "Recommendations",
                "TenWinBuildCatalogFactory.cs"
            )
        );
        var targets = File.ReadAllText(
            Path.Combine(root, "src", "BazaarPlusPlus", "RemoteEmbeddedData.targets")
        );
        True(factory.Contains(expected, StringComparison.Ordinal), "runtime URL");
        True(targets.Contains(expected, StringComparison.Ordinal), "build-time URL");
        return Task.CompletedTask;
    }

    private static RemoteEmbeddedCatalog<TenWinBuildCorpus> CreateCatalog(
        MemoryCache cache,
        string embedded,
        string remote,
        DateTime now,
        RecordingScheduler scheduler
    ) =>
        new(
            new CorpusParser(),
            new MemoryEmbeddedSource(embedded),
            cache,
            new MemoryRemoteSource(remote),
            new FixedClock(now),
            scheduler,
            new NullObserver(),
            TimeSpan.FromHours(20)
        );

    private static TenWinBuildMatch SingleMatch(TenWinBuildCorpus corpus)
    {
        var matches = corpus.FindBuilds("Vanessa", new[] { FirstCard }, BuildLiveState.Empty);
        Equal(1, matches.Count, "match count");
        return matches[0];
    }

    // Set BPP_TENWIN_SAMPLE_PATH to a downloaded builds/latest.json to assert the
    // production payload parses; without it the test is a no-op so CI stays hermetic.
    private static Task ParsesOptionalLiveSample()
    {
        var path = Environment.GetEnvironmentVariable("BPP_TENWIN_SAMPLE_PATH");
        if (string.IsNullOrWhiteSpace(path))
            return Task.CompletedTask;

        var corpus = RequireCorpus(File.ReadAllText(path));
        True(corpus.HeroCount >= 1, "live sample exposes at least one hero");
        True(corpus.BuildCount >= 1, "live sample exposes at least one build");
        Console.WriteLine(
            $"     live sample: heroes={corpus.HeroCount} builds={corpus.BuildCount}"
        );
        return Task.CompletedTask;
    }

    private static TenWinBuildCorpus RequireCorpus(string json) =>
        TenWinBuildCorpus.Parse(json)
        ?? throw new InvalidOperationException("payload was rejected");

    private static void AcceptWindow(string start, string end, int days)
    {
        var root = ContractObject();
        root["window"] = new JObject
        {
            ["start"] = start,
            ["end"] = end,
            ["days"] = days,
        };
        True(
            TenWinBuildCorpus.Parse(root.ToString(Formatting.None)) != null,
            $"{days}-day window should be accepted"
        );
    }

    private static void RejectWindow(string start, string end, int days)
    {
        var root = ContractObject();
        root["window"] = new JObject
        {
            ["start"] = start,
            ["end"] = end,
            ["days"] = days,
        };
        True(
            TenWinBuildCorpus.Parse(root.ToString(Formatting.None)) == null,
            $"{days}-day window should be rejected"
        );
    }

    private static void Reject(Action<JObject> mutate, string scenario)
    {
        var root = ContractObject();
        mutate(root);
        True(
            TenWinBuildCorpus.Parse(root.ToString(Formatting.None)) == null,
            $"{scenario} should be rejected"
        );
    }

    private static JObject ContractObject() => JObject.Parse(ContractJson());

    private static string ContractJson()
    {
        using var stream = Assembly
            .GetExecutingAssembly()
            .GetManifestResourceStream(ContractResourceName);
        if (stream == null)
            throw new InvalidOperationException("contract fixture is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static JObject Heroes(JObject root) => (JObject)root["heroes"]!;

    private static JObject Hero(JObject root) => (JObject)Heroes(root)["Vanessa"]!;

    private static JArray BuildRow(JObject root) => (JArray)((JArray)Hero(root)["builds"]!)[0]!;

    private static JArray CardRefs(JObject root) => (JArray)BuildRow(root)[0]!;

    private static JArray LayoutRow(JObject root, int index) =>
        (JArray)((JArray)BuildRow(root)[1]!)[index]!;

    private static JArray StatsRow(JObject root) => (JArray)BuildRow(root)[2]!;

    private static JArray CardIndexPair(JObject root, int index) =>
        (JArray)((JArray)Hero(root)["card_index"]!)[index]!;

    private static JArray Schema(JObject root, string name) =>
        (JArray)((JObject)root["schemas"]!)[name]!;

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (
                File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "src",
                        "BazaarPlusPlus",
                        "BazaarPlusPlus.csproj"
                    )
                )
            )
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("repository root not found");
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private sealed class CorpusParser : ICatalogParser<TenWinBuildCorpus>
    {
        public CatalogParseResult<TenWinBuildCorpus> Parse(string document, CatalogSource source)
        {
            var corpus = TenWinBuildCorpus.Parse(document);
            return corpus == null
                ? CatalogParseResult<TenWinBuildCorpus>.Failure("invalid_response")
                : CatalogParseResult<TenWinBuildCorpus>.Success(corpus);
        }
    }

    private sealed class MemoryEmbeddedSource(string document) : IEmbeddedCatalogSource
    {
        public ValueTask<string?> ReadAsync(CancellationToken cancellationToken) => new(document);
    }

    private sealed class MemoryRemoteSource(string document) : IRemoteCatalogSource
    {
        public ValueTask<string?> DownloadAsync(CancellationToken cancellationToken) =>
            new(document);
    }

    private sealed class MemoryCache(CatalogCacheDocument? document) : ILocalCatalogCache
    {
        private CatalogCacheDocument? _document = document;

        internal string? WrittenDocument { get; private set; }

        public ValueTask<CatalogCacheDocument?> ReadAsync(CancellationToken cancellationToken) =>
            new(_document);

        public ValueTask WriteAsync(string value, CancellationToken cancellationToken)
        {
            WrittenDocument = value;
            _document = new CatalogCacheDocument(value, DateTime.UtcNow);
            return default;
        }
    }

    private sealed class FixedClock(DateTime utcNow) : ICatalogClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class RecordingScheduler : ICatalogRefreshScheduler
    {
        private readonly List<Func<Task>> _work = new();

        internal int Count => _work.Count;

        public void Queue(Func<Task> refresh) => _work.Add(refresh);
    }

    private sealed class NullObserver : IRemoteEmbeddedCatalogObserver<TenWinBuildCorpus>
    {
        public void OnWarmStarted() { }

        public void OnInitialLoad(CatalogInitialLoadResult<TenWinBuildCorpus> result) { }

        public void OnRefreshQueued(CatalogIssue reason) { }

        public void OnRefreshCompleted(
            CatalogRefreshTrigger trigger,
            CatalogRefreshResult<TenWinBuildCorpus> result
        ) { }
    }
}
