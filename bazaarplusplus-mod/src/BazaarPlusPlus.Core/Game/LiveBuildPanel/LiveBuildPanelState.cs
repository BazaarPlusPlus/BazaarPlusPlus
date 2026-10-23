#nullable enable
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Core.Cards.LiveCards;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

/// <summary>
/// The Live Build Panel's state for one panel lifetime (ADR-0005): the live snapshot the panel
/// opened on, the candidate selection, the matched recommendations and the one being browsed, and
/// the manual refresh. A host feeds it inputs and renders <see cref="BuildSnapshot"/>.
/// </summary>
/// <remarks>
/// Not thread-safe. Call every member, <see cref="CompleteRefresh"/> included, on the host's main
/// thread. <see cref="FetchRefreshAsync"/> may finish on a pool thread, because the catalog
/// refresh resumes without a context: a host that has no main-thread synchronization context
/// must post the result back to its main thread before completing it.
/// </remarks>
public sealed class LiveBuildPanelState
{
    /// <summary>How many supporters the panel's attribution rail shows.</summary>
    public const int SupporterAttributionCount = 4;

    private readonly BuildRecommendationRepository _recommendations;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<string> _theDragonsDisplayName;
    private readonly LiveBuildRefreshState _refresh = new();
    private readonly LiveBuildCandidateState _candidates = new();
    private LiveCardSnapshotSet _live = LiveCardSnapshotSet.Empty;
    private IReadOnlyList<BuildRecommendation> _matches = Array.Empty<BuildRecommendation>();
    private IReadOnlyList<BPPSupporterSample> _supporters = Array.Empty<BPPSupporterSample>();
    private int _recommendationIndex;

    /// <param name="recommendations">The shared repository, owned by the composition root.</param>
    /// <param name="utcNow">The clock the corpus freshness reads.</param>
    /// <param name="theDragonsDisplayName">The game's localized name for The Dragons.</param>
    public LiveBuildPanelState(
        BuildRecommendationRepository recommendations,
        Func<DateTimeOffset> utcNow,
        Func<string> theDragonsDisplayName
    )
    {
        _recommendations =
            recommendations ?? throw new ArgumentNullException(nameof(recommendations));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _theDragonsDisplayName =
            theDragonsDisplayName ?? throw new ArgumentNullException(nameof(theDragonsDisplayName));
    }

    public bool IsVisible => _refresh.IsVisible;

    /// <summary>Starts loading the corpus so the first open has recommendations to query.</summary>
    public void BeginCorpusLoad() => _recommendations.BeginCorpusLoad();

    /// <summary>Shows the panel on a fresh live snapshot, with no candidates selected.</summary>
    public void Open(LiveCardSnapshotSet live, IReadOnlyList<BPPSupporterSample> supporters)
    {
        _refresh.SetVisible(true);
        _supporters = supporters ?? Array.Empty<BPPSupporterSample>();
        _candidates.Clear();
        _recommendationIndex = 0;
        _live = live ?? LiveCardSnapshotSet.Empty;
        RefreshRecommendations();
    }

    /// <summary>Hides the panel; false when it was not visible.</summary>
    public bool Close()
    {
        if (!_refresh.IsVisible)
            return false;

        _refresh.SetVisible(false);
        _candidates.Clear();
        _matches = Array.Empty<BuildRecommendation>();
        _recommendationIndex = 0;
        return true;
    }

    /// <summary>Ends the panel lifetime: no later refresh completion has a view effect.</summary>
    public void Destroy() => _refresh.Destroy();

    /// <summary>
    /// Toggles a candidate template and re-queries from the first recommendation; false when
    /// nothing changed.
    /// </summary>
    public bool ToggleCandidate(Guid templateId)
    {
        if (!_refresh.IsVisible || templateId == Guid.Empty)
            return false;

        _candidates.Toggle(templateId);
        _candidates.PruneToSelectableRows(BuildSelectableBoards());
        _recommendationIndex = 0;
        RefreshRecommendations();
        return true;
    }

    /// <summary>Browses to the previous recommendation, wrapping; false with one match or none.</summary>
    public bool Previous() => Browse(-1);

    /// <summary>Browses to the next recommendation, wrapping; false with one match or none.</summary>
    public bool Next() => Browse(+1);

    public LiveBuildRefreshRequest BeginRefresh() => _refresh.BeginRefresh();

    /// <summary>
    /// Runs the refresh <see cref="BeginRefresh"/> asked for. Never throws: an exception becomes
    /// a <see cref="LiveBuildRefreshFailureReasonCode.RefreshException"/> failure.
    /// </summary>
    public async Task<BuildRecommendationRefreshResult> FetchRefreshAsync(
        LiveBuildRefreshRequest request
    )
    {
        try
        {
            return request == LiveBuildRefreshRequest.NoChange
                ? BuildRecommendationRefreshResult.NoChange()
                : await _recommendations.TryRefreshFinalBuildsFromRemoteAsync();
        }
        catch (Exception ex)
        {
            return BuildRecommendationRefreshResult.Failure(
                LiveBuildRefreshFailureReasonCode.RefreshException,
                ex.Message,
                ex
            );
        }
    }

    /// <summary>
    /// Records a refresh outcome, logs it, and re-queries recommendations after a success. The
    /// effect says what the host must redraw.
    /// </summary>
    public LiveBuildRefreshEffect CompleteRefresh(
        BuildRecommendationRefreshResult result,
        Guid requestId
    )
    {
        LogRefresh(result, requestId);
        var effect = _refresh.CompleteRefresh(result);
        if (effect == LiveBuildRefreshEffect.Recommendations)
            RefreshRecommendations();
        return effect;
    }

    public LiveBuildPanelSnapshot BuildSnapshot()
    {
        var shop = BuildBoard(
            BppItemBoardId.LiveShop,
            BppItemBoardType.SelectableShop,
            _live.ShopItems
        );
        var board = BuildBoard(
            BppItemBoardId.LiveBoard,
            BppItemBoardType.SelectableContainer,
            _live.BoardItems
        );
        var stash = BuildBoard(
            BppItemBoardId.LiveStash,
            BppItemBoardType.SelectableContainer,
            _live.StashItems
        );
        var recommendation = _matches.Count > 0 ? _matches[_recommendationIndex] : null;
        var nowUtc = _utcNow();
        var corpus = ResolveCorpusStatus();
        var matches = ResolveMatches(recommendation);
        var finalBuild =
            recommendation?.Board
            ?? new BppItemBoard(
                BppItemBoardId.FinalBuild,
                BppItemBoardType.Reference,
                Array.Empty<BppItemBoardCard>(),
                "final-build:none"
            );

        return new LiveBuildPanelSnapshot
        {
            Hero = _live.Hero,
            FinalBuild = finalBuild,
            Shop = shop,
            Board = board,
            Stash = stash,
            CandidateTemplateIds = _candidates.TemplateIds,
            MatchesState = matches.State,
            MatchesGuidance = matches.Guidance,
            MatchTenWinRateBps = matches.Recommendation?.TenWinRateBps,
            MatchTenWinRunCount = matches.Recommendation?.TenWinRunCount ?? 0,
            MatchP75FinalDay = matches.Recommendation?.P75TenWinFinalDay,
            MatchMatchedCardCount = matches.Recommendation?.MatchedCardCount ?? 0,
            RecommendationIndex = _recommendationIndex,
            RecommendationCount = _matches.Count,
            FinalBuildRefreshButtonText = _refresh.IsPending
                ? LiveBuildPanelText.Working()
                : LiveBuildPanelText.RefreshFinalBuilds(),
            FinalBuildRefreshButtonEnabled = !_refresh.IsPending,
            CorpusState = corpus.State,
            CorpusFreshnessText = corpus.Summary.HasValue
                ? LiveBuildPanelText.CorpusFreshnessLine(corpus.Summary.Value, nowUtc)
                : string.Empty,
            CorpusFreshnessSeverity = ResolveFreshnessSeverity(corpus.Summary, nowUtc),
            CorpusStatusText = corpus.Text,
            CorpusStatusTooltip = corpus.Tooltip,
            CorpusStatusSeverity = corpus.Severity,
            Supporters = _supporters,
        };
    }

    private bool Browse(int step)
    {
        if (_matches.Count <= 1)
            return false;

        _recommendationIndex = WrapIndex(_recommendationIndex + step, _matches.Count);
        return true;
    }

    private void RefreshRecommendations()
    {
        var hero = _live.Hero?.ToString();
        _matches =
            string.IsNullOrWhiteSpace(hero) || !_candidates.HasCandidates
                ? Array.Empty<BuildRecommendation>()
                : _recommendations.FindRecommendations(
                    hero,
                    _candidates.TemplateIds,
                    ResolveLiveState()
                );
        _recommendationIndex = Math.Clamp(_recommendationIndex, 0, Math.Max(0, _matches.Count - 1));
    }

    // Live board/stash/shop only rank matched builds (board weighs most); they never drive recall.
    private BuildLiveState ResolveLiveState() =>
        BuildLiveState.From(
            TemplateIdsOf(_live.BoardItems),
            TemplateIdsOf(_live.StashItems),
            TemplateIdsOf(_live.ShopItems)
        );

    private static IReadOnlyCollection<Guid> TemplateIdsOf(IReadOnlyList<LiveCardSnapshot> cards) =>
        cards.Select(card => card.TemplateId).Where(id => id != Guid.Empty).Distinct().ToArray();

    // The header prioritizes pending, failure, empty, then the corpus summary.
    // Detailed timestamps and per-hero counts stay available in its hover details.
    private (
        LiveBuildCorpusState State,
        TenWinCorpusSummary? Summary,
        string Text,
        string Tooltip,
        LiveBuildRefreshSeverity Severity
    ) ResolveCorpusStatus()
    {
        if (_refresh.IsPending)
            return (
                LiveBuildCorpusState.Pending,
                null,
                LiveBuildPanelText.RefreshingFinalBuilds(),
                string.Empty,
                LiveBuildRefreshSeverity.Pending
            );

        var summary = _recommendations.GetCorpusSummary();
        var tooltip = summary.HasValue
            ? LiveBuildPanelText.CorpusSummaryTooltip(summary.Value, _theDragonsDisplayName)
            : string.Empty;

        if (_refresh.Feedback is { Succeeded: false } failure)
            return (
                LiveBuildCorpusState.Failure,
                summary,
                LiveBuildPanelText.FinalBuildRefreshFailed(
                    string.IsNullOrWhiteSpace(failure.Error)
                        ? LiveBuildPanelText.Unknown()
                        : failure.Error!
                ),
                tooltip,
                LiveBuildRefreshSeverity.Failure
            );

        if (!summary.HasValue)
            return (
                LiveBuildCorpusState.Empty,
                null,
                LiveBuildPanelText.CorpusEmpty(),
                string.Empty,
                LiveBuildRefreshSeverity.Neutral
            );

        return (
            LiveBuildCorpusState.Summary,
            summary,
            string.Empty,
            tooltip,
            LiveBuildRefreshSeverity.Neutral
        );
    }

    // Freshness tint for the corpus header: a just-pulled
    // or <=24h corpus reads green (Success), <=7d blue (Pending), older/unknown warm (Failure).
    private LiveBuildRefreshSeverity ResolveFreshnessSeverity(
        TenWinCorpusSummary? summary,
        DateTimeOffset nowUtc
    )
    {
        if (_refresh.Feedback?.Succeeded == true)
            return LiveBuildRefreshSeverity.Success;
        if (summary?.WindowEndUtc is not { } windowEnd)
            return LiveBuildRefreshSeverity.Failure;

        var age = nowUtc - windowEnd;
        if (age <= TimeSpan.FromHours(24))
            return LiveBuildRefreshSeverity.Success;
        if (age <= TimeSpan.FromDays(7))
            return LiveBuildRefreshSeverity.Pending;
        return LiveBuildRefreshSeverity.Failure;
    }

    private IEnumerable<BppItemBoard> BuildSelectableBoards()
    {
        yield return BuildBoard(
            BppItemBoardId.LiveShop,
            BppItemBoardType.SelectableShop,
            _live.ShopItems
        );
        yield return BuildBoard(
            BppItemBoardId.LiveBoard,
            BppItemBoardType.SelectableContainer,
            _live.BoardItems
        );
        yield return BuildBoard(
            BppItemBoardId.LiveStash,
            BppItemBoardType.SelectableContainer,
            _live.StashItems
        );
    }

    private static BppItemBoard BuildBoard(
        BppItemBoardId id,
        BppItemBoardType type,
        IReadOnlyList<LiveCardSnapshot> cards
    )
    {
        var boardCards = cards.Select(ToBoardCard).ToArray();
        return BppItemBoardSlotPlanner.Plan(
            new BppItemBoard(id, type, boardCards, BuildSignature(id, boardCards))
        );
    }

    private static BppItemBoardCard ToBoardCard(LiveCardSnapshot card) =>
        new()
        {
            TemplateId = card.TemplateId,
            InstanceId = card.InstanceId,
            Order = card.Order,
            Tier = card.Tier,
            Size = card.Size,
            Span = BppItemBoardSpan.Resolve(card.Size),
            EnchantmentType = card.EnchantmentType,
            SourceSocketId = card.SocketId,
            Attributes = card.Attributes,
        };

    private (
        LiveBuildMatchesState State,
        string Guidance,
        BuildRecommendation? Recommendation
    ) ResolveMatches(BuildRecommendation? recommendation)
    {
        if (_live.Hero == null)
            return (LiveBuildMatchesState.NoRun, LiveBuildPanelText.NoRun(), null);
        if (!_candidates.HasCandidates)
            return (LiveBuildMatchesState.NoCandidates, LiveBuildPanelText.NoCandidates(), null);
        if (recommendation == null)
            return (
                LiveBuildMatchesState.NoRecommendation,
                LiveBuildPanelText.NoRecommendation(),
                null
            );

        return (LiveBuildMatchesState.HasRecommendation, string.Empty, recommendation);
    }

    private static string BuildSignature(
        BppItemBoardId id,
        IReadOnlyList<BppItemBoardCard> cards
    ) =>
        $"{id}:{string.Join(",", cards.Select(card => $"{card.InstanceId}:{card.TemplateId}:{card.DisplaySocketId}:{card.Tier}:{card.EnchantmentType}"))}";

    private static int WrapIndex(int index, int count)
    {
        if (count <= 0)
            return 0;

        var wrapped = index % count;
        return wrapped < 0 ? wrapped + count : wrapped;
    }

    private static void LogRefresh(BuildRecommendationRefreshResult result, Guid requestId)
    {
        if (result.Succeeded)
        {
            BppLog.InfoEvent(
                new BppLogEvent(
                    BppLogFeatureScope.LiveBuildPanel,
                    "live_build_panel.refresh.succeeded"
                ),
                ("request_id", requestId, BppLogCorrelationPolicy.Short),
                (
                    "result",
                    result.Outcome == BuildRecommendationRefreshOutcome.NoChange
                        ? LiveBuildRefreshResultCode.NoChange
                        : LiveBuildRefreshResultCode.Updated
                )
            );
            return;
        }

        var failed = new BppLogEvent(
            BppLogFeatureScope.LiveBuildPanel,
            "live_build_panel.refresh.failed"
        );
        var fields = new BppLogField[]
        {
            ("request_id", requestId, BppLogCorrelationPolicy.Short),
            (
                "reason_code",
                result.FailureReason ?? LiveBuildRefreshFailureReasonCode.RefreshException
            ),
        };
        if (result.Exception == null)
            BppLog.ErrorEvent(failed, fields);
        else
            BppLog.ErrorEvent(failed, result.Exception, fields);
    }
}
