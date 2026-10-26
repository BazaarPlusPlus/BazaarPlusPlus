using System.Reflection;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using BazaarPlusPlus.TestSupport;
using Json.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Behavior tests for the analyzer ten-win build corpus consumed by LiveBuildPanel, run against
// Core alone. The payload is the compact string-table + schema-driven array-row format published
// at builds/latest.json on the V6 metrics origin. Every assertion drives the public
// BuildRecommendationRepository.FindRecommendations surface (parse + recall + scoring + board
// projection). Catalog lifecycle is tested through IRemoteEmbeddedCatalog in the dedicated
// RemoteEmbeddedCatalog.Tests project; the embedded seed the desktop host bundles is gated by
// LiveBuildRecommendations.Tests.

TenWinBuildTests.Run();

internal static class TenWinBuildTests
{
    // The analyzer-produced golden (contracts/v6/fixtures/builds.latest.json).
    private const string ContractResourceName =
        "LiveBuildRecommendationCore.Tests.builds.latest.json";
    private const string ContractHeroName = "Dooley";
    private const string GuidA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string GuidB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string GuidC = "cccccccc-cccc-cccc-cccc-cccccccccccc";
    private const string GuidD = "dddddddd-dddd-dddd-dddd-dddddddddddd";

    public static void Run()
    {
        TestParserAcceptsAnalyzerContract();
        TestParserEnforcesTopLevelAndExactSchemas();
        TestParserEnforcesWindowContract();
        TestParserRejectsIllegalIndexesAndSemanticMismatches();
        TestParserAcceptsDuplicateCardRefsNullableP75AndEmptyHero();
        TestRemoteUrlsUseMetricsV6Latest();
        TestOptionalLiveSampleParses();
        TestFindRecommendationsTakesHeroSelectionAndLiveState();
        TestDefaultCachePathUsesBuildsFileName();
        TestSingleCardSelectionResolvesViaCardIndex();
        TestMultiCardSelectionPrefersIntersection();
        TestUnionFallbackWhenIntersectionEmptyButAllCovered();
        TestUnionFallbackKeepsResultsWhenOneSelectedCardUncovered();
        TestSelectingOnlyUncoveredCardsReturnsEmpty();
        TestLiveStateRankingOutranksScore();
        TestBoardContractMapsTierEnchantSize();
        TestNullEnchantRefAndP75DoNotCrash();
        TestRemoteRefreshPreservesProductFailure();
        TestCatalogRefreshPublishesSharedSnapshot();
        TestCorpusSummaryIncludesPerHeroBuildCounts();
        TestProjectedBoardsFollowTheirHeroAndSnapshot();
        TestNonPlayableCorpusKeysCannotBeQueried();
        TestCorpusTooltipsUseCanonicalPresentation();
        TestDragonsQueriesShareTheCanonicalRemoteFixture();
        TestExistingSevenHeroesKeepRecommendationResults();
        TestOnlyPlayableHeroesAreQueried();

        Console.WriteLine("LiveBuild recommendation checks passed.");
    }

    // ---- Tests ------------------------------------------------------------

    private static void TestParserAcceptsAnalyzerContract()
    {
        using var schemaStream =
            Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream("LiveBuildRecommendationCore.Tests.builds.schema.json")
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
        Assert(corpus.BuildCount == 4, "The contract fixture should contain four builds.");
        Assert(
            corpus.GeneratedAtUtc == DateTimeOffset.Parse("2026-08-11T00:30:00Z"),
            "generated_at should parse as UTC."
        );
        Assert(
            corpus.WindowEndUtc == DateTimeOffset.Parse("2026-08-10T00:00:00Z"),
            "window.end should parse as the freshness timestamp."
        );

        var match = corpus
            .FindBuilds(
                ContractHeroName,
                [Guid.Parse("11111111-1111-4111-8111-111111111111")],
                BuildLiveState.Empty
            )
            .Single();
        Assert(match.Build.Layout.Count == 4, "The contract layout should retain four cards.");
        Assert(match.Build.Stats.CompletedRunCount == 2, "Completed runs should parse.");
        Assert(match.Build.Stats.TenWinRunCount == 2, "Ten-win runs should parse.");
        Assert(match.Build.Stats.TenWinRateBps == 10000, "Basis-point rate should parse.");
        Assert(match.Build.Stats.P75TenWinFinalDay == 10, "p75 day should parse.");
        Assert(match.Build.Stats.Score == 342372, "Score should parse.");
        Assert(match.Build.Layout[0].EnchantName == null, "Enchantment ref zero must be null.");
        Assert(
            match.Build.Layout[1].EnchantName == "Burning",
            "Named enchantments should resolve."
        );

        using var catalog = new StubCatalog(corpus);
        var summary = new BuildRecommendationRepository(catalog).GetCorpusSummary()!.Value;
        Assert(
            summary.WindowEndUtc == DateTimeOffset.Parse("2026-08-10T00:00:00Z"),
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

    private static void TestRemoteUrlsUseMetricsV6Latest()
    {
        const string expected = "https://cdn.bazaarplusplus.com/metrics/v6/builds/latest.json";
        var runtimeUrl = TenWinBuildCatalogSettings.RemoteUrl;
        var buildUrl = TestInputs
            .MsBuild("src/BazaarPlusPlus/RemoteEmbeddedData.targets")
            .Descendants()
            .SingleOrDefault(element => element.Name.LocalName == "TenWinBuildsRemoteUrl")
            ?.Value.Trim();
        Assert(
            runtimeUrl == expected,
            $"Runtime URL should use the V6 metrics origin, got '{runtimeUrl}'."
        );
        Assert(
            buildUrl == expected,
            $"Build URL should use the V6 metrics origin, got '{buildUrl}'."
        );
    }

    // Set BPP_TENWIN_SAMPLE_PATH to a downloaded builds/latest.json to validate a live sample.
    // Without it this remains a no-op so the normal test run is hermetic.
    private static void TestOptionalLiveSampleParses()
    {
        var path = Environment.GetEnvironmentVariable("BPP_TENWIN_SAMPLE_PATH");
        if (string.IsNullOrWhiteSpace(path))
            return;

        using var reader = new StreamReader(File.OpenRead(path));
        var corpus = RequireCorpus(reader.ReadToEnd());
        Assert(corpus.HeroCount > 0, "The live sample should contain heroes.");
        Assert(corpus.BuildCount > 0, "The live sample should contain builds.");
        Console.WriteLine(
            $"Live ten-win sample: heroes={corpus.HeroCount} builds={corpus.BuildCount}"
        );
    }

    private static void TestFindRecommendationsTakesHeroSelectionAndLiveState()
    {
        var repositoryType = typeof(BuildRecommendationRepository);

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
    }

    private static void TestDefaultCachePathUsesBuildsFileName()
    {
        var dataRootPath = Path.Combine(Path.GetTempPath(), $"bpp-data-root-{Guid.NewGuid():N}");
        var cachePath = TenWinBuildCatalogSettings.BuildCacheFilePath(dataRootPath);

        Assert(
            cachePath == Path.Combine(dataRootPath, "builds.json"),
            "Ten-win build cache should live under the data root."
        );
    }

    private static void TestRemoteRefreshPreservesProductFailure()
    {
        var corpus = TenWinBuildCorpus.Parse(ScorePayload("CacheHero", 333))!;
        using var catalog = new StubCatalog(corpus);
        var repository = new BuildRecommendationRepository(catalog);

        catalog.Enqueue(
            CatalogRefreshResult<TenWinBuildCorpus>.Failure(
                new CatalogIssue(
                    CatalogIssueKind.RemoteDownloadFailed,
                    new InvalidOperationException("refresh-boom")
                )
            )
        );
        var failure = repository.TryRefreshFinalBuildsFromRemoteAsync().GetAwaiter().GetResult();
        Assert(!failure.Succeeded, "A failed pull should surface as a product failure.");
        Assert(
            failure.Error?.Contains("refresh-boom") == true,
            "The failure should retain its typed diagnostic detail."
        );
    }

    private static void TestCatalogRefreshPublishesSharedSnapshot()
    {
        var initialCorpus = TenWinBuildCorpus.Parse(ScorePayload("InitialHero", 111))!;
        var refreshedCorpus = TenWinBuildCorpus.Parse(ScorePayload("RefreshedHero", 222))!;
        using var catalog = new BlockingCatalog(initialCorpus);
        var repository = new BuildRecommendationRepository(catalog);

        var refresh = repository.TryRefreshFinalBuildsFromRemoteAsync();
        catalog.Started.GetAwaiter().GetResult();
        catalog.Complete(refreshedCorpus);

        var result = refresh.GetAwaiter().GetResult();
        Assert(result.Succeeded, "The plugin-lifetime catalog refresh should finish.");
        var summary = repository.GetCorpusSummary();
        Assert(summary.HasValue, "The shared repository should retain the refreshed snapshot.");
        var loadedSummary = summary.GetValueOrDefault();
        Assert(
            loadedSummary.HeroBuildCounts.Any(row => row.Hero == "RefreshedHero"),
            "The shared snapshot should publish after refresh."
        );
    }

    private static void TestSingleCardSelectionResolvesViaCardIndex()
    {
        WithCorpus(
            MainRecallPayload("Vanessa"),
            repository =>
            {
                var scores = ScoresOf(Find(repository, "Vanessa", [GuidA]));
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
            repository =>
            {
                var scores = ScoresOf(Find(repository, "Vanessa", [GuidA, GuidB]));
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
            repository =>
            {
                var scores = ScoresOf(Find(repository, "Dooley", [GuidA, GuidB]));
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
            repository =>
            {
                // C is present in cards[] but absent from this hero's cardIndex -> uncovered.
                // The literal intersection is empty, so union over the covered card (A) must still return results.
                var scores = ScoresOf(Find(repository, "Vanessa", [GuidA, GuidC]));
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
            repository =>
            {
                // D is absent from cards[] entirely; C is in cards[] but not in cardIndex. Neither has coverage.
                Assert(
                    ScoresOf(Find(repository, "Vanessa", [GuidD])).Count == 0,
                    "An unknown selected card should return no recommendation."
                );
                Assert(
                    ScoresOf(Find(repository, "Vanessa", [GuidC])).Count == 0,
                    "A selected card with no historical coverage should return no recommendation."
                );
            }
        );
    }

    private static void TestLiveStateRankingOutranksScore()
    {
        WithCorpus(
            MainRecallPayload("Vanessa"),
            repository =>
            {
                // Selecting A returns build0(score100, contains B) and build1(score200, A only).
                // With no live state, build1 wins on score.
                Assert(
                    ScoresOf(Find(repository, "Vanessa", [GuidA])).First() == 200L,
                    "Without live state the higher-score build ranks first."
                );

                // Put B on the board: build0 gains live-state weight and must outrank the higher-score build1.
                var liveState = LiveState(board: [GuidB], stash: [], shop: []);
                var ranked = ScoresOf(Find(repository, "Vanessa", [GuidA], liveState));
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
            repository =>
            {
                var recommendation = Find(repository, "Mak", [GuidA]).Single();
                var board = recommendation.Board;
                Assert(
                    board.Id == BppItemBoardId.FinalBuild,
                    "Recommendation board Id should be FinalBuild."
                );
                Assert(
                    board.Type == BppItemBoardType.Reference,
                    "Recommendation board Type should be Reference."
                );

                var card = board.Cards.Single(candidate => candidate.SourceSocketId == 3);
                Assert(
                    card.Tier == CardTier.Legendary,
                    "Tier value 5 should map to CardTier.Legendary."
                );
                Assert(card.Size == CardSize.Medium, "Size 2 should map to CardSize.Medium.");
                Assert(
                    card.EnchantmentType == CardEnchantment.Fiery,
                    "enchantRef 1 should resolve to CardEnchantment.Fiery."
                );
                Assert(card.DisplaySocketId == 3, "Slot 3 should keep socket index 3.");

                // tenWinRunCount / score survive into the recommendation DTO for UI evidence.
                Assert(
                    recommendation.TenWinRunCount == 80,
                    "TenWinRunCount should be carried from stats."
                );
                Assert(recommendation.Score == 1L, "Score should be carried from stats.");
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
            repository =>
            {
                var recommendation = Find(repository, "Jules", [GuidA]).Single();
                var card = recommendation.Board.Cards.First();
                Assert(card.Tier == CardTier.Bronze, "Tier value one should project as Bronze.");
                Assert(
                    card.EnchantmentType == null,
                    "enchantRef 0 should resolve to no enchantment."
                );
                Assert(
                    recommendation.P75TenWinFinalDay == null,
                    "A null p75 should remain null in the recommendation."
                );
            }
        );
    }

    private static void TestCorpusSummaryIncludesPerHeroBuildCounts()
    {
        WithCorpus(
            TwoHeroSummaryPayload(),
            repository =>
            {
                var summary = repository.GetCorpusSummary();
                Assert(summary.HasValue, "Loaded corpus summary should not be null.");

                Assert(summary!.Value.HeroCount == 2, "Summary should count both heroes.");
                Assert(
                    summary.Value.BuildCount == 3,
                    "Summary should count all build rows across heroes."
                );

                var heroCounts = HeroCounts(summary.Value);
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

    private static void TestProjectedBoardsFollowTheirHeroAndSnapshot()
    {
        var corpus = RequireCorpus(TwoHeroSummaryPayload());
        using var catalog = new StubCatalog(corpus);
        var repository = new BuildRecommendationRepository(catalog);

        var vanessa = Find(repository, "Vanessa", [GuidA]).Single().Board;
        var dooley = Find(repository, "Dooley", [GuidA]).Single().Board;
        Assert(
            vanessa.Cards.Single(card => card.SourceSocketId == 0).Tier == CardTier.Bronze
                && dooley.Cards.Single(card => card.SourceSocketId == 0).Tier == CardTier.Gold,
            "Each hero's build 0 should project its own layout, not another hero's build 0."
        );
        Assert(
            ReferenceEquals(Find(repository, "Vanessa", [GuidA]).Single().Board, vanessa),
            "A repeated query against the same snapshot should reuse the projected board."
        );

        catalog.Enqueue(
            CatalogRefreshResult<TenWinBuildCorpus>.Published(
                Snapshot(RequireCorpus(TwoHeroSummaryPayload()), CatalogSource.Remote),
                degraded: false
            )
        );
        Assert(
            repository.TryRefreshFinalBuildsFromRemoteAsync().GetAwaiter().GetResult().Succeeded,
            "The replacement snapshot should publish."
        );
        var refreshed = Find(repository, "Vanessa", [GuidA]).Single().Board;
        Assert(
            !ReferenceEquals(refreshed, vanessa) && refreshed.Signature == vanessa.Signature,
            "A replaced snapshot should project its builds afresh."
        );
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
            repository =>
            {
                Assert(
                    !Find(repository, "Common", [GuidA]).Any(),
                    "Common corpus rows must not be queryable as playable recommendations."
                );
                Assert(
                    !Find(repository, "UnknownHero", [GuidA]).Any(),
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
        var tooltip = LiveBuildPanelText.CorpusSummaryTooltip(withBuilds, () => "The Dragons");
        Assert(
            tooltip.Contains("The Dragons 4", StringComparison.Ordinal)
                && !tooltip.Contains("Hero8", StringComparison.Ordinal),
            "The corpus summary tooltip must not expose the transitional alias."
        );

        Assert(
            tooltip.Contains("Vanessa 2", StringComparison.Ordinal)
                && tooltip.Contains("UnknownHero 7", StringComparison.Ordinal),
            "Other corpus keys should show as stored."
        );
    }

    private static void TestOnlyPlayableHeroesAreQueried()
    {
        foreach (
            var hero in new[]
            {
                "Vanessa",
                "Pygmalien",
                "Dooley",
                "Mak",
                "Jules",
                "Karnok",
                "Stelle",
                "Hero8",
                " hero8 ",
                "TheDragons",
                " THEDRAGONS ",
                " Vanessa ",
            }
        )
            Assert(LiveBuildPlayableHero.IsPlayable(hero), $"{hero} should be playable.");

        foreach (var hero in new[] { null, "", "Common", "vanessa", "2", "UnknownHero" })
            Assert(!LiveBuildPlayableHero.IsPlayable(hero), $"'{hero}' should not be playable.");
    }

    private static void TestDragonsQueriesShareTheCanonicalRemoteFixture()
    {
        var parser = new TenWinBuildCatalogParser();
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
            repository =>
            {
                foreach (var hero in heroIds)
                {
                    var scores = ScoresOf(Find(repository, hero, [GuidA]));
                    Assert(
                        scores.SequenceEqual([200L, 100L]),
                        $"{hero} recommendation recall/ranking should remain unchanged."
                    );
                }

                var summary = repository.GetCorpusSummary()!.Value;
                Assert(
                    summary.HeroCount == 7 && summary.BuildCount == 21,
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
            "A canonical source fixture should project one TheDragons hero/build count."
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
            throw new InvalidOperationException("The analyzer contract fixture is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static JObject ContractHeroes(JObject root) => (JObject)root["heroes"]!;

    private static JObject ContractHero(JObject root) =>
        (JObject)ContractHeroes(root)[ContractHeroName]!;

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
        // Dooley's build 0 differs from Vanessa's build 0 (Gold, not Bronze): build ids are per hero.
        var dooleyBuild = Build("[0]", "[[0,0,3,0,1]]", 111);
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

    // ---- Plumbing ---------------------------------------------------------

    private static void WithCorpus(string payload, Action<BuildRecommendationRepository> body)
    {
        var corpus = RequireCorpus(payload);
        using var catalog = new StubCatalog(corpus);
        body(new BuildRecommendationRepository(catalog));
    }

    private static List<(string Hero, int BuildCount)> HeroCounts(TenWinCorpusSummary summary) =>
        summary.HeroBuildCounts.Select(row => (row.Hero, row.BuildCount)).ToList();

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

    private static IReadOnlyList<BuildRecommendation> Find(
        BuildRecommendationRepository repository,
        string hero,
        string[] templateIds,
        BuildLiveState? liveState = null
    ) => repository.FindRecommendations(hero, templateIds.Select(Guid.Parse).ToArray(), liveState);

    private static BuildLiveState LiveState(string[] board, string[] stash, string[] shop) =>
        BuildLiveState.From(
            board.Select(Guid.Parse).ToArray(),
            stash.Select(Guid.Parse).ToArray(),
            shop.Select(Guid.Parse).ToArray()
        );

    private static List<long> ScoresOf(IEnumerable<BuildRecommendation> recommendations) =>
        recommendations.Select(recommendation => recommendation.Score).ToList();

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
