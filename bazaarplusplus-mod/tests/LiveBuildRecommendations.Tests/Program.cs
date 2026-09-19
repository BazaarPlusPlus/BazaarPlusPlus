using System.Collections;
using System.Reflection;
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.GameInterop.Heroes;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using BazaarPlusPlus.Infrastructure.UiTokens;
using Json.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Behavior tests for the analyzer-v5 ten-win build corpus consumed by LiveBuildPanel.
// The payload is the compact string-table + schema-driven array-row format published at
// analyzer-v5/builds/latest.json. Every assertion drives the public
// BuildRecommendationRepository.FindRecommendations surface (parse + recall + scoring +
// board projection). Catalog lifecycle is tested through IRemoteEmbeddedCatalog in the dedicated
// RemoteEmbeddedCatalog.Tests project; this feature project keeps parser/query/summary and manual
// refresh product policy coverage.

TenWinBuildTests.Run();

internal static class TenWinBuildTests
{
    private const string ContractResourceName =
        "LiveBuildRecommendations.Tests.analyzer-v5-schema2-contract.json";
    private const string GuidA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string GuidB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string GuidC = "cccccccc-cccc-cccc-cccc-cccccccccccc";
    private const string GuidD = "dddddddd-dddd-dddd-dddd-dddddddddddd";

    public static void Run()
    {
        RegisterAssemblyResolution();

        TestParserAcceptsAnalyzerV5Contract();
        TestParserEnforcesTopLevelAndExactSchemas();
        TestParserEnforcesWindowContract();
        TestParserRejectsIllegalIndexesAndSemanticMismatches();
        TestParserAcceptsDuplicateCardRefsNullableP75AndEmptyHero();
        TestRemoteUrlsUseAnalyzerV5Latest();
        TestOptionalLiveSampleParses();
        TestFindRecommendationsHasNoRatingTierParam();
        TestDefaultCachePathUsesBuildsFileName();
        TestSingleCardSelectionResolvesViaCardIndex();
        TestMultiCardSelectionPrefersIntersection();
        TestUnionFallbackWhenIntersectionEmptyButAllCovered();
        TestUnionFallbackKeepsResultsWhenOneSelectedCardUncovered();
        TestSelectingOnlyUncoveredCardsReturnsEmpty();
        TestLiveStateRankingOutranksScore();
        TestBoardContractMapsTierEnchantSize();
        TestNullEnchantRefAndP75DoNotCrash();
        TestRefreshServicePreservesSessionProductSemantics();
        TestPanelInvalidationRejectsUiContinuationWithoutCancelingCatalogRefresh();
        TestEmbeddedSeedResourceIsBundledAndParses();
        TestCorpusSummaryIncludesPerHeroBuildCounts();
        TestMixedAliasCorpusMergesCanonicalFirstByBuildIdentity();
        TestNonPlayableCorpusKeysCannotBeQueried();
        TestCorpusTooltipsUseCanonicalPresentation();
        TestLegacyCacheAndCanonicalRemoteFixturesShareOneIdentity();
        TestExistingSevenHeroesKeepRecommendationResults();

        Console.WriteLine("LiveBuild recommendation checks passed.");
    }

    // ---- Tests ------------------------------------------------------------

    private static void TestParserAcceptsAnalyzerV5Contract()
    {
        using var schemaStream =
            Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream("LiveBuildRecommendations.Tests.builds.schema.json")
            ?? throw new InvalidOperationException("The analyzer-owned builds schema is missing.");
        using var schemaDocument = System.Text.Json.JsonDocument.Parse(schemaStream);
        var schema = JsonSchema.Build(schemaDocument.RootElement);
        using var fixture = System.Text.Json.JsonDocument.Parse(ContractJson());
        var validation = schema.Evaluate(
            fixture.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List }
        );
        Assert(
            validation.IsValid,
            "The contract fixture must satisfy the analyzer-owned builds schema: "
                + System.Text.Json.JsonSerializer.Serialize(validation)
        );
        var corpus = RequireCorpus(ContractJson());
        Assert(corpus.HeroCount == 8, "The contract fixture should contain all eight heroes.");
        Assert(corpus.BuildCount == 1, "The contract fixture should contain one build.");
        Assert(
            corpus.GeneratedAtUtc == DateTimeOffset.Parse("2026-08-12T02:00:00Z"),
            "generated_at should parse as UTC."
        );
        Assert(
            corpus.WindowEndUtc == DateTimeOffset.Parse("2026-08-11T00:00:00Z"),
            "window.end should parse as the freshness timestamp."
        );

        var match = corpus
            .FindBuilds(
                "Vanessa",
                [Guid.Parse("11111111-1111-1111-1111-111111111111")],
                BuildLiveState.Empty
            )
            .Single();
        Assert(match.Build.Layout.Count == 5, "The contract layout should retain five cards.");
        Assert(match.Build.Stats.CompletedRunCount == 123, "Completed runs should parse.");
        Assert(match.Build.Stats.TenWinRunCount == 45, "Ten-win runs should parse.");
        Assert(match.Build.Stats.TenWinRateBps == 3659, "Basis-point rate should parse.");
        Assert(match.Build.Stats.P75TenWinFinalDay == 13, "p75 day should parse.");
        Assert(match.Build.Stats.Score == 421037, "Score should parse.");
        Assert(match.Build.Layout[0].EnchantName == null, "Enchantment ref zero must be null.");
        Assert(match.Build.Layout[1].EnchantName == "Burn", "Named enchantments should resolve.");

        using var catalog = new StubCatalog(corpus);
        var summary = new BuildRecommendationRepository(catalog).GetCorpusSummary()!.Value;
        Assert(
            summary.WindowEndUtc == DateTimeOffset.Parse("2026-08-11T00:00:00Z"),
            "Recommendation freshness should project window.end rather than generated_at."
        );
    }

    private static void TestParserEnforcesTopLevelAndExactSchemas()
    {
        RejectContract(root => root["schema_version"] = 1, "schema_version 1");
        RejectContract(root => root["schema_version"] = 3, "schema_version 3");
        RejectContract(root => root["kind"] = "mod_tenwin_builds", "legacy kind");
        RejectContract(root => root["heroes"] = new JArray(), "heroes array");

        foreach (var schemaName in new[] { "build", "layout", "stats" })
        {
            RejectContract(
                root => ContractSchema(root, schemaName).Add("unexpected"),
                $"extra {schemaName} column"
            );
        }

        RejectContract(
            root =>
            {
                var schema = ContractSchema(root, "build");
                (schema[0], schema[1]) = (schema[1], schema[0]);
            },
            "reordered build columns"
        );
    }

    private static void TestParserEnforcesWindowContract()
    {
        AcceptWindow("2026-08-11", "2026-08-11", 1);
        AcceptWindow("2026-08-07", "2026-08-11", 5);
        AcceptWindow("2026-08-05", "2026-08-11", 7);
        RejectWindow("2026-08-11", "2026-08-11", 0);
        RejectWindow("2026-08-04", "2026-08-11", 8);
        RejectWindow("2026-08-07", "2026-08-11", 4);
    }

    private static void TestParserRejectsIllegalIndexesAndSemanticMismatches()
    {
        RejectContract(root => ContractCardRefs(root)[0] = 99, "card_refs card ref");
        RejectContract(root => ContractLayoutRow(root, 0)[0] = 99, "layout card ref");
        RejectContract(root => ContractLayoutRow(root, 0)[3] = 99, "layout enchant ref");
        RejectContract(root => ContractCardIndexPair(root, 0)[0] = 99, "card_index card ref");
        RejectContract(
            root => ((JArray)ContractCardIndexPair(root, 0)[1]!)[0] = 99,
            "card_index build ID"
        );
        RejectContract(
            root => ((JArray)root["enchantments"]!)[0] = "Burn",
            "non-null enchantment ref zero"
        );
        RejectContract(root => ContractBuildRow(root).Add(new JArray()), "extra build column");
        RejectContract(root => ContractStatsRow(root).Add(1), "extra stats column");
        RejectContract(
            root => ContractLayoutRow(root, 0)[4] = 1,
            "layout that does not occupy ten slots"
        );
        RejectContract(
            root => ((JArray)ContractHero(root)["card_index"]!).RemoveAt(0),
            "incomplete card_index"
        );
        RejectContract(
            root =>
            {
                var refs = ContractCardRefs(root);
                (refs[0], refs[1]) = (refs[1], refs[0]);
            },
            "unsorted card_refs"
        );
        RejectContract(
            root => ContractCardRefs(root)[0] = ContractCardRefs(root)[1]!.DeepClone(),
            "card_refs/layout multiset mismatch"
        );
    }

    private static void TestParserAcceptsDuplicateCardRefsNullableP75AndEmptyHero()
    {
        var root = JObject.Parse(ScorePayload("Vanessa", 333, p75: null));
        ((JObject)root["heroes"]!)["Dooley"] = new JObject
        {
            ["builds"] = new JArray(),
            ["card_index"] = new JArray(),
        };

        var corpus = RequireCorpus(root.ToString(Formatting.None));
        Assert(corpus.HeroCount == 2, "An empty hero should remain represented.");
        var match = corpus
            .FindBuilds("Vanessa", [Guid.Parse(GuidA)], BuildLiveState.Empty)
            .Single();
        Assert(
            match.Build.Layout.Count > match.Build.TemplateIdSet.Count,
            "Repeated copies should remain in layout while recall uses a deduped template set."
        );
        Assert(match.Build.Stats.P75TenWinFinalDay == null, "p75 should accept null.");
        Assert(
            corpus.FindBuilds("Dooley", [Guid.Parse(GuidA)], BuildLiveState.Empty).Count == 0,
            "An empty hero should have no recommendations."
        );
    }

    private static void TestRemoteUrlsUseAnalyzerV5Latest()
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
        Assert(factory.Contains(expected, StringComparison.Ordinal), "Runtime URL should use v5.");
        Assert(targets.Contains(expected, StringComparison.Ordinal), "Build URL should use v5.");
    }

    // Set BPP_TENWIN_SAMPLE_PATH to a downloaded builds/latest.json to validate a live sample.
    // Without it this remains a no-op so the normal test run is hermetic.
    private static void TestOptionalLiveSampleParses()
    {
        var path = Environment.GetEnvironmentVariable("BPP_TENWIN_SAMPLE_PATH");
        if (string.IsNullOrWhiteSpace(path))
            return;

        var corpus = RequireCorpus(File.ReadAllText(path));
        Assert(corpus.HeroCount > 0, "The live sample should contain heroes.");
        Assert(corpus.BuildCount > 0, "The live sample should contain builds.");
        Console.WriteLine(
            $"Live ten-win sample: heroes={corpus.HeroCount} builds={corpus.BuildCount}"
        );
    }

    private static void TestFindRecommendationsHasNoRatingTierParam()
    {
        var repositoryType = GetRepositoryType();

        var find = repositoryType.GetMethod("FindRecommendations");
        Assert(find != null, "Repository should expose FindRecommendations.");
        var parameters = find!.GetParameters();
        Assert(
            parameters.Length == 3,
            "FindRecommendations should take (hero, selectedTemplateIds, liveState)."
        );
        Assert(
            parameters[0].ParameterType == typeof(string),
            "First parameter should be the hero string."
        );
        Assert(
            !parameters.Skip(1).Any(p => p.ParameterType == typeof(string)),
            "FindRecommendations must not take a string rating-tier parameter."
        );

        Assert(
            repositoryType.GetMethod("FindFinalRecommendations") == null,
            "Legacy tier-based FindFinalRecommendations must be removed."
        );
        Assert(
            repositoryType.GetMethod("TryFindFinalRecommendation") == null,
            "Legacy TryFindFinalRecommendation must be removed."
        );

        var ratingTierType = repositoryType.Assembly.GetType(
            "BazaarPlusPlus.Game.LiveBuildPanel.Recommendations.BuildRatingTier"
        );
        Assert(
            ratingTierType == null,
            "BuildRatingTier must be deleted from the production assembly."
        );
    }

    private static void TestDefaultCachePathUsesBuildsFileName()
    {
        var dataRootPath = Path.Combine(Path.GetTempPath(), $"bpp-data-root-{Guid.NewGuid():N}");
        var cachePath = TenWinBuildCatalogFactory.BuildCacheFilePath(dataRootPath);

        Assert(
            cachePath == Path.Combine(dataRootPath, "builds.json"),
            "Ten-win build cache should live under the V5 data root."
        );
    }

    private static void TestRefreshServicePreservesSessionProductSemantics()
    {
        var corpus = TenWinBuildCorpus.Parse(ScorePayload("CacheHero", 333))!;
        using var catalog = new StubCatalog(corpus);
        var repository = new BuildRecommendationRepository(catalog);
        var service = new BuildRecommendationRefreshService();

        catalog.Enqueue(
            CatalogRefreshResult<TenWinBuildCorpus>.Failure(
                new CatalogIssue(
                    CatalogIssueKind.RemoteDownloadFailed,
                    new InvalidOperationException("refresh-boom")
                )
            )
        );
        var failure = service
            .RefreshAsync(repository, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert(!failure.Succeeded, "A failed pull should surface as a product failure.");
        Assert(
            failure.Error?.Contains("refresh-boom") == true,
            "The failure should retain its typed diagnostic detail."
        );

        catalog.Enqueue(
            CatalogRefreshResult<TenWinBuildCorpus>.Published(
                Snapshot(corpus, CatalogSource.Remote),
                degraded: false
            )
        );
        var firstSuccess = service
            .RefreshAsync(repository, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert(
            firstSuccess.Outcome == BuildRecommendationRefreshOutcome.Updated,
            "The first successful remote pull must be Updated even when the payload is identical."
        );
        Assert(catalog.RefreshCount == 2, "Failure must not consume the session pull allowance.");

        var gated = service
            .RefreshAsync(repository, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert(
            gated.Outcome == BuildRecommendationRefreshOutcome.NoChange,
            "Only a session-gated pull should report NoChange."
        );
        Assert(catalog.RefreshCount == 2, "The gated pull must perform zero downloads.");
    }

    private static void TestPanelInvalidationRejectsUiContinuationWithoutCancelingCatalogRefresh()
    {
        var initialCorpus = TenWinBuildCorpus.Parse(ScorePayload("InitialHero", 111))!;
        var refreshedCorpus = TenWinBuildCorpus.Parse(ScorePayload("RefreshedHero", 222))!;
        using var catalog = new BlockingCatalog(initialCorpus);
        var repository = new BuildRecommendationRepository(catalog);
        var service = new BuildRecommendationRefreshService();
        var continuation = new LiveBuildRefreshContinuationGate();
        var operationVersion = continuation.Capture();

        var refresh = service.RefreshAsync(repository, CancellationToken.None);
        catalog.Started.GetAwaiter().GetResult();
        continuation.Invalidate();
        catalog.Complete(refreshedCorpus);

        var result = refresh.GetAwaiter().GetResult();
        Assert(
            result.Succeeded,
            "The plugin-lifetime catalog refresh should finish after panel loss."
        );
        Assert(
            !continuation.IsCurrent(operationVersion),
            "The destroyed panel must reject its stale UI continuation."
        );
        var summary = repository.GetCorpusSummary();
        Assert(summary.HasValue, "The shared repository should retain the refreshed snapshot.");
        var loadedSummary = summary.GetValueOrDefault();
        Assert(
            loadedSummary.HeroBuildCounts.Any(row => row.Hero == "RefreshedHero"),
            "The shared snapshot should publish even though the initiating panel was destroyed."
        );
    }

    private static void TestSingleCardSelectionResolvesViaCardIndex()
    {
        WithCorpus(
            MainRecallPayload("Vanessa"),
            (repositoryType, repository) =>
            {
                var scores = ScoresOf(Find(repositoryType, repository, "Vanessa", [GuidA]));
                // cardIndex[A] = [build0(score100), build1(score200)] -> ranked by score desc.
                Assert(
                    scores.SequenceEqual([200L, 100L]),
                    $"Single-card recall should return A's builds by score; got [{string.Join(",", scores)}]."
                );
            }
        );
    }

    private static void TestMultiCardSelectionPrefersIntersection()
    {
        WithCorpus(
            MainRecallPayload("Vanessa"),
            (repositoryType, repository) =>
            {
                var scores = ScoresOf(Find(repositoryType, repository, "Vanessa", [GuidA, GuidB]));
                // cardIndex[A]=[0,1], cardIndex[B]=[0,2] -> intersection {0} (score100).
                Assert(
                    scores.SequenceEqual([100L]),
                    $"Intersection of A and B should return only build0; got [{string.Join(",", scores)}]."
                );
            }
        );
    }

    private static void TestUnionFallbackWhenIntersectionEmptyButAllCovered()
    {
        // cards A,B both covered but with disjoint build sets -> intersection empty -> union.
        var payload = Payload(
            cards: $"[\"{GuidA}\",\"{GuidB}\"]",
            enchantments: "[null]",
            hero: "Dooley",
            builds: $"[{Build("[0]", "[[0,0,1,0,1]]", 10)},{Build("[1]", "[[1,0,1,0,1]]", 20)}]",
            cardIndex: "[[0,[0]],[1,[1]]]"
        );
        WithCorpus(
            payload,
            (repositoryType, repository) =>
            {
                var scores = ScoresOf(Find(repositoryType, repository, "Dooley", [GuidA, GuidB]));
                Assert(
                    scores.Count == 2,
                    $"Disjoint intersection should fall back to the union of both builds; got {scores.Count}."
                );
            }
        );
    }

    private static void TestUnionFallbackKeepsResultsWhenOneSelectedCardUncovered()
    {
        WithCorpus(
            MainRecallPayload("Vanessa"),
            (repositoryType, repository) =>
            {
                // C is present in cards[] but absent from this hero's cardIndex -> uncovered.
                // The literal intersection is empty, so union over the covered card (A) must still return results.
                var scores = ScoresOf(Find(repositoryType, repository, "Vanessa", [GuidA, GuidC]));
                Assert(
                    scores.Count == 2,
                    $"An uncovered selected card must not empty the result while A has coverage; got {scores.Count}."
                );
            }
        );
    }

    private static void TestSelectingOnlyUncoveredCardsReturnsEmpty()
    {
        WithCorpus(
            MainRecallPayload("Vanessa"),
            (repositoryType, repository) =>
            {
                // D is absent from cards[] entirely; C is in cards[] but not in cardIndex. Neither has coverage.
                Assert(
                    ScoresOf(Find(repositoryType, repository, "Vanessa", [GuidD])).Count == 0,
                    "An unknown selected card should return no recommendation."
                );
                Assert(
                    ScoresOf(Find(repositoryType, repository, "Vanessa", [GuidC])).Count == 0,
                    "A selected card with no historical coverage should return no recommendation."
                );
            }
        );
    }

    private static void TestLiveStateRankingOutranksScore()
    {
        WithCorpus(
            MainRecallPayload("Vanessa"),
            (repositoryType, repository) =>
            {
                // Selecting A returns build0(score100, contains B) and build1(score200, A only).
                // With no live state, build1 wins on score.
                Assert(
                    ScoresOf(Find(repositoryType, repository, "Vanessa", [GuidA])).First() == 200L,
                    "Without live state the higher-score build ranks first."
                );

                // Put B on the board: build0 gains live-state weight and must outrank the higher-score build1.
                var liveState = LiveState(repositoryType, board: [GuidB], stash: [], shop: []);
                var ranked = ScoresOf(
                    Find(repositoryType, repository, "Vanessa", [GuidA], liveState)
                );
                Assert(
                    ranked.First() == 100L,
                    $"A board match should outrank a higher analyzer score; got first={ranked.First()}."
                );
            }
        );
    }

    private static void TestBoardContractMapsTierEnchantSize()
    {
        var payload = Payload(
            cards: $"[\"{GuidA}\"]",
            enchantments: "[null,\"Fiery\"]",
            hero: "Mak",
            // layout: cardRef0, slot3, tier5(Legendary), enchantRef1(Fiery), size2(Medium)
            builds: $"[{Build("[0]", "[[0,3,5,1,2]]", 1)}]",
            cardIndex: "[[0,[0]]]"
        );
        WithCorpus(
            payload,
            (repositoryType, repository) =>
            {
                var recommendation = Find(repositoryType, repository, "Mak", [GuidA])
                    .Cast<object>()
                    .Single();
                var board = Prop(recommendation, "Board")!;
                Assert(
                    Prop(board, "Id")!.ToString() == "FinalBuild",
                    "Recommendation board Id should be FinalBuild."
                );
                Assert(
                    Prop(board, "Type")!.ToString() == "Reference",
                    "Recommendation board Type should be Reference."
                );

                var cards = ((IEnumerable)Prop(board, "Cards")!).Cast<object>().ToList();
                var card = cards.Single(candidate =>
                    Prop(candidate, "SourceSocketId")?.ToString() == "Socket_3"
                );
                Assert(
                    Prop(card, "Tier")!.ToString() == "Legendary",
                    "Tier value 5 should map to ETier.Legendary."
                );
                Assert(
                    Prop(card, "Size")!.ToString() == "Medium",
                    "Size 2 should map to ECardSize.Medium."
                );
                Assert(
                    Prop(card, "EnchantmentType")?.ToString() == "Fiery",
                    "enchantRef 1 should resolve to EEnchantmentType.Fiery."
                );
                Assert(
                    Prop(card, "SourceSocketId")?.ToString() == "Socket_3",
                    "Slot 3 should map to EContainerSocketId.Socket_3."
                );

                // tenWinRunCount / score survive into the recommendation DTO for UI evidence.
                Assert(
                    (int)Prop(recommendation, "TenWinRunCount")! == 80,
                    "TenWinRunCount should be carried from stats."
                );
                Assert(
                    (long)Prop(recommendation, "Score")! == 1L,
                    "Score should be carried from stats."
                );
            }
        );
    }

    private static void TestNullEnchantRefAndP75DoNotCrash()
    {
        var payload = Payload(
            cards: $"[\"{GuidA}\"]",
            enchantments: "[null]",
            hero: "Jules",
            builds: $"[{Build("[0]", "[[0,0,1,0,3]]", 5, p75: null)}]",
            cardIndex: "[[0,[0]]]"
        );
        WithCorpus(
            payload,
            (repositoryType, repository) =>
            {
                var recommendation = Find(repositoryType, repository, "Jules", [GuidA])
                    .Cast<object>()
                    .Single();
                var board = Prop(recommendation, "Board")!;
                var card = ((IEnumerable)Prop(board, "Cards")!).Cast<object>().First();
                Assert(
                    Prop(card, "Tier")!.ToString() == "Bronze",
                    "Tier value one should project as Bronze."
                );
                Assert(
                    Prop(card, "EnchantmentType") == null,
                    "enchantRef 0 should resolve to no enchantment."
                );
                Assert(
                    Prop(recommendation, "P75TenWinFinalDay") == null,
                    "A null p75 should remain null in the recommendation."
                );
            }
        );
    }

    private static void TestEmbeddedSeedResourceIsBundledAndParses()
    {
        var source = new AssemblyResourceCatalogSource(
            typeof(TenWinBuildCorpus).Assembly,
            TenWinBuildCatalogFactory.EmbeddedResourceName
        );
        var json = source.ReadAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        Assert(
            !string.IsNullOrWhiteSpace(json),
            "The bundled builds.json seed should be embedded in the assembly."
        );

        var corpus = TenWinBuildCorpus.Parse(json);
        Assert(corpus != null, "The bundled seed should parse with the production corpus parser.");
        Assert(
            corpus!.HeroCount == 8,
            "The bundled seed should contain all eight canonical heroes."
        );
#if !REMOTE_EMBEDDED_DATA_PREPARED
        Assert(
            corpus.GeneratedAtUtc == DateTimeOffset.Parse("2026-08-12T02:00:00Z"),
            "The bundled seed should carry its deterministic generated_at timestamp."
        );
        Assert(
            corpus.WindowEndUtc == DateTimeOffset.Parse("2026-08-11T00:00:00Z"),
            "The bundled seed should carry its deterministic window.end timestamp."
        );
#endif
        Assert(corpus.BuildCount > 0, "The bundled seed should count at least one build.");
#if !REMOTE_EMBEDDED_DATA_PREPARED
        Assert(corpus.BuildCount == 1, "The deterministic fallback should contain one seed build.");
#endif
        Assert(
            corpus.HeroBuildCounts.All(row => row.Hero != "Hero8"),
            "Any The Dragons data present in the embedded seed must use the canonical summary identity."
        );
#if !REMOTE_EMBEDDED_DATA_PREPARED
        Assert(
            corpus.HeroBuildCounts.Any(row => row.Hero == "TheDragons" && row.BuildCount == 0),
            "The deterministic fallback should retain an empty canonical TheDragons hero."
        );
#else
        Assert(
            corpus.HeroBuildCounts.Any(row => row.Hero == "TheDragons"),
            "Prepared data should retain the canonical TheDragons hero."
        );
#endif
    }

    private static void TestCorpusSummaryIncludesPerHeroBuildCounts()
    {
        WithCorpus(
            TwoHeroSummaryPayload(),
            (repositoryType, repository) =>
            {
                var getSummary = repositoryType.GetMethod("GetCorpusSummary");
                Assert(getSummary != null, "Repository should expose corpus summary.");
                var summary = getSummary!.Invoke(repository, null);
                Assert(summary != null, "Loaded corpus summary should not be null.");

                Assert((int)Prop(summary!, "HeroCount")! == 2, "Summary should count both heroes.");
                Assert(
                    (int)Prop(summary!, "BuildCount")! == 3,
                    "Summary should count all build rows across heroes."
                );

                var heroCounts = ((IEnumerable)Prop(summary!, "HeroBuildCounts")!)
                    .Cast<object>()
                    .Select(row => ((string)Prop(row, "Hero")!, (int)Prop(row, "BuildCount")!))
                    .ToList();
                Assert(heroCounts.Count == 2, "Summary should expose both hero count rows.");
                Assert(
                    heroCounts[0] == ("Vanessa", 2),
                    "Hero count rows should sort by descending build count."
                );
                Assert(
                    heroCounts[1] == ("Dooley", 1),
                    "Hero count rows should preserve exact hero names and counts."
                );
            }
        );
    }

    private static void TestMixedAliasCorpusMergesCanonicalFirstByBuildIdentity()
    {
        var canonicalBuild0 = Build("[0]", "[[0,0,1,0,1]]", 900);
        var canonicalBuild1 = Build("[1]", "[[1,1,1,0,1]]", 800);
        var legacyBuild0 = Build("[2]", "[[2,2,1,0,1]]", 100);
        var legacyBuild1 = Build("[2]", "[[2,3,1,0,1]]", 200);
        var legacyBuild2 = Build("[0]", "[[0,4,1,0,1]]", 600);
        var canonicalHero = $$"""
            "TheDragons": {
              "builds": [{{canonicalBuild0}},{{canonicalBuild1}}],
              "card_index": [[0,[0]],[1,[1]]]
            }
            """;
        var legacyHero = $$"""
            "Hero8": {
              "builds": [{{legacyBuild0}},{{legacyBuild1}},{{legacyBuild2}}],
              "card_index": [[0,[2]],[2,[0,1]]]
            }
            """;

        foreach (
            var heroes in new[] { $"{legacyHero},{canonicalHero}", $"{canonicalHero},{legacyHero}" }
        )
        {
            WithCorpus(
                PayloadWithHeroes(
                    cards: $"[\"{GuidA}\",\"{GuidB}\",\"{GuidC}\"]",
                    enchantments: "[null]",
                    heroes: heroes
                ),
                (repositoryType, repository) =>
                {
                    var canonicalScores = ScoresOf(
                        Find(repositoryType, repository, "TheDragons", [GuidA])
                    );
                    var legacyScores = ScoresOf(Find(repositoryType, repository, "Hero8", [GuidA]));
                    Assert(
                        canonicalScores.SequenceEqual([900L, 600L]),
                        "Canonical alias entries should win duplicate build IDs, while a legacy-only "
                            + $"build ID remains queryable; got [{string.Join(",", canonicalScores)}]."
                    );
                    Assert(
                        legacyScores.SequenceEqual(canonicalScores),
                        "Legacy and canonical runtime queries should resolve the same merged build set."
                    );

                    var summary = repositoryType
                        .GetMethod("GetCorpusSummary")!
                        .Invoke(repository, null)!;
                    Assert(
                        (int)Prop(summary, "HeroCount")! == 1,
                        "Both aliases should contribute one canonical hero."
                    );
                    Assert(
                        (int)Prop(summary, "BuildCount")! == 3,
                        "Duplicate alias build IDs should not inflate the total build count."
                    );
                    var heroCounts = ((IEnumerable)Prop(summary, "HeroBuildCounts")!)
                        .Cast<object>()
                        .Select(row => ((string)Prop(row, "Hero")!, (int)Prop(row, "BuildCount")!))
                        .ToList();
                    Assert(
                        heroCounts.SequenceEqual([("TheDragons", 3)]),
                        "Summary should expose one canonical TheDragons count row."
                    );
                }
            );
        }
    }

    private static void TestNonPlayableCorpusKeysCannotBeQueried()
    {
        var build = Build("[0]", "[[0,0,1,0,1]]", 500);
        WithCorpus(
            PayloadWithHeroes(
                cards: $"[\"{GuidA}\"]",
                enchantments: "[null]",
                heroes: $$"""
                    "Common": {
                      "builds": [{{build}}],
                      "card_index": [[0,[0]]]
                    },
                    "UnknownHero": {
                      "builds": [{{build}}],
                      "card_index": [[0,[0]]]
                    }
                """
            ),
            (repositoryType, repository) =>
            {
                Assert(
                    !Find(repositoryType, repository, "Common", [GuidA]).Cast<object>().Any(),
                    "Common corpus rows must not be queryable as playable recommendations."
                );
                Assert(
                    !Find(repositoryType, repository, "UnknownHero", [GuidA]).Cast<object>().Any(),
                    "Unknown corpus rows must not be queryable as playable recommendations."
                );
            }
        );
    }

    private static void TestCorpusTooltipsUseCanonicalPresentation()
    {
        var withBuilds = new TenWinCorpusSummary(
            windowEndUtc: null,
            buildCount: 18,
            heroCount: 4,
            [
                new TenWinHeroBuildCount("Hero8", 4),
                new TenWinHeroBuildCount("Vanessa", 2),
                new TenWinHeroBuildCount("Common", 5),
                new TenWinHeroBuildCount("UnknownHero", 7),
            ]
        );
        Assert(
            LiveBuildHeroPresentation.DisplayName("Hero8") == "The Dragons",
            "A legacy tile or tooltip identity should display the canonical native hero name."
        );
        var tooltip = LiveBuildPanelText.CorpusSummaryTooltip(withBuilds);
        Assert(
            tooltip.Contains("The Dragons 4", StringComparison.Ordinal)
                && !tooltip.Contains("Hero8", StringComparison.Ordinal),
            "The corpus summary tooltip must not expose the transitional alias."
        );

        var badge = HeroVisual.Resolve("Hero8");
        Assert(
            badge.ShortCode == "DRA" && badge.Background == Colors.HeroTheDragonsBackground,
            "The Dragons corpus tile should use the shared DRA badge and native cyan."
        );
    }

    private static void TestLegacyCacheAndCanonicalRemoteFixturesShareOneIdentity()
    {
        var parser = new TenWinBuildCatalogParser();
        var embeddedDocument = ScorePayload("Hero8", 222);
        var embedded = parser.Parse(embeddedDocument, CatalogSource.Embedded);
        Assert(embedded.Succeeded, "A legacy embedded-seed fixture should remain parseable.");
        AssertAliasQueriesShareScore(embedded.Value!, 222);

        var legacyCacheDocument = ScorePayload("Hero8", 333);
        var legacyCache = parser.Parse(legacyCacheDocument, CatalogSource.Cache);
        Assert(legacyCache.Succeeded, "A legacy local-cache fixture should remain parseable.");
        Assert(
            legacyCacheDocument.Contains("\"Hero8\"", StringComparison.Ordinal),
            "Parsing must not rewrite the user's raw legacy cache document."
        );
        AssertAliasQueriesShareScore(legacyCache.Value!, 333);

        var canonicalRemoteDocument = ScorePayload("TheDragons", 444);
        var canonicalRemote = parser.Parse(canonicalRemoteDocument, CatalogSource.Remote);
        Assert(canonicalRemote.Succeeded, "A canonical remote fixture should parse.");
        AssertAliasQueriesShareScore(canonicalRemote.Value!, 444);
    }

    private static void TestExistingSevenHeroesKeepRecommendationResults()
    {
        var heroIds = new[]
        {
            "Vanessa",
            "Pygmalien",
            "Dooley",
            "Mak",
            "Jules",
            "Karnok",
            "Stelle",
        };
        var builds =
            $"[{Build("[0,1]", "[[0,0,1,0,1],[1,1,1,0,1]]", 100)},"
            + $"{Build("[0]", "[[0,0,1,0,1]]", 200)},"
            + $"{Build("[1]", "[[1,0,1,0,1]]", 150)}]";
        var heroes = string.Join(
            ",",
            heroIds
                .Reverse()
                .Select(hero =>
                    $$"""
                        "{{hero}}": {
                          "builds": {{builds}},
                          "card_index": [[0,[0,1]],[1,[0,2]]]
                        }
                        """
                )
        );

        WithCorpus(
            PayloadWithHeroes(
                cards: $"[\"{GuidA}\",\"{GuidB}\"]",
                enchantments: "[null]",
                heroes: heroes
            ),
            (repositoryType, repository) =>
            {
                foreach (var hero in heroIds)
                {
                    var scores = ScoresOf(Find(repositoryType, repository, hero, [GuidA]));
                    Assert(
                        scores.SequenceEqual([200L, 100L]),
                        $"{hero} recommendation recall/ranking should remain unchanged."
                    );
                }

                var summary = repositoryType
                    .GetMethod("GetCorpusSummary")!
                    .Invoke(repository, null)!;
                Assert(
                    (int)Prop(summary, "HeroCount")! == 7
                        && (int)Prop(summary, "BuildCount")! == 21,
                    "The existing seven heroes should retain their distinct hero/build counts."
                );
            }
        );
    }

    private static void AssertAliasQueriesShareScore(TenWinBuildCorpus corpus, long expectedScore)
    {
        using var catalog = new StubCatalog(corpus);
        var repository = new BuildRecommendationRepository(catalog);
        foreach (var alias in new[] { "Hero8", "TheDragons" })
        {
            var scores = repository
                .FindRecommendations(alias, [Guid.Parse(GuidA)])
                .Select(recommendation => recommendation.Score)
                .ToArray();
            Assert(
                scores.SequenceEqual([expectedScore]),
                $"{alias} should query the same canonical source fixture."
            );
        }

        var summary = repository.GetCorpusSummary()!.Value;
        Assert(
            summary.HeroCount == 1
                && summary.BuildCount == 1
                && summary.HeroBuildCounts.Single().Hero == "TheDragons",
            "A single-alias source fixture should project one canonical hero/build count."
        );
    }

    private static TenWinBuildCorpus RequireCorpus(string json) =>
        TenWinBuildCorpus.Parse(json)
        ?? throw new InvalidOperationException("The schema-2 payload was rejected.");

    private static void RejectContract(Action<JObject> mutate, string scenario)
    {
        var root = JObject.Parse(ContractJson());
        mutate(root);
        Assert(
            TenWinBuildCorpus.Parse(root.ToString(Formatting.None)) == null,
            $"{scenario} should be rejected."
        );
    }

    private static void AcceptWindow(string start, string end, int days)
    {
        var root = JObject.Parse(ContractJson());
        root["window"] = new JObject
        {
            ["start"] = start,
            ["end"] = end,
            ["days"] = days,
        };
        Assert(
            TenWinBuildCorpus.Parse(root.ToString(Formatting.None)) != null,
            $"A {days}-day consistent window should be accepted."
        );
    }

    private static void RejectWindow(string start, string end, int days)
    {
        var root = JObject.Parse(ContractJson());
        root["window"] = new JObject
        {
            ["start"] = start,
            ["end"] = end,
            ["days"] = days,
        };
        Assert(
            TenWinBuildCorpus.Parse(root.ToString(Formatting.None)) == null,
            $"A {days}-day invalid window should be rejected."
        );
    }

    private static string ContractJson()
    {
        using var stream = Assembly
            .GetExecutingAssembly()
            .GetManifestResourceStream(ContractResourceName);
        if (stream == null)
            throw new InvalidOperationException("The analyzer-v5 contract fixture is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static JObject ContractHeroes(JObject root) => (JObject)root["heroes"]!;

    private static JObject ContractHero(JObject root) => (JObject)ContractHeroes(root)["Vanessa"]!;

    private static JArray ContractBuildRow(JObject root) =>
        (JArray)((JArray)ContractHero(root)["builds"]!)[0]!;

    private static JArray ContractCardRefs(JObject root) => (JArray)ContractBuildRow(root)[0]!;

    private static JArray ContractLayoutRow(JObject root, int index) =>
        (JArray)((JArray)ContractBuildRow(root)[1]!)[index]!;

    private static JArray ContractStatsRow(JObject root) => (JArray)ContractBuildRow(root)[2]!;

    private static JArray ContractCardIndexPair(JObject root, int index) =>
        (JArray)((JArray)ContractHero(root)["card_index"]!)[index]!;

    private static JArray ContractSchema(JObject root, string name) =>
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

        throw new InvalidOperationException("Repository root not found.");
    }

    // ---- Payload builders -------------------------------------------------

    private static string MainRecallPayload(string hero) =>
        Payload(
            cards: $"[\"{GuidA}\",\"{GuidB}\",\"{GuidC}\"]",
            enchantments: "[null,\"Fiery\"]",
            hero: hero,
            builds: $"[{Build("[0,1]", "[[0,0,1,0,1],[1,1,1,0,1]]", 100)},"
                + $"{Build("[0]", "[[0,0,1,0,1]]", 200)},"
                + $"{Build("[1]", "[[1,0,1,0,1]]", 150)}]",
            // A(ref0) -> builds 0,1 ; B(ref1) -> builds 0,2 ; C(ref2) absent (uncovered for this hero).
            cardIndex: "[[0,[0,1]],[1,[0,2]]]"
        );

    private static string ScorePayload(string hero, long score, int? p75 = 13) =>
        Payload(
            cards: $"[\"{GuidA}\"]",
            enchantments: "[null]",
            hero: hero,
            builds: $"[{Build("[0]", "[[0,0,1,0,1]]", score, p75)}]",
            cardIndex: "[[0,[0]]]"
        );

    private static string TwoHeroSummaryPayload()
    {
        var dooleyBuild = Build("[0]", "[[0,0,1,0,1]]", 111);
        var vanessaBuildA = Build("[0]", "[[0,0,1,0,1]]", 222);
        var vanessaBuildB = Build("[1]", "[[1,1,1,0,1]]", 333);

        return PayloadWithHeroes(
            cards: $"[\"{GuidA}\",\"{GuidB}\"]",
            enchantments: "[null]",
            heroes: $$"""
                "Dooley": {
                  "builds": [{{dooleyBuild}}],
                  "card_index": [[0,[0]]]
                },
                "Vanessa": {
                  "builds": [{{vanessaBuildA}},{{vanessaBuildB}}],
                  "card_index": [[0,[0]],[1,[1]]]
                }
            """
        );
    }

    private static string Build(string cardRefs, string layout, long score, int? p75 = 13)
    {
        var refs = JArray.Parse(cardRefs);
        var rows = JArray.Parse(layout);
        var occupied = new bool[10];
        foreach (var row in rows.Cast<JArray>())
        {
            var slot = row[1]!.Value<int>();
            var size = row[4]!.Value<int>();
            for (var socket = slot; socket < slot + size; socket++)
                occupied[socket] = true;
        }

        var fillerCardRef = refs[0]!.Value<int>();
        for (var socket = 0; socket < occupied.Length; )
        {
            if (occupied[socket])
            {
                socket++;
                continue;
            }

            var size = 1;
            while (size < 3 && socket + size < occupied.Length && !occupied[socket + size])
                size++;

            rows.Add(new JArray(fillerCardRef, socket, 1, 0, size));
            refs.Add(fillerCardRef);
            for (var filled = socket; filled < socket + size; filled++)
                occupied[filled] = true;
            socket += size;
        }

        refs = new JArray(refs.OrderBy(token => token.Value<int>()));
        return new JArray(
            refs,
            rows,
            new JArray(300, 80, 2667, p75.HasValue ? p75.Value : JValue.CreateNull(), score)
        ).ToString(Formatting.None);
    }

    private static string Payload(
        string cards,
        string enchantments,
        string hero,
        string builds,
        string cardIndex
    ) =>
        PayloadWithHeroes(
            cards,
            enchantments,
            $$"""
                "{{hero}}": {
                  "builds": {{builds}},
                  "card_index": {{cardIndex}}
                }
            """
        );

    private static string PayloadWithHeroes(string cards, string enchantments, string heroes) =>
        $$"""
            {
              "schema_version": 2,
              "kind": "ten_win_builds",
              "generated_at": "2026-08-12T02:00:00Z",
              "window": { "start": "2026-08-07", "end": "2026-08-11", "days": 5 },
              "cards": {{cards}},
              "enchantments": {{enchantments}},
              "schemas": {
                "build": ["card_refs", "layout", "stats"],
                "layout": ["card_ref", "slot", "tier", "enchant_ref", "size"],
                "stats": ["completed_run_count", "ten_win_run_count", "ten_win_rate_bps", "p75_ten_win_final_day", "score"]
              },
              "heroes": {
                {{heroes}}
              }
            }
            """;

    // ---- Reflection plumbing ----------------------------------------------

    private static void RegisterAssemblyResolution()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var assemblyName = new AssemblyName(args.Name);
            if (
                !string.Equals(
                    assemblyName.Name,
                    "UnityEngine.CoreModule",
                    StringComparison.Ordinal
                )
            )
                return null;

            var assemblyPath = Path.Combine(AppContext.BaseDirectory, "UnityEngine.CoreModule.dll");
            return File.Exists(assemblyPath) ? Assembly.LoadFrom(assemblyPath) : null;
        };
    }

    private static Type GetRepositoryType()
    {
        var assembly = Assembly.Load("BazaarPlusPlus");
        return assembly.GetType(
            "BazaarPlusPlus.Game.LiveBuildPanel.Recommendations.BuildRecommendationRepository"
        )!;
    }

    private static void WithCorpus(string payload, Action<Type, object> body)
    {
        var repositoryType = GetRepositoryType();
        var corpus = RequireCorpus(payload);
        using var catalog = new StubCatalog(corpus);
        body(repositoryType, new BuildRecommendationRepository(catalog));
    }

    private static CatalogSnapshot<TenWinBuildCorpus> Snapshot(
        TenWinBuildCorpus corpus,
        CatalogSource source
    ) =>
        new(
            corpus,
            source,
            new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc),
            IsStale: false,
            Issue: null
        );

    private sealed class StubCatalog : IRemoteEmbeddedCatalog<TenWinBuildCorpus>
    {
        private readonly Queue<CatalogRefreshResult<TenWinBuildCorpus>> _refreshes = new();
        private CatalogSnapshot<TenWinBuildCorpus>? _snapshot;

        internal StubCatalog(TenWinBuildCorpus corpus)
        {
            _snapshot = Snapshot(corpus, CatalogSource.Cache);
        }

        internal int RefreshCount { get; private set; }

        internal void Enqueue(CatalogRefreshResult<TenWinBuildCorpus> result) =>
            _refreshes.Enqueue(result);

        public bool TryGet(out CatalogSnapshot<TenWinBuildCorpus> snapshot)
        {
            if (_snapshot.HasValue)
            {
                snapshot = _snapshot.Value;
                return true;
            }

            snapshot = default;
            return false;
        }

        public ValueTask WarmAsync(CancellationToken cancellationToken = default) => default;

        public ValueTask<CatalogRefreshResult<TenWinBuildCorpus>> RefreshAsync(
            CancellationToken cancellationToken = default
        )
        {
            RefreshCount++;
            var result = _refreshes.Dequeue();
            if (result.Snapshot.HasValue)
                _snapshot = result.Snapshot.Value;
            return ValueTask.FromResult(result);
        }

        public void Dispose() => _snapshot = null;
    }

    private sealed class BlockingCatalog : IRemoteEmbeddedCatalog<TenWinBuildCorpus>
    {
        private readonly TaskCompletionSource<bool> _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<CatalogRefreshResult<TenWinBuildCorpus>> _refresh =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CatalogSnapshot<TenWinBuildCorpus>? _snapshot;

        internal BlockingCatalog(TenWinBuildCorpus corpus)
        {
            _snapshot = Snapshot(corpus, CatalogSource.Cache);
        }

        internal Task Started => _started.Task;

        internal void Complete(TenWinBuildCorpus corpus) =>
            _refresh.SetResult(
                CatalogRefreshResult<TenWinBuildCorpus>.Published(
                    Snapshot(corpus, CatalogSource.Remote),
                    degraded: false
                )
            );

        public bool TryGet(out CatalogSnapshot<TenWinBuildCorpus> snapshot)
        {
            if (_snapshot.HasValue)
            {
                snapshot = _snapshot.Value;
                return true;
            }

            snapshot = default;
            return false;
        }

        public ValueTask WarmAsync(CancellationToken cancellationToken = default) => default;

        public async ValueTask<CatalogRefreshResult<TenWinBuildCorpus>> RefreshAsync(
            CancellationToken cancellationToken = default
        )
        {
            _started.TrySetResult(true);
            var result = await _refresh.Task.ConfigureAwait(false);
            if (result.Snapshot.HasValue)
                _snapshot = result.Snapshot.Value;
            return result;
        }

        public void Dispose() => _snapshot = null;
    }

    private static IEnumerable Find(
        Type repositoryType,
        object repository,
        string hero,
        string[] templateIds,
        object? liveState = null
    )
    {
        var method = repositoryType.GetMethod("FindRecommendations")!;
        var ids = templateIds.Select(Guid.Parse).ToArray();
        return (IEnumerable)method.Invoke(repository, [hero, ids, liveState])!;
    }

    private static object LiveState(
        Type repositoryType,
        string[] board,
        string[] stash,
        string[] shop
    )
    {
        var liveStateType = repositoryType.Assembly.GetType(
            "BazaarPlusPlus.Game.LiveBuildPanel.Recommendations.BuildLiveState"
        )!;
        var from = liveStateType.GetMethod("From")!;
        return from.Invoke(
            null,
            [
                board.Select(Guid.Parse).ToArray(),
                stash.Select(Guid.Parse).ToArray(),
                shop.Select(Guid.Parse).ToArray(),
            ]
        )!;
    }

    private static List<long> ScoresOf(IEnumerable recommendations) =>
        recommendations.Cast<object>().Select(r => (long)Prop(r, "Score")!).ToList();

    private static object? Prop(object target, string name) =>
        target.GetType().GetProperty(name)!.GetValue(target);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
