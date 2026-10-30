#nullable enable

using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Core.Cards.LiveCards;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.LiveBuildPanel.Preview;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.LiveBuildPanel.Ui;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.LiveCards;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

/// <summary>
/// The desktop shell around <see cref="LiveBuildPanelState"/>: overlay registration, the live
/// read, the native view and boards, and the await that brings a refresh back to the main thread.
/// </summary>
internal sealed class LiveBuildPanel : MonoBehaviour
{
    private const string OverlayPanelId = "LiveBuildPanel";

    private static LiveBuildPanel? _instance;
    private LiveBuildPanelState? _state;
    private LiveBuildPreviewRenderer? _previewRenderer;
    private LiveBuildPanelView? _view;
    private IOverlayPanelHandle? _overlayHandle;

    public static bool IsVisible => _instance?._state?.IsVisible == true;

    internal static void OpenFromDockButton()
    {
        if (Touch.TouchLiveBuildHost.TryOpenFromDockButton())
            return;
        _instance?._overlayHandle?.RequestOpen();
    }

    private void Awake()
    {
        _instance = this;
    }

    internal void Initialize(
        BuildRecommendationRepository recommendations,
        DesktopOverlayPanels overlayHost
    )
    {
        if (_state != null)
            return;

        _state = new LiveBuildPanelState(
            recommendations ?? throw new ArgumentNullException(nameof(recommendations)),
            () => DateTimeOffset.UtcNow,
            DesktopCardFacets.TheDragonsDisplayName
        );
        AttachToOverlayHost(overlayHost);
        _state.BeginCorpusLoad();
    }

    internal void AttachToOverlayHost(DesktopOverlayPanels overlayHost)
    {
        if (_overlayHandle != null)
            return;

        _overlayHandle = overlayHost.Register(
            new OverlayPanelRegistration(
                OverlayPanelId,
                onOpen: () =>
                {
                    Open();
                    return true;
                },
                onClose: _ => Close(),
                tick: Tick
            ),
            BppHotkeyActionId.ToggleLiveBuildPanel
        );
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_instance, this))
            _instance = null;

        _state?.Destroy();
        _overlayHandle?.Dispose();
        _overlayHandle = null;
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

        _view?.Tick();
        var mouse = Mouse.current;
        _previewRenderer?.Tick(
            mouse?.position.ReadValue(),
            mouse?.leftButton.wasPressedThisFrame == true
        );
    }

    private void Open()
    {
        if (_state == null)
            return;

        EnsureView();
        var outcome = LiveCardSnapshotRead.Read(
            LiveCardSnapshotReader.Walk,
            new BppLogEvent(
                BppLogFeatureScope.LiveBuildPanel,
                "live_build_panel.live_snapshot.degraded",
                storm: ["section", "reason_code"]
            ),
            LiveCardSnapshotLogValues.SnakeCase
        );
        _state.Open(
            outcome.Snapshot,
            BPPSupporters.SampleMany(LiveBuildPanelState.SupporterAttributionCount)
        );
        RefreshViewAndPreview();
        _view?.SetVisible(true);
    }

    private void Close()
    {
        if (_state?.Close() != true)
            return;

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
            () => _overlayHandle?.RequestClose(),
            PreviousRecommendation,
            NextRecommendation,
            TryRefreshFinalBuilds
        );
        _view.RowBoundsChanged += OnRowBoundsChanged;
        _previewRenderer = new LiveBuildPreviewRenderer(
            transform,
            _view,
            OnCandidateToggleRequested
        );
        _view.EnsureCreated();
    }

    private void OnRowBoundsChanged(BppItemBoardId id, Rect bounds)
    {
        _previewRenderer?.SetBounds(id, bounds);
    }

    private void OnCandidateToggleRequested(Guid templateId)
    {
        if (_state?.ToggleCandidate(templateId) == true)
            RefreshViewAndPreview();
    }

    private void PreviousRecommendation()
    {
        if (_state?.Previous() == true)
            RefreshViewAndPreview();
    }

    private void NextRecommendation()
    {
        if (_state?.Next() == true)
            RefreshViewAndPreview();
    }

    private void TryRefreshFinalBuilds()
    {
        if (_state == null)
            return;

        var request = _state.BeginRefresh();
        if (request == LiveBuildRefreshRequest.Ignore)
            return;

        RefreshStatusView();
        _ = RefreshFinalBuildsAsync(_state, request, Guid.NewGuid());
    }

    // Awaited on Unity's synchronization context, so CompleteRefresh runs on the main thread as
    // LiveBuildPanelState requires.
    private async Task RefreshFinalBuildsAsync(
        LiveBuildPanelState state,
        LiveBuildRefreshRequest request,
        Guid logRequestId
    )
    {
        var result = await state.FetchRefreshAsync(request);
        switch (state.CompleteRefresh(result, logRequestId))
        {
            case LiveBuildRefreshEffect.Recommendations:
                RefreshViewAndPreview();
                break;
            case LiveBuildRefreshEffect.Status:
                RefreshStatusView();
                break;
        }
    }

    // Status-only updates refresh the view without reloading native boards:
    // the boards did not change, so re-rendering card previews would be wasted work.
    private void RefreshStatusView()
    {
        if (_state?.IsVisible != true)
            return;

        _view?.Refresh(_state.BuildSnapshot());
    }

    private void RefreshViewAndPreview()
    {
        if (_state?.IsVisible != true)
            return;

        var snapshot = _state.BuildSnapshot();
        _view?.Refresh(snapshot);
        _previewRenderer?.Render(snapshot);
    }
}
