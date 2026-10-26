using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.LiveCards;
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;

namespace LiveBuildPanel.Tests;

internal static class LiveBuildPanelStateTests
{
    private const string DragonsName = "Native Dragons";

    // A matches builds 0 and 1, C matches build 1 only, B is a live card no build holds.
    private static readonly Guid CardA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CardB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid CardC = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid Unlisted = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    public static void Run()
    {
        SupporterAttributionFillsTheRail();
        OpenResetsCandidatesAndTheIndex();
        CloseClearsTheSelection();
        ToggleIsIgnoredWhileHidden();
        TogglePrunesUnselectableCandidatesAndResetsTheIndex();
        BrowsingWrapsAndNeedsTwoMatches();
        MatchesReportEachState();
        LiveCardsProjectOntoPlannedBoards();
        CorpusStatusPrefersPendingThenFailureThenEmptyThenSummary();
        FreshnessTintsByAgeAndAFreshPull();
        StatusOnlyCompletionKeepsTheRecommendation();
        FetchNeverThrowsAndSkipsTheNetworkForNoChange();
        TooltipNamesTheDragonsThroughThePort();
        RefreshLoggingKeepsItsEventsAndFields();
    }

    private static void SupporterAttributionFillsTheRail()
    {
        Check.Equal(4, LiveBuildPanelState.SupporterAttributionCount, "Rail size.");
        var supporters = Enumerable
            .Range(1, LiveBuildPanelState.SupporterAttributionCount)
            .Select(index => new BPPSupporterSample { Name = $"S{index}", Tier = index })
            .ToArray();
        var (state, _) = Create();

        state.Open(Live(CardHero.Vanessa), supporters);

        Check.That(
            state.BuildSnapshot().Supporters.SequenceEqual(supporters),
            "The snapshot carries the supporters the panel opened with."
        );
    }

    private static void OpenResetsCandidatesAndTheIndex()
    {
        var (state, _) = Create();
        state.Open(Live(CardHero.Vanessa, board: [CardA]), []);
        state.ToggleCandidate(CardA);
        state.Next();
        Check.Equal(1, state.BuildSnapshot().RecommendationIndex, "Browsed to the second build.");

        state.Open(Live(CardHero.Vanessa, board: [CardA]), []);

        var snapshot = state.BuildSnapshot();
        Check.That(snapshot.CandidateTemplateIds.Count == 0, "Open clears candidates.");
        Check.Equal(0, snapshot.RecommendationIndex, "Open resets the index.");
        Check.Equal(LiveBuildMatchesState.NoCandidates, snapshot.MatchesState, "Open state.");
    }

    private static void CloseClearsTheSelection()
    {
        var (state, _) = Create();
        state.Open(Live(CardHero.Vanessa, board: [CardA]), []);
        state.ToggleCandidate(CardA);

        Check.That(state.Close(), "Closing a visible panel reports a change.");
        Check.That(!state.IsVisible, "A closed panel is hidden.");
        Check.That(!state.Close(), "Closing a hidden panel is a no-op.");
        var snapshot = state.BuildSnapshot();
        Check.That(snapshot.CandidateTemplateIds.Count == 0, "Close clears candidates.");
        Check.Equal(0, snapshot.RecommendationCount, "Close clears matches.");
    }

    private static void ToggleIsIgnoredWhileHidden()
    {
        var (state, _) = Create();
        Check.That(!state.ToggleCandidate(CardA), "A hidden panel ignores toggles.");

        state.Open(Live(CardHero.Vanessa, board: [CardA]), []);
        Check.That(!state.ToggleCandidate(Guid.Empty), "An empty template is not a candidate.");
        Check.That(state.BuildSnapshot().CandidateTemplateIds.Count == 0, "Nothing selected.");
    }

    private static void TogglePrunesUnselectableCandidatesAndResetsTheIndex()
    {
        var (state, _) = Create();
        state.Open(Live(CardHero.Vanessa, board: [CardA]), []);
        state.ToggleCandidate(CardA);
        state.Next();

        Check.That(state.ToggleCandidate(Unlisted), "A toggle request is handled.");

        var snapshot = state.BuildSnapshot();
        Check.That(
            snapshot.CandidateTemplateIds.SequenceEqual([CardA]),
            "A template on no live row is pruned."
        );
        Check.Equal(0, snapshot.RecommendationIndex, "A toggle resets the index.");
    }

    private static void BrowsingWrapsAndNeedsTwoMatches()
    {
        var (state, _) = Create();
        state.Open(Live(CardHero.Vanessa, board: [CardA, CardC]), []);
        Check.That(!state.Next() && !state.Previous(), "No matches cannot be browsed.");

        state.ToggleCandidate(CardC);
        Check.Equal(1, state.BuildSnapshot().RecommendationCount, "C matches one build.");
        Check.That(!state.Next() && !state.Previous(), "One match cannot be browsed.");

        state.ToggleCandidate(CardC);
        state.ToggleCandidate(CardA);
        Check.Equal(2, state.BuildSnapshot().RecommendationCount, "A matches two builds.");
        Check.That(state.Previous(), "Previous browses.");
        Check.Equal(1, state.BuildSnapshot().RecommendationIndex, "Previous wraps to the end.");
        Check.That(state.Next(), "Next browses.");
        Check.Equal(0, state.BuildSnapshot().RecommendationIndex, "Next wraps to the start.");
    }

    private static void MatchesReportEachState()
    {
        var (state, _) = Create();
        state.Open(Live(hero: null, board: [CardA]), []);
        var noRun = state.BuildSnapshot();
        Check.Equal(LiveBuildMatchesState.NoRun, noRun.MatchesState, "No hero is no run.");
        Check.Equal(LiveBuildPanelText.NoRun(), noRun.MatchesGuidance, "No-run guidance.");

        state.Open(Live(CardHero.Vanessa, board: [CardA, CardB]), []);
        Check.Equal(
            LiveBuildMatchesState.NoCandidates,
            state.BuildSnapshot().MatchesState,
            "A run without candidates."
        );

        state.ToggleCandidate(CardB);
        Check.Equal(
            LiveBuildMatchesState.NoRecommendation,
            state.BuildSnapshot().MatchesState,
            "A candidate no build holds."
        );

        state.ToggleCandidate(CardB);
        state.ToggleCandidate(CardA);
        var match = state.BuildSnapshot();
        Check.Equal(LiveBuildMatchesState.HasRecommendation, match.MatchesState, "A match.");
        Check.Equal(string.Empty, match.MatchesGuidance, "A match has no guidance line.");
        Check.Equal(80, match.MatchTenWinRunCount, "The match carries its run count.");
        Check.Equal(1, match.MatchMatchedCardCount, "The match counts the candidate it holds.");
        Check.That(match.FinalBuild.Cards.Count > 0, "The match renders its final build.");
    }

    private static void LiveCardsProjectOntoPlannedBoards()
    {
        var (state, _) = Create();
        var medium = new LiveCardSnapshot
        {
            TemplateId = CardA,
            InstanceId = "live-a",
            Tier = CardTier.Gold,
            Size = CardSize.Medium,
            EnchantmentType = CardEnchantment.Icy,
            SocketId = 2,
            Attributes = new Dictionary<string, int> { ["DamageAmount"] = 7 },
        };
        state.Open(
            new LiveCardSnapshotSet(
                CardHero.Vanessa,
                shopItems: [new LiveCardSnapshot { TemplateId = CardB, Size = CardSize.Large }],
                boardItems: [medium],
                stashItems: []
            ),
            []
        );

        var snapshot = state.BuildSnapshot();
        var card = snapshot.Board.Cards.Single();
        Check.Equal(2, card.DisplaySocketId, "A board card keeps its socket.");
        Check.Equal(2, card.DisplaySpan, "A medium card spans two sockets.");
        Check.Equal(CardTier.Gold, card.Tier, "The tier facet carries through.");
        Check.Equal(CardEnchantment.Icy, card.EnchantmentType, "The enchantment carries through.");
        Check.Equal(7, card.Attributes["DamageAmount"], "Attributes carry through by name.");
        Check.Equal(
            (10 - 3) / 2,
            snapshot.Shop.Cards.Single().DisplaySocketId,
            "The shop row centers its cards."
        );
    }

    private static void CorpusStatusPrefersPendingThenFailureThenEmptyThenSummary()
    {
        var (state, catalog) = Create();
        state.Open(Live(CardHero.Vanessa), []);
        Check.Equal(LiveBuildCorpusState.Summary, state.BuildSnapshot().CorpusState, "Summary.");

        state.BeginRefresh();
        var pending = state.BuildSnapshot();
        Check.Equal(LiveBuildCorpusState.Pending, pending.CorpusState, "Pending wins.");
        Check.Equal(LiveBuildRefreshSeverity.Pending, pending.CorpusStatusSeverity, "Pending.");
        Check.That(!pending.FinalBuildRefreshButtonEnabled, "A pending refresh disables pulls.");

        state.CompleteRefresh(
            BuildRecommendationRefreshResult.Failure(
                LiveBuildRefreshFailureReasonCode.RemoteRequestFailed,
                "boom"
            ),
            Guid.NewGuid()
        );
        var failure = state.BuildSnapshot();
        Check.Equal(LiveBuildCorpusState.Failure, failure.CorpusState, "Failure follows.");
        Check.That(failure.CorpusStatusText.Contains("boom"), "The failure shows its detail.");

        catalog.Clear();
        state.Open(Live(CardHero.Vanessa), []);
        var empty = state.BuildSnapshot();
        Check.Equal(LiveBuildCorpusState.Empty, empty.CorpusState, "No corpus is empty.");
        Check.Equal(LiveBuildPanelText.CorpusEmpty(), empty.CorpusStatusText, "Empty text.");
    }

    private static void FreshnessTintsByAgeAndAFreshPull()
    {
        var windowEnd = Repository(new StubCatalog(Corpus()))
            .GetCorpusSummary()!
            .Value.WindowEndUtc!.Value;
        foreach (
            var (age, expected) in new[]
            {
                (TimeSpan.FromHours(24), LiveBuildRefreshSeverity.Success),
                (TimeSpan.FromDays(7), LiveBuildRefreshSeverity.Pending),
                (TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1), LiveBuildRefreshSeverity.Failure),
            }
        )
        {
            var (state, _) = Create(windowEnd + age);
            state.Open(Live(CardHero.Vanessa), []);
            Check.Equal(expected, state.BuildSnapshot().CorpusFreshnessSeverity, $"Age {age}.");
        }

        var (stale, catalog) = Create(windowEnd + TimeSpan.FromDays(30));
        stale.Open(Live(CardHero.Vanessa), []);
        stale.BeginRefresh();
        stale.CompleteRefresh(BuildRecommendationRefreshResult.Updated(), Guid.NewGuid());
        Check.Equal(
            LiveBuildRefreshSeverity.Success,
            stale.BuildSnapshot().CorpusFreshnessSeverity,
            "A fresh pull reads green whatever the corpus age."
        );

        catalog.Clear();
        stale.Open(Live(CardHero.Vanessa), []);
        Check.Equal(
            LiveBuildRefreshSeverity.Failure,
            stale.BuildSnapshot().CorpusFreshnessSeverity,
            "An unknown age reads warm."
        );
    }

    private static void StatusOnlyCompletionKeepsTheRecommendation()
    {
        var (state, _) = Create();
        state.Open(Live(CardHero.Vanessa, board: [CardA]), []);
        state.ToggleCandidate(CardA);
        state.Next();
        var before = state.BuildSnapshot();

        state.BeginRefresh();
        var effect = state.CompleteRefresh(
            BuildRecommendationRefreshResult.Failure(
                LiveBuildRefreshFailureReasonCode.RemoteRequestFailed,
                "offline"
            ),
            Guid.NewGuid()
        );

        var after = state.BuildSnapshot();
        Check.Equal(LiveBuildRefreshEffect.Status, effect, "A failure redraws status only.");
        Check.Equal(LiveBuildMatchesState.HasRecommendation, after.MatchesState, "Still matched.");
        Check.Equal(before.RecommendationIndex, after.RecommendationIndex, "Same build shown.");
        Check.Equal(before.FinalBuild.Signature, after.FinalBuild.Signature, "Same final build.");
    }

    private static void FetchNeverThrowsAndSkipsTheNetworkForNoChange()
    {
        var (state, catalog) = Create();
        catalog.RefreshFailure = new InvalidOperationException("transport down");

        var failure = state.FetchRefreshAsync(LiveBuildRefreshRequest.Download).Result;
        Check.That(!failure.Succeeded, "A throwing refresh is a failure.");
        Check.Equal(
            LiveBuildRefreshFailureReasonCode.RefreshException,
            failure.FailureReason,
            "An exception maps to RefreshException."
        );
        Check.Equal("transport down", failure.Error, "The failure keeps the message.");

        var refreshes = catalog.RefreshCount;
        var noChange = state.FetchRefreshAsync(LiveBuildRefreshRequest.NoChange).Result;
        Check.Equal(BuildRecommendationRefreshOutcome.NoChange, noChange.Outcome, "No change.");
        Check.Equal(refreshes, catalog.RefreshCount, "No change never refreshes the catalog.");
    }

    private static void TooltipNamesTheDragonsThroughThePort()
    {
        var (state, _) = Create();
        state.Open(Live(CardHero.Vanessa), []);

        var tooltip = state.BuildSnapshot().CorpusStatusTooltip;
        Check.That(
            tooltip.Contains($"{DragonsName} 1", StringComparison.Ordinal)
                && !tooltip.Contains("Hero8", StringComparison.Ordinal),
            $"The tooltip names The Dragons through the host port: {tooltip}"
        );
        Check.That(tooltip.Contains("Vanessa 2", StringComparison.Ordinal), "Other heroes.");
    }

    private static void RefreshLoggingKeepsItsEventsAndFields()
    {
        var lines = new List<string>();
        BppLog.Install(new ListLogSink(lines));
        var (state, _) = Create();
        state.Open(Live(CardHero.Vanessa), []);

        state.BeginRefresh();
        state.CompleteRefresh(BuildRecommendationRefreshResult.NoChange(), Guid.NewGuid());
        state.BeginRefresh();
        state.CompleteRefresh(
            BuildRecommendationRefreshResult.Failure(
                LiveBuildRefreshFailureReasonCode.RemoteInvalidResponse,
                "bad"
            ),
            Guid.NewGuid()
        );

        var succeeded = lines.Single(line =>
            line.Contains("event=live_build_panel.refresh.succeeded", StringComparison.Ordinal)
        );
        Check.That(
            succeeded.Contains("request_id=", StringComparison.Ordinal)
                && succeeded.Contains("result=no_change", StringComparison.Ordinal),
            $"The success event keeps request_id and result: {succeeded}"
        );
        var failed = lines.Single(line =>
            line.Contains("event=live_build_panel.refresh.failed", StringComparison.Ordinal)
        );
        Check.That(
            failed.Contains("request_id=", StringComparison.Ordinal)
                && failed.Contains("reason_code=remote_invalid_response", StringComparison.Ordinal),
            $"The failure event keeps request_id and reason_code: {failed}"
        );
    }

    private static (LiveBuildPanelState State, StubCatalog Catalog) Create(
        DateTimeOffset? nowUtc = null
    )
    {
        var catalog = new StubCatalog(Corpus());
        var now = nowUtc ?? new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
        return (
            new LiveBuildPanelState(Repository(catalog), () => now, () => DragonsName),
            catalog
        );
    }

    private static BuildRecommendationRepository Repository(StubCatalog catalog) => new(catalog);

    private static LiveCardSnapshotSet Live(CardHero? hero, Guid[]? board = null) =>
        new(
            hero,
            shopItems: [],
            boardItems: (board ?? [])
                .Select(
                    (templateId, index) =>
                        new LiveCardSnapshot
                        {
                            TemplateId = templateId,
                            InstanceId = $"board-{index}",
                            Order = index,
                            SocketId = index,
                        }
                )
                .ToArray(),
            stashItems: []
        );

    // Two Vanessa builds hold A (C only in the second) and one Dragons build holds A.
    private static TenWinBuildCorpus Corpus() =>
        TenWinBuildCorpus.Parse(
            $$"""
            {
              "schema_version": 2,
              "kind": "ten_win_builds",
              "generated_at": "2026-08-12T02:00:00Z",
              "window": { "start": "2026-08-07", "end": "2026-08-11", "days": 5 },
              "cards": ["{{CardA}}", "{{CardC}}"],
              "enchantments": [null],
              "schemas": {
                "build": ["card_refs", "layout", "stats"],
                "layout": ["card_ref", "slot", "tier", "enchant_ref", "size"],
                "stats": ["completed_run_count", "ten_win_run_count", "ten_win_rate_bps", "p75_ten_win_final_day", "score"]
              },
              "heroes": {
                "Vanessa": {
                  "builds": [
                    [[0,0,0,0], [[0,0,1,0,3],[0,3,1,0,3],[0,6,1,0,3],[0,9,1,0,1]], [300,80,2667,13,200]],
                    [[0,0,0,1], [[0,0,1,0,3],[0,3,1,0,3],[0,6,1,0,3],[1,9,1,0,1]], [300,80,2667,13,100]]
                  ],
                  "card_index": [[0,[0,1]],[1,[1]]]
                },
                "TheDragons": {
                  "builds": [
                    [[0,0,0,0], [[0,0,1,0,3],[0,3,1,0,3],[0,6,1,0,3],[0,9,1,0,1]], [300,80,2667,13,50]]
                  ],
                  "card_index": [[0,[0]]]
                }
              }
            }
            """
        ) ?? throw new InvalidOperationException("The test corpus was rejected.");

    private sealed class StubCatalog : IRemoteEmbeddedCatalog<TenWinBuildCorpus>
    {
        private CatalogSnapshot<TenWinBuildCorpus>? _snapshot;

        internal StubCatalog(TenWinBuildCorpus corpus)
        {
            _snapshot = new CatalogSnapshot<TenWinBuildCorpus>(
                corpus,
                CatalogSource.Cache,
                new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc),
                IsStale: false,
                Issue: null
            );
        }

        internal Exception? RefreshFailure { get; set; }

        internal int RefreshCount { get; private set; }

        internal void Clear() => _snapshot = null;

        public bool TryGet(out CatalogSnapshot<TenWinBuildCorpus> snapshot)
        {
            snapshot = _snapshot.GetValueOrDefault();
            return _snapshot.HasValue;
        }

        public ValueTask WarmAsync(CancellationToken cancellationToken = default) => default;

        public ValueTask<CatalogRefreshResult<TenWinBuildCorpus>> RefreshAsync(
            CancellationToken cancellationToken = default
        )
        {
            RefreshCount++;
            if (RefreshFailure != null)
                throw RefreshFailure;
            return ValueTask.FromResult(
                CatalogRefreshResult<TenWinBuildCorpus>.Published(_snapshot!.Value, degraded: false)
            );
        }

        public void Dispose() => _snapshot = null;
    }
}
