#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel
{
    private readonly HistoryPanelUiRefresh _uiRefresh = new();
    private Ui.HistoryPanelView? _uiView;
    private Rect _previewContainerBounds;
    private bool _hasPreviewContainerBounds;

    private void EnsureUi()
    {
        if (_uiView == null)
        {
            _uiView = new Ui.HistoryPanelView(
                transform,
                () => SetHistoryVisible(false),
                _uiRefresh.Guard(() => TryReplaySelectedBattle(false)),
                _uiRefresh.Guard(() => TryReplaySelectedBattle(true)),
                _uiRefresh.Guard(TryDeleteSelectedRun),
                TryCheckServerHealth,
                _uiRefresh.Guard<string?>(SubmitAccountLinkCode),
                ToggleAccountLinkForm,
                _uiRefresh.Guard(MarkAccountLinkedManually),
                _uiRefresh.Guard<int>(SelectRun),
                _uiRefresh.Guard<int>(SelectBattle),
                SetSectionMode,
                SetGhostBattleFilter,
                SetRunHero,
                ToggleGhostDayMin10
            );

            _uiView.SetPaginationActions(
                _uiRefresh.Guard<int>(direction => _coordinator?.PageArchive(direction)),
                _uiRefresh.Guard<int>(direction => _coordinator?.PageBattles(direction))
            );
            _uiView.SetFavoriteActions(
                _uiRefresh.Guard(TryFavoriteSelectedBattle),
                SaveFavoriteNote
            );
            _uiView.PreviewContainerBoundsChanged += OnPreviewContainerBoundsChanged;
            _uiView.OpponentBoundsChanged += bounds =>
            {
                _opponentBounds = bounds;
                if (_nativeOpponentBoard != null)
                    _nativeOpponentBoard.SetBounds(bounds);
                else
                    RefreshNativeHistoryBoards();
            };

            // Warm the native recorder on the UI thread before per-refresh gating.
            _coordinator?.PrewarmRecordingAvailability();
        }
        _uiView.EnsureCreated();
    }

    private void OnPreviewContainerBoundsChanged(Rect bounds)
    {
        _previewContainerBounds = bounds;
        _hasPreviewContainerBounds = true;
        if (_nativePlayerBoard != null)
            _nativePlayerBoard.SetBounds(bounds);
        else
            RefreshNativeHistoryBoards();
    }

    private void DisposeUi()
    {
        _uiView?.Dispose();
        _uiView = null;
    }

    private void SetUiVisible(bool visible)
    {
        _uiView?.SetVisible(visible);
    }

    private bool IsTextInputFocused()
    {
        return _uiView?.IsTextInputFocused() == true;
    }

    private void RefreshUi() => _uiRefresh.Request();

    private void FlushUi() => _uiRefresh.Flush(RenderUi);

    private void RenderUi()
    {
        _uiView?.Refresh(BuildUiModel());
        RefreshNativeHistoryMessages();
    }

    private void SetPreviewStatus(string? message, bool visible)
    {
        _uiView?.SetPreviewStatus(message, visible);
    }

    private HistoryPanelViewModel BuildUiModel() =>
        HistoryPanelViewModelBuilder.Build(
            _state,
            _coordinator,
            _runContext,
            _dependencies?.AccountLinkGate,
            Time.unscaledTime,
            _supporters,
            _nativePlayerStatus,
            _nativeOpponentStatus
        );
}
