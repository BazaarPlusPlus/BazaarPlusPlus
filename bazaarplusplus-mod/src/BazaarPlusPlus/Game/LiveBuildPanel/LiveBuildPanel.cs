#nullable enable

using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Game.LiveBuildPanel.Preview;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.LiveBuildPanel.Ui;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.GameInterop.LiveCards;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal sealed class LiveBuildPanel : MonoBehaviour
{
    private const string OverlayPanelId = "LiveBuildPanel";
    private const int SupporterAttributionCount = 4;

    private static LiveBuildPanel? _instance;
    private readonly LiveCardSnapshotReader _reader = new();
    private BuildRecommendationRepository? _recommendations;
    private readonly BuildRecommendationRefreshService _refreshService = new();
    private readonly LiveBuildCandidateState _candidateState = new();
    private LiveBuildPreviewRenderer? _previewRenderer;
    private LiveBuildPanelView? _view;
    private Coroutine? _renderCoroutine;
    private LiveCardSnapshotSet _liveSnapshot = LiveCardSnapshotSet.Empty;
    private IReadOnlyList<BuildRecommendation> _matches = Array.Empty<BuildRecommendation>();
    private IReadOnlyList<BPPSupporterSample> _supporters = Array.Empty<BPPSupporterSample>();
    private IOverlayPanelHandle? _overlayHandle;
    private bool _isVisible;
    private int _recommendationIndex;
    private bool _buildRefreshInProgress;
    private string _buildRefreshError = string.Empty;
    private bool _buildRefreshSucceeded;
    private readonly LiveBuildRefreshContinuationGate _buildRefreshContinuation = new();

    public static bool IsVisible => _instance?._isVisible == true;

    private void Awake()
    {
        _instance = this;
    }

    internal void Initialize(
        BuildRecommendationRepository recommendations,
        OverlayPanelHost overlayHost,
        INativeCardPreviewHost nativeCardPreviewHost
    )
    {
        if (_recommendations != null)
            return;

        _recommendations =
            recommendations ?? throw new ArgumentNullException(nameof(recommendations));
        _previewRenderer = new LiveBuildPreviewRenderer(
            nativeCardPreviewHost ?? throw new ArgumentNullException(nameof(nativeCardPreviewHost))
        );
        AttachToOverlayHost(overlayHost);
        _recommendations.BeginCorpusLoad();
    }

    internal void AttachToOverlayHost(OverlayPanelHost overlayHost)
    {
        if (_overlayHandle != null)
            return;

        _overlayHandle = overlayHost.Register(
            new OverlayPanelRegistration(
                OverlayPanelId,
                BppHotkeyActionId.ToggleLiveBuildPanel,
                onOpen: Open,
                onClose: Close,
                tick: Tick
            )
        );
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_instance, this))
            _instance = null;

        // Invalidate any in-flight manual refresh so its continuation never touches the
        // destroyed view (the repository corpus update itself is allowed to finish in the background).
        _buildRefreshContinuation.Invalidate();
        _overlayHandle?.Dispose();
        _overlayHandle = null;
        StopRender();
        _previewRenderer?.Dispose();
        _view?.Dispose();
        _view = null;
    }

    // Lifecycle (scene change, combat gate, hotkey, escape) is owned by the Overlay Panel Host;
    // this tick only carries the panel's own per-frame content work.
    private void Tick(float dt, bool isVisible)
    {
        if (!isVisible)
            return;

        var mouse = Mouse.current;
        if (mouse != null)
            _previewRenderer?.PollHover(mouse.position.ReadValue());
    }

    private void Open()
    {
        EnsureView();
        _isVisible = true;
        _supporters = BPPSupporters.SampleMany(SupporterAttributionCount);
        _candidateState.Clear();
        _recommendationIndex = 0;
        // A refresh still in flight keeps its pending status visible; otherwise drop the previous
        // session's one-shot success/failure feedback so the card reopens on the plain summary.
        if (!_buildRefreshInProgress)
        {
            _buildRefreshError = string.Empty;
            _buildRefreshSucceeded = false;
        }
        var snapshotOutcome = _reader.Read();
        _liveSnapshot = snapshotOutcome.Snapshot;
        ReportLiveSnapshotIssues(snapshotOutcome.Issues);
        RefreshRecommendations();
        RefreshViewAndPreview();
        _view?.SetVisible(true);
    }

    private static void ReportLiveSnapshotIssues(IReadOnlyList<LiveCardSnapshotIssue> issues)
    {
        foreach (var issue in issues)
        {
            var fields = new[]
            {
                LiveBuildPanelLogEvents.LiveSnapshotDegradedSection.Bind(issue.Section),
                LiveBuildPanelLogEvents.LiveSnapshotDegradedReasonCode.Bind(
                    issue.Reason == LiveCardSnapshotFailureReason.InvalidPlacement
                        ? LiveBuildSnapshotReasonCode.InvalidPlacement
                        : LiveBuildSnapshotReasonCode.ReadException
                ),
                LiveBuildPanelLogEvents.LiveSnapshotDegradedTemplateId.Bind(issue.TemplateId),
                LiveBuildPanelLogEvents.LiveSnapshotDegradedSocketId.Bind(issue.SocketId),
                LiveBuildPanelLogEvents.LiveSnapshotDegradedItemSize.Bind(issue.ItemSize),
            };
            if (issue.Exception == null)
                BppLog.WarnEvent(LiveBuildPanelLogEvents.LiveSnapshotDegraded, fields);
            else
                BppLog.WarnEvent(
                    LiveBuildPanelLogEvents.LiveSnapshotDegraded,
                    issue.Exception,
                    fields
                );
        }
    }

    private void Close()
    {
        if (!_isVisible)
            return;

        _isVisible = false;
        _candidateState.Clear();
        _matches = Array.Empty<BuildRecommendation>();
        _recommendationIndex = 0;
        StopRender();
        _previewRenderer?.Hide();
        _view?.SetVisible(false);
    }

    private void EnsureView()
    {
        if (_view != null)
        {
            _view.EnsureCreated();
            return;
        }

        _view = new LiveBuildPanelView(
            transform,
            Close,
            PreviousRecommendation,
            NextRecommendation,
            TryRefreshFinalBuilds
        );
        _view.RowBoundsChanged += OnRowBoundsChanged;
        _view.CandidateToggleRequested += OnCandidateToggleRequested;
        _view.EnsureCreated();
    }

    private void OnRowBoundsChanged(BppItemBoardId id, Rect bounds)
    {
        if (_previewRenderer?.SetBounds(id, bounds) == true && _isVisible)
            RefreshViewAndPreview();
    }

    private void OnCandidateToggleRequested(BppItemBoardId rowId, Guid templateId)
    {
        if (!_isVisible || templateId == Guid.Empty)
            return;

        _candidateState.Toggle(templateId);
        _candidateState.PruneToSelectableRows(BuildSelectableBoards());
        _recommendationIndex = 0;
        RefreshRecommendations();
        RefreshViewAndPreview();
    }

    private void PreviousRecommendation()
    {
        if (_matches.Count <= 1)
            return;

        _recommendationIndex = WrapIndex(_recommendationIndex - 1, _matches.Count);
        RefreshViewAndPreview();
    }

    private void NextRecommendation()
    {
        if (_matches.Count <= 1)
            return;

        _recommendationIndex = WrapIndex(_recommendationIndex + 1, _matches.Count);
        RefreshViewAndPreview();
    }

    private void TryRefreshFinalBuilds()
    {
        // The pull button is disabled while a refresh runs; the in-progress check only guards
        // against re-entry races, the card already shows the pending status.
        if (!_isVisible || _buildRefreshInProgress)
            return;

        _buildRefreshInProgress = true;
        _buildRefreshError = string.Empty;
        _buildRefreshSucceeded = false;
        RefreshRailView();
        _ = RefreshFinalBuildsAsync(
            _buildRefreshContinuation.Capture(),
            new LiveBuildRefreshLogOperation(Guid.NewGuid())
        );
    }

    private async Task RefreshFinalBuildsAsync(
        int operationVersion,
        LiveBuildRefreshLogOperation logOperation
    )
    {
        BuildRecommendationRefreshResult result;
        try
        {
            result = await _refreshService.RefreshAsync(_recommendations!, CancellationToken.None);
        }
        catch (Exception ex)
        {
            result = BuildRecommendationRefreshResult.Failure(
                LiveBuildRefreshFailureReasonCode.RefreshException,
                ex.Message,
                ex
            );
        }

        if (result.Succeeded)
        {
            logOperation.TrySucceed(
                result.Outcome == BuildRecommendationRefreshOutcome.NoChange
                    ? LiveBuildRefreshResultCode.NoChange
                    : LiveBuildRefreshResultCode.Updated
            );
        }
        else
        {
            logOperation.TryFail(
                result.FailureReason ?? LiveBuildRefreshFailureReasonCode.RefreshException,
                result.Exception
            );
        }

        // Stale continuation guard: a destroyed panel bumped the version; the corpus update (if
        // any) already landed in the repository and must not touch this UI.
        if (!_buildRefreshContinuation.IsCurrent(operationVersion))
            return;

        _buildRefreshInProgress = false;
        if (result.Succeeded)
        {
            // No standalone success copy: the card's summary line refreshes (new data time)
            // and the success severity tints it for this session.
            _buildRefreshError = string.Empty;
            _buildRefreshSucceeded = true;
        }
        else
        {
            _buildRefreshError = string.IsNullOrWhiteSpace(result.Error)
                ? LiveBuildPanelText.Unknown()
                : result.Error!;
            _buildRefreshSucceeded = false;
        }

        if (!_isVisible || _view == null)
            return;

        if (result.Succeeded)
        {
            // Recompute against the refreshed corpus; failures keep the previous matches intact.
            RefreshRecommendations();
            RefreshViewAndPreview();
        }
        else
        {
            RefreshRailView();
        }
    }

    // Status-only updates redraw the UITK tree without restarting the native preview coroutine:
    // the boards did not change, so re-rendering card previews would be wasted work.
    private void RefreshRailView()
    {
        if (!_isVisible)
            return;

        _view?.Refresh(BuildPanelSnapshot());
    }

    private void RefreshRecommendations()
    {
        var hero = _liveSnapshot.Hero?.ToString();
        _matches =
            string.IsNullOrWhiteSpace(hero) || !_candidateState.HasCandidates
                ? Array.Empty<BuildRecommendation>()
                : _recommendations!.FindRecommendations(
                    hero,
                    _candidateState.TemplateIds,
                    ResolveLiveState()
                );
        _recommendationIndex = Mathf.Clamp(
            _recommendationIndex,
            0,
            Math.Max(0, _matches.Count - 1)
        );
    }

    // Live board/stash/shop only rank matched builds (board weighs most); they never drive recall.
    private BuildLiveState ResolveLiveState()
    {
        return BuildLiveState.From(
            TemplateIdsOf(_liveSnapshot.BoardItems),
            TemplateIdsOf(_liveSnapshot.StashItems),
            TemplateIdsOf(_liveSnapshot.ShopItems)
        );
    }

    private static IReadOnlyCollection<Guid> TemplateIdsOf(IReadOnlyList<LiveCardSnapshot> cards) =>
        cards.Select(card => card.TemplateId).Where(id => id != Guid.Empty).Distinct().ToArray();

    private void RefreshViewAndPreview()
    {
        if (!_isVisible)
            return;

        var snapshot = BuildPanelSnapshot();
        _view?.Refresh(snapshot);
        StopRender();
        if (_previewRenderer != null)
            _renderCoroutine = StartCoroutine(_previewRenderer.Render(snapshot));
    }

    private LiveBuildPanelSnapshot BuildPanelSnapshot()
    {
        var shop = BuildBoard(
            BppItemBoardId.LiveShop,
            BppItemBoardType.SelectableShop,
            _liveSnapshot.ShopItems
        );
        var board = BuildBoard(
            BppItemBoardId.LiveBoard,
            BppItemBoardType.SelectableContainer,
            _liveSnapshot.BoardItems
        );
        var stash = BuildBoard(
            BppItemBoardId.LiveStash,
            BppItemBoardType.SelectableContainer,
            _liveSnapshot.StashItems
        );
        var recommendation = _matches.Count > 0 ? _matches[_recommendationIndex] : null;
        var nowUtc = DateTimeOffset.UtcNow;
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
            Hero = _liveSnapshot.Hero,
            FinalBuild = finalBuild,
            Shop = shop,
            Board = board,
            Stash = stash,
            CandidateTemplateIds = _candidateState.TemplateIds,
            MatchesState = matches.State,
            MatchesGuidance = matches.Guidance,
            MatchTenWinRateBps = matches.Recommendation?.TenWinRateBps,
            MatchTenWinRunCount = matches.Recommendation?.TenWinRunCount ?? 0,
            MatchP75FinalDay = matches.Recommendation?.P75TenWinFinalDay,
            MatchMatchedCardCount = matches.Recommendation?.MatchedCardCount ?? 0,
            RecommendationIndex = _recommendationIndex,
            RecommendationCount = _matches.Count,
            FinalBuildRefreshButtonText = _buildRefreshInProgress
                ? LiveBuildPanelText.Working()
                : LiveBuildPanelText.RefreshFinalBuilds(),
            FinalBuildRefreshButtonEnabled = !_buildRefreshInProgress,
            CorpusState = corpus.State,
            CorpusSummary = corpus.Summary,
            CorpusFreshnessText = corpus.Summary.HasValue
                ? LiveBuildPanelText.CorpusFreshnessLine(corpus.Summary.Value, nowUtc)
                : string.Empty,
            CorpusFreshnessTooltip = corpus.Tooltip,
            CorpusFreshnessSeverity = ResolveFreshnessSeverity(corpus.Summary, nowUtc),
            CorpusStatusText = corpus.Text,
            CorpusStatusTooltip = corpus.Tooltip,
            CorpusStatusSeverity = corpus.Severity,
            Supporters = _supporters,
        };
    }

    // The corpus card multiplexes four states into one fixed-height box: pending > failure > empty
    // > summary. Only the summary state fills the per-hero dashboard; the others render a single
    // status line. The post-pull success cue rides the freshness tint, not a "✓" prefix.
    private (
        LiveBuildCorpusState State,
        TenWinCorpusSummary? Summary,
        string Text,
        string Tooltip,
        LiveBuildRefreshSeverity Severity
    ) ResolveCorpusStatus()
    {
        if (_buildRefreshInProgress)
            return (
                LiveBuildCorpusState.Pending,
                null,
                LiveBuildPanelText.RefreshingFinalBuilds(),
                string.Empty,
                LiveBuildRefreshSeverity.Pending
            );

        var summary = _recommendations?.GetCorpusSummary();
        var tooltip = summary.HasValue
            ? LiveBuildPanelText.CorpusSummaryTooltip(summary.Value)
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(_buildRefreshError))
            return (
                LiveBuildCorpusState.Failure,
                summary,
                LiveBuildPanelText.FinalBuildRefreshFailed(_buildRefreshError),
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

    // Freshness tint for the corpus dashboard, reusing the refresh-severity palette: a just-pulled
    // or <=24h corpus reads green (Success), <=7d blue (Pending), older/unknown warm (Failure).
    private LiveBuildRefreshSeverity ResolveFreshnessSeverity(
        TenWinCorpusSummary? summary,
        DateTimeOffset nowUtc
    )
    {
        if (_buildRefreshSucceeded)
            return LiveBuildRefreshSeverity.Success;
        if (summary?.GeneratedAtUtc is not { } generatedAt)
            return LiveBuildRefreshSeverity.Failure;

        var age = nowUtc - generatedAt;
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
            _liveSnapshot.ShopItems
        );
        yield return BuildBoard(
            BppItemBoardId.LiveBoard,
            BppItemBoardType.SelectableContainer,
            _liveSnapshot.BoardItems
        );
        yield return BuildBoard(
            BppItemBoardId.LiveStash,
            BppItemBoardType.SelectableContainer,
            _liveSnapshot.StashItems
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

    private static BppItemBoardCard ToBoardCard(LiveCardSnapshot card)
    {
        return new BppItemBoardCard
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
    }

    private (
        LiveBuildMatchesState State,
        string Guidance,
        BuildRecommendation? Recommendation
    ) ResolveMatches(BuildRecommendation? recommendation)
    {
        if (_liveSnapshot.Hero == null)
            return (LiveBuildMatchesState.NoRun, LiveBuildPanelText.NoRun(), null);
        if (!_candidateState.HasCandidates)
            return (LiveBuildMatchesState.NoCandidates, LiveBuildPanelText.NoCandidates(), null);
        if (recommendation == null)
            return (
                LiveBuildMatchesState.NoRecommendation,
                LiveBuildPanelText.NoRecommendation(),
                null
            );

        return (LiveBuildMatchesState.HasRecommendation, string.Empty, recommendation);
    }

    private void StopRender()
    {
        if (_renderCoroutine == null)
            return;

        StopCoroutine(_renderCoroutine);
        _renderCoroutine = null;
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
}
