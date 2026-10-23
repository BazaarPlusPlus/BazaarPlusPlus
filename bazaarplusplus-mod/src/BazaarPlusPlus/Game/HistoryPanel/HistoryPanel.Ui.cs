#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel
{
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
                () => TryReplaySelectedBattle(false),
                () => TryReplaySelectedBattle(true),
                TryDeleteSelectedRun,
                TryCheckServerHealth,
                SubmitAccountLinkCode,
                ToggleAccountLinkForm,
                MarkAccountLinkedManually,
                SelectRun,
                SelectBattle,
                SetSectionMode,
                SetGhostBattleFilter,
                SetRunHero,
                ToggleGhostDayMin10
            );

            _uiView.SetPaginationActions(
                direction => _coordinator?.PageArchive(direction),
                direction => _coordinator?.PageBattles(direction)
            );
            _uiView.SetFavoriteActions(TryFavoriteSelectedBattle, SaveFavoriteNote);
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

    private void RefreshUi()
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
