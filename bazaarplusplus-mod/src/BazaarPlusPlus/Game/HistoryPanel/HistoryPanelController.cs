#nullable enable

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel
{
    private void RefreshSectionOnEntry()
    {
        _coordinator?.RefreshSectionOnEntry();
    }

    private void RefreshData()
    {
        _coordinator?.RefreshData();
    }

    private void RefreshGhostData()
    {
        _coordinator?.RefreshGhostData();
    }

    private void SetSectionMode(HistorySectionMode mode)
    {
        _coordinator?.SetSectionMode(mode);
    }

    private void SetGhostBattleFilter(GhostBattleFilter filter)
    {
        _coordinator?.SetGhostBattleFilter(filter);
    }

    private void SelectRun(int index)
    {
        _coordinator?.SelectRun(index);
    }

    private void SelectBattle(int index)
    {
        _coordinator?.SelectBattle(index);
    }

    private void LoadBattlesForSelectedRun()
    {
        _coordinator?.RefreshData();
    }

    private bool CanReplaySelectedBattle(out string reason)
    {
        if (_coordinator == null)
        {
            reason = HistoryPanelText.PanelUnavailable();
            return false;
        }

        return _coordinator.CanReplaySelectedBattle(ActiveSelectedBattle, out reason);
    }

    private bool CanRecordSelectedBattle(out string reason)
    {
        if (_coordinator == null)
        {
            reason = HistoryPanelText.PanelUnavailable();
            return false;
        }

        return _coordinator.CanRecordSelectedBattle(ActiveSelectedBattle, out reason);
    }

    private bool CanDeleteSelectedRun(out string reason)
    {
        if (_coordinator == null)
        {
            reason = HistoryPanelText.PanelUnavailable();
            return false;
        }

        return _coordinator.CanDeleteSelectedRun(SelectedRun, out reason);
    }

    private void TryReplaySelectedBattle(bool recordVideo = false)
    {
        if (_coordinator != null)
            _ = _coordinator.TryReplaySelectedBattleAsync(ActiveSelectedBattle, recordVideo);
    }

    private void TryDeleteSelectedRun()
    {
        _coordinator?.TryDeleteSelectedRun(SelectedRun);
    }

    private void TryCheckServerHealth()
    {
        if (_coordinator != null)
            _ = _coordinator.TryCheckServerHealthAsync();
    }

    private void ClearDeleteRunConfirmation()
    {
        _state.DeleteRunConfirmationRunId = null;
        _state.DeleteRunConfirmationUntil = 0f;
    }

    private bool IsDeleteRunConfirmationActive(string runId)
    {
        return _coordinator?.IsDeleteRunConfirmationActive(runId) == true;
    }

    private string GetDatabaseChipText()
    {
        return _coordinator?.GetDatabaseChipText() ?? HistoryPanelText.DatabaseUnavailable();
    }
}
