#nullable enable

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel
{
    private void SetSectionMode(HistorySectionMode mode)
    {
        _coordinator?.SetSectionMode(mode);
    }

    private void SetGhostBattleFilter(GhostBattleFilter filter)
    {
        _coordinator?.SetGhostBattleFilter(filter);
    }

    private void SetRunHero(string hero)
    {
        _coordinator?.SetRunHeroFilter(hero);
    }

    private void ToggleGhostDayMin10()
    {
        _coordinator?.ToggleGhostDayMin10();
    }

    private void SelectRun(int index)
    {
        _coordinator?.SelectRun(index);
    }

    private void SelectBattle(int index)
    {
        _coordinator?.SelectBattle(index);
    }

    private void TryReplaySelectedBattle(bool recordVideo = false)
    {
        if (_coordinator != null)
            _ = _coordinator.TryReplaySelectedBattleAsync(ActiveSelectedBattle, recordVideo);
    }

    // The delete button removes the selected favorite in Favorites and the selected run in Runs.
    private void TryDeleteSelectedRun()
    {
        if (_coordinator == null)
            return;
        if (_state.SectionMode == HistorySectionMode.Favorites)
            _ = _coordinator.TryRemoveSelectedFavoriteAsync();
        else
            _coordinator.TryDeleteSelectedRun(SelectedRun);
    }

    private void TryFavoriteSelectedBattle()
    {
        if (_coordinator != null)
            _ = _coordinator.TryFavoriteSelectedBattleAsync(ActiveSelectedBattle);
    }

    private Task<bool> SaveFavoriteNote(string battleId, string note) =>
        _coordinator?.TrySaveFavoriteNoteAsync(battleId, note) ?? Task.FromResult(false);

    private void TryCheckServerHealth()
    {
        if (_coordinator != null)
            _ = _coordinator.TryCheckServerHealthAsync();
    }

    private void SubmitAccountLinkCode(string? code)
    {
        if (_coordinator != null)
            _ = _coordinator.TryRedeemBazaarDbAccountAsync(code);
    }

    private void ToggleAccountLinkForm()
    {
        _coordinator?.ToggleAccountLinkForm();
    }

    private void MarkAccountLinkedManually()
    {
        _coordinator?.MarkAccountLinkedManually();
    }
}
