#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Favorites;

namespace BazaarPlusPlus.Game.HistoryPanel;

public sealed partial class HistoryPanelCoordinator
{
    private sealed record FavoriteCheck(string BattleId, bool IsFavorite);

    private readonly LatestHistoryRead<FavoriteCheck> _favoriteReads = new();
    private bool _favoriteCopiesSwept;

    public bool FavoritesAvailable => _favorites != null;

    public bool IsSelectedBattleFavorite(HistoryBattleRecord? battle) =>
        battle != null
        && _state.FavoriteCheckBattleId == battle.BattleId
        && _state.FavoriteCheckIsFavorite;

    private bool CanReplaySelectedFavorite(out string reason)
    {
        if (_favorites == null)
        {
            reason = HistoryPanelText.PanelUnavailable();
            return false;
        }
        return _favorites.CanReplay(_state.GetSelectedFavorite(), out reason);
    }

    private void CheckFavorite(string battleId)
    {
        _state.FavoriteCheckBattleId = null;
        _state.FavoriteCheckIsFavorite = false;
        var favorites = _favorites;
        if (favorites == null)
            return;
        var session = _session.Version;
        _favoriteReads.Submit(
            () => new FavoriteCheck(battleId, favorites.IsFavorite(battleId)),
            (check, _) =>
            {
                if (!_session.IsCurrent(session) || check == null)
                    return;
                _state.FavoriteCheckBattleId = check.BattleId;
                _state.FavoriteCheckIsFavorite = check.IsFavorite;
                _requestUiRefresh();
            }
        );
    }

    // Once per game launch, the first time Favorites opens: copies left by a crash or a failed
    // delete. A failed sweep leaves the copies for the next launch.
    private void SweepFavoriteCopies()
    {
        if (_favorites == null || _favoriteCopiesSwept)
            return;
        _favoriteCopiesSwept = true;
        _ = SweepFavoriteCopiesAsync(_favorites, _session.Token);
    }

    private static async Task SweepFavoriteCopiesAsync(
        HistoryFavoriteService favorites,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await favorites.SweepOrphanCopiesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Best-effort cleanup; the copies stay until a later sweep succeeds.
        }
    }

    /// <summary>
    /// The favorite button. In Favorites it returns the selected favorite, whose note the host's
    /// dialog edits, and does nothing else (null when none is selected). In Runs and Ghost it
    /// favorites the active battle and returns null.
    /// </summary>
    public HistoryFavoriteRecord? FavoriteSelected()
    {
        if (_state.SectionMode == HistorySectionMode.Favorites)
            return _state.GetSelectedFavorite();
        _ = TryFavoriteSelectedBattleAsync(ActiveBattle());
        return null;
    }

    // Runs and Ghost: favorite the selected battle, or open it in Favorites when it already is.
    private async Task TryFavoriteSelectedBattleAsync(HistoryBattleRecord? battle)
    {
        if (battle == null || _favorites == null || _state.FavoriteActionInProgress)
            return;
        if (IsSelectedBattleFavorite(battle))
        {
            ShowFavorite(battle.BattleId);
            return;
        }

        _state.FavoriteActionInProgress = true;
        var session = _session.Version;
        SetStatusMessage(HistoryPanelText.SavingFavorite(), StatusSeverity.Pending);
        _requestUiRefresh();
        FavoriteAddResult result;
        try
        {
            result = await _favorites.AddAsync(
                battle,
                _state.CachedAccountId ?? string.Empty,
                _session.Token
            );
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            result = new FavoriteAddResult(FavoriteAddOutcome.Failed, ex.Message);
        }
        if (!_session.IsCurrent(session))
            return;
        _state.FavoriteActionInProgress = false;
        if (result.Succeeded)
        {
            _state.FavoriteCheckBattleId = battle.BattleId;
            _state.FavoriteCheckIsFavorite = true;
        }
        SetStatusMessage(
            result.Outcome switch
            {
                FavoriteAddOutcome.Added => HistoryPanelText.FavoriteSaved(),
                FavoriteAddOutcome.AddedWithoutReplay =>
                    HistoryPanelText.FavoriteSavedWithoutReplay(),
                FavoriteAddOutcome.AlreadyFavorite => HistoryPanelText.FavoritedBattle(),
                FavoriteAddOutcome.Busy => HistoryPanelText.FavoriteBusy(),
                _ => HistoryPanelText.FavoriteFailed(result.Error ?? HistoryPanelText.Unknown()),
            },
            result.Succeeded ? StatusSeverity.Success : StatusSeverity.Failure
        );
        _requestUiRefresh();
    }

    private void ShowFavorite(string battleId)
    {
        _state.SectionMode = HistorySectionMode.Favorites;
        ClearDetail();
        LoadArchive(new(AnchorId: battleId), preserveSelection: true);
        SweepFavoriteCopies();
    }

    public bool IsRemoveFavoriteConfirmationActive(string battleId, float now) =>
        _state.DeleteRunConfirmation.IsActiveFor(battleId, now);

    // The delete button in Favorites: a first press arms a confirmation, a second removes the
    // favorite, its note and its replay copy.
    private async Task TryRemoveSelectedFavoriteAsync()
    {
        var favorite = _state.GetSelectedFavorite();
        if (favorite == null || _favorites == null || _state.FavoriteActionInProgress)
            return;
        var now = _unscaledTime();
        if (!IsRemoveFavoriteConfirmationActive(favorite.BattleId, now))
        {
            _state.DeleteRunConfirmation = new DeleteConfirmation(favorite.BattleId, now + 5f);
            SetStatusMessage(
                HistoryPanelText.RemoveFavoriteConfirmPrompt(),
                isDeleteConfirmation: true
            );
            _requestUiRefresh();
            return;
        }
        ClearDeleteRunConfirmation();
        _state.FavoriteActionInProgress = true;
        var session = _session.Version;
        try
        {
            await _favorites.RemoveAsync(favorite.BattleId, _session.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            if (!_session.IsCurrent(session))
                return;
            _state.FavoriteActionInProgress = false;
            SetStatusMessage(HistoryPanelText.FavoriteFailed(ex.Message), StatusSeverity.Failure);
            _requestUiRefresh();
            return;
        }
        if (!_session.IsCurrent(session))
            return;
        _state.FavoriteActionInProgress = false;
        LoadArchive(new(_state.FavoritePage.First, Inclusive: true), preserveSelection: true);
        SetStatusMessage(HistoryPanelText.FavoriteRemoved(), StatusSeverity.Success);
        _requestUiRefresh();
    }

    // False keeps the note dialog open with the draft, so a failed save loses nothing.
    public async Task<bool> TrySaveFavoriteNoteAsync(string battleId, string note)
    {
        if (_favorites == null)
            return false;
        var session = _session.Version;
        var favorites = _favorites;
        bool saved;
        try
        {
            saved = await Task.Run(() => favorites.UpdateNote(battleId, note));
        }
        catch (Exception ex)
        {
            if (_session.IsCurrent(session))
            {
                SetStatusMessage(
                    HistoryPanelText.FavoriteNoteSaveFailed(ex.Message),
                    StatusSeverity.Failure
                );
                _requestUiRefresh();
            }
            return false;
        }
        if (!_session.IsCurrent(session))
            return saved;
        if (!saved)
        {
            SetStatusMessage(
                HistoryPanelText.FavoriteNoteSaveFailed(HistoryPanelText.FavoriteBattleNotFound()),
                StatusSeverity.Failure
            );
            _requestUiRefresh();
            return false;
        }
        LoadArchive(new(_state.FavoritePage.First, Inclusive: true), preserveSelection: true);
        SetStatusMessage(HistoryPanelText.FavoriteNoteSaved(), StatusSeverity.Success);
        _requestUiRefresh();
        return true;
    }
}
