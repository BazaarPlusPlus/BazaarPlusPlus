#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Game.LiveBuildPanel.Preview;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.LiveBuildPanel.Ui;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.GameInterop.LiveCards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.UiTokens;
using TheBazaar;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal sealed class LiveBuildPanel : MonoBehaviour
{
    private const string OverlayPanelId = "LiveBuildPanel";
    private const int OverlaySortingBand = BppOverlaySorting.MainOverlayPanelBand;
    private const int SupporterAttributionCount = 4;

    private static LiveBuildPanel? _instance;
    private readonly LiveCardSnapshotReader _reader = new();
    private readonly BuildRecommendationRepository _recommendations = new();
    private readonly BuildRecommendationRefreshService _refreshService = new();
    private readonly LiveBuildCandidateState _candidateState = new();
    private readonly LiveBuildPreviewRenderer _previewRenderer = new();
    private LiveBuildPanelView? _view;
    private Coroutine? _renderCoroutine;
    private LiveCardSnapshotSet _liveSnapshot = LiveCardSnapshotSet.Empty;
    private IReadOnlyList<BuildRecommendation> _matches = Array.Empty<BuildRecommendation>();
    private IReadOnlyList<BPPSupporterSample> _supporters = Array.Empty<BPPSupporterSample>();
    private string _lastSceneToken = string.Empty;
    private bool _isVisible;
    private int _recommendationIndex;
    private bool _buildRefreshInProgress;
    private string _buildRefreshError = string.Empty;
    private bool _buildRefreshSucceeded;
    private int _buildRefreshOperationVersion;

    public static bool IsVisible => _instance?._isVisible == true;

    private void Awake()
    {
        _instance = this;
        _lastSceneToken = GetSceneToken(SceneManager.GetActiveScene());
        BppOverlayPanelMutex.Register(
            new BppOverlayPanelRegistration(
                OverlayPanelId,
                OverlaySortingBand,
                () => _instance?._isVisible == true,
                () => _instance?.Close()
            )
        );
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_instance, this))
            _instance = null;

        // Invalidate any in-flight manual refresh so its continuation never touches the
        // destroyed view (the repository corpus update itself is allowed to finish in the background).
        _buildRefreshOperationVersion++;
        BppOverlayPanelMutex.Unregister(OverlayPanelId);
        StopRender();
        _previewRenderer.Dispose();
        _view?.Dispose();
        _view = null;
    }

    private void Update()
    {
        DetectSceneChange();

        if (_isVisible && TheBazaar.Data.IsInCombat)
        {
            Close();
            return;
        }

        var keyboard = Keyboard.current;
        if (
            keyboard?.capsLockKey.wasPressedThisFrame == true
            && keyboard.ctrlKey.isPressed == false
            && keyboard.altKey.isPressed == false
            && keyboard.shiftKey.isPressed == false
        )
        {
            Toggle();
            return;
        }

        if (!_isVisible)
            return;

        if (keyboard?.escapeKey.wasPressedThisFrame == true)
        {
            Close();
            return;
        }

        var mouse = Mouse.current;
        if (mouse != null)
            _previewRenderer.PollHover(mouse.position.ReadValue());
    }

    private void Toggle()
    {
        if (_isVisible)
            Close();
        else
            Open();
    }

    private void Open()
    {
        if (TheBazaar.Data.IsInCombat)
        {
            BppLog.Info("LiveBuildPanel", "Open suppressed: combat is active.");
            return;
        }

        BppOverlayPanelMutex.CloseOthers(OverlayPanelId, OverlaySortingBand);
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
        _liveSnapshot = _reader.Read();
        RefreshRecommendations();
        RefreshViewAndPreview();
        _view?.SetVisible(true);
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
        _previewRenderer.Hide();
        _view?.SetVisible(false);
    }

    private void EnsureView()
    {
        if (_view != null)
            return;

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
        if (_previewRenderer.SetBounds(id, bounds) && _isVisible)
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
        _ = RefreshFinalBuildsAsync(_buildRefreshOperationVersion);
    }

    private async Task RefreshFinalBuildsAsync(int operationVersion)
    {
        BuildRecommendationRefreshResult result;
        try
        {
            result = await _refreshService.RefreshAsync(_recommendations, CancellationToken.None);
        }
        catch (Exception ex)
        {
            result = BuildRecommendationRefreshResult.Failure(ex.Message);
        }

        // Stale continuation guard: a destroyed panel bumped the version; the corpus update (if
        // any) already landed in the repository and must not touch this UI.
        if (operationVersion != _buildRefreshOperationVersion)
            return;

        _buildRefreshInProgress = false;
        if (result.Succeeded)
        {
            // No standalone success copy: the card's summary line refreshes (new data time)
            // and the success severity tints it for this session.
            _buildRefreshError = string.Empty;
            _buildRefreshSucceeded = true;
            BppLog.Info("LiveBuildPanel", "Manual ten-win builds refresh succeeded.");
        }
        else
        {
            _buildRefreshError = string.IsNullOrWhiteSpace(result.Error)
                ? LiveBuildPanelText.Unknown()
                : result.Error!;
            _buildRefreshSucceeded = false;
            BppLog.Warn(
                "LiveBuildPanel",
                $"Manual ten-win builds refresh failed error={_buildRefreshError}."
            );
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
                : _recommendations.FindRecommendations(
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
        var corpusStatus = ResolveCorpusStatus();
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
            RecommendationStatus = ResolveRecommendationStatus(recommendation),
            RecommendationIndex = _recommendationIndex,
            RecommendationCount = _matches.Count,
            FinalBuildRefreshButtonText = _buildRefreshInProgress
                ? LiveBuildPanelText.Working()
                : LiveBuildPanelText.RefreshFinalBuilds(),
            FinalBuildRefreshButtonEnabled = !_buildRefreshInProgress,
            CorpusStatusText = corpusStatus.Text,
            CorpusStatusTooltip = corpusStatus.Tooltip,
            CorpusStatusSeverity = corpusStatus.Severity,
            Supporters = _supporters,
        };
    }

    // The corpus card body is one state-multiplexed line set: pending > failure > empty-corpus
    // guidance > summary. Success has no standalone copy — the refreshed summary line itself is
    // the evidence, tinted by the success severity until the next state change.
    private (string Text, string Tooltip, LiveBuildRefreshSeverity Severity) ResolveCorpusStatus()
    {
        if (_buildRefreshInProgress)
            return (
                LiveBuildPanelText.RefreshingFinalBuilds(),
                string.Empty,
                LiveBuildRefreshSeverity.Pending
            );

        var summary = _recommendations.GetCorpusSummary();
        var tooltip = summary.HasValue
            ? LiveBuildPanelText.CorpusSummaryTooltip(summary.Value)
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(_buildRefreshError))
            return (
                LiveBuildPanelText.FinalBuildRefreshFailed(_buildRefreshError),
                tooltip,
                LiveBuildRefreshSeverity.Failure
            );

        if (!summary.HasValue)
            return (
                LiveBuildPanelText.CorpusEmpty(),
                string.Empty,
                LiveBuildRefreshSeverity.Neutral
            );

        var line = LiveBuildPanelText.CorpusSummaryLine(summary.Value);
        return _buildRefreshSucceeded
            ? ($"✓ {line}", tooltip, LiveBuildRefreshSeverity.Success)
            : (line, tooltip, LiveBuildRefreshSeverity.Neutral);
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

    private string ResolveRecommendationStatus(BuildRecommendation? recommendation)
    {
        if (_liveSnapshot.Hero == null)
            return LiveBuildPanelText.NoRun();
        if (!_candidateState.HasCandidates)
            return LiveBuildPanelText.NoCandidates();
        if (recommendation == null)
            return LiveBuildPanelText.NoRecommendation();

        return $"{LiveBuildPanelText.RecommendationCount(_recommendationIndex, _matches.Count)} · "
            + LiveBuildPanelText.RecommendationEvidence(
                recommendation.TenWinRunCount,
                recommendation.TenWinRateBps,
                recommendation.P75TenWinFinalDay
            );
    }

    private void StopRender()
    {
        if (_renderCoroutine == null)
            return;

        StopCoroutine(_renderCoroutine);
        _renderCoroutine = null;
    }

    private void DetectSceneChange()
    {
        var token = GetSceneToken(SceneManager.GetActiveScene());
        if (string.Equals(token, _lastSceneToken, StringComparison.Ordinal))
            return;

        _lastSceneToken = token;
        if (_isVisible)
            Close();
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

    private static string GetSceneToken(Scene scene) =>
        $"{scene.name}|{scene.path}|{scene.buildIndex}|{scene.isLoaded}";
}
