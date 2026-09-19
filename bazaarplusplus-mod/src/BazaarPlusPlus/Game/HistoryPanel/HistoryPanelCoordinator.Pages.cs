#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.GameInterop;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanelCoordinator
{
    private sealed record ArchivePage(
        HistoryPage<HistoryRunRecord>? Runs,
        HistoryPage<HistoryBattleRecord>? Ghosts
    );

    private sealed record MaintenanceResult(int Restored);

    private readonly HistoryPanelPayloadFailureLogGate _detailFailures = new();
    private readonly LatestHistoryRead<ArchivePage> _archiveReads = new();
    private readonly LatestHistoryRead<HistoryPage<HistoryBattleRecord>> _battleReads = new();
    private readonly LatestHistoryRead<PvpBattleSnapshots> _detailReads = new();
    private readonly LatestHistoryRead<MaintenanceResult> _maintenanceReads = new();

    private HistoryBattleRecord? ActiveBattle() =>
        _state.SectionMode == HistorySectionMode.Ghost
            ? _state.GetSelectedGhostBattle(_state.GhostBattles)
            : _state.GetSelectedBattle();

    public void PageArchive(int direction)
    {
        var ghost = _state.SectionMode == HistorySectionMode.Ghost;
        var first = ghost ? _state.GhostPage.First : _state.RunPage.First;
        var last = ghost ? _state.GhostPage.Last : _state.RunPage.Last;
        LoadArchive(
            direction == 0 ? new() : new(direction < 0 ? first : last, Newer: direction < 0),
            false
        );
    }

    public void PageBattles(int direction) =>
        LoadBattlesForSelectedRun(
            new(
                direction < 0 ? _state.BattlePage.First : _state.BattlePage.Last,
                Newer: direction < 0
            ),
            false
        );

    private void LoadArchive(HistoryPageRequest request, bool preserveSelection)
    {
        ClearDeleteRunConfirmation();
        _state.PageLoading = true;
        var section = _state.SectionMode;
        var account = _state.CachedAccountId ?? "";
        var hero = _state.SelectedRunHero;
        var filter = _state.GhostBattleFilter;
        var day = _state.GhostDayMin10;
        var session = _session.Version;
        var selectedRun = preserveSelection ? GetSelectedRun()?.RunId : null;
        var selectedBattle = preserveSelection ? ActiveBattle()?.BattleId : null;
        var oldIndex =
            section == HistorySectionMode.Ghost
                ? _state.SelectedGhostBattleIndex
                : _state.SelectedRunIndex;
        ClearDetail();
        _battleReads.Clear();
        _archiveReads.Submit(
            () =>
            {
                if (section == HistorySectionMode.Runs)
                {
                    var page = _dataService.LoadRuns(request, hero);
                    if (page.Rows.Count == 0 && request.Cursor.HasValue)
                        page = _dataService.LoadRuns(new(request.Cursor, Newer: true), hero);
                    if (selectedRun != null && !page.Rows.Any(r => r.RunId == selectedRun))
                    {
                        var anchored = _dataService.LoadRuns(new(AnchorId: selectedRun), hero);
                        if (anchored.Rows.Any(r => r.RunId == selectedRun))
                            page = anchored;
                    }
                    return new ArchivePage(page, null);
                }
                var ghosts = _dataService.LoadGhosts(account, filter, day, request);
                if (ghosts.Rows.Count == 0 && request.Cursor.HasValue)
                    ghosts = _dataService.LoadGhosts(
                        account,
                        filter,
                        day,
                        new(request.Cursor, Newer: true)
                    );
                if (selectedBattle != null && !ghosts.Rows.Any(r => r.BattleId == selectedBattle))
                {
                    var anchored = _dataService.LoadGhosts(
                        account,
                        filter,
                        day,
                        new(AnchorId: selectedBattle)
                    );
                    if (anchored.Rows.Any(r => r.BattleId == selectedBattle))
                        ghosts = anchored;
                }
                return new ArchivePage(null, ghosts);
            },
            (result, error) =>
            {
                if (
                    !_session.IsCurrent(session)
                    || _state.SectionMode != section
                    || account != (_state.CachedAccountId ?? "")
                )
                    return;
                _state.PageLoading = false;
                if (error != null)
                {
                    SetStatusMessage(
                        HistoryPanelText.HistoryLoadFailed(error.Message),
                        StatusSeverity.Failure
                    );
                    _requestUiRefresh();
                    return;
                }
                if (result?.Runs is { } runs)
                {
                    _state.RunPage = runs;
                    var index = runs.FindIndex(r => r.RunId == selectedRun);
                    _state.SelectedRunIndex =
                        index >= 0
                            ? index
                            : ClampIndex(
                                preserveSelection && request.AnchorId == null ? oldIndex : 0,
                                runs.Rows.Count
                            );
                    LoadBattlesForSelectedRun(preserveBattleId: selectedBattle);
                }
                else if (result?.Ghosts is { } ghosts)
                {
                    _state.GhostPage = ghosts;
                    var index = ghosts.FindIndex(r => r.BattleId == selectedBattle);
                    _state.SelectedGhostBattleIndex = index >= 0 ? index : 0;
                    LoadSelectedDetail();
                    if (account.Length == 0)
                        SetStatusMessage(HistoryPanelText.AccountLink.SignedOut());
                }
                _requestUiRefresh();
            }
        );
        _requestUiRefresh();
    }

    private void LoadBattlesForSelectedRun(
        HistoryPageRequest? request = null,
        bool preserve = true,
        string? preserveBattleId = null
    )
    {
        var run = GetSelectedRun();
        var selected = preserve ? preserveBattleId ?? _state.GetSelectedBattle()?.BattleId : null;
        var session = _session.Version;
        var boundary =
            preserve && _state.GetSelectedBattle()?.RunId == run?.RunId
                ? _state.BattlePage.First
                : null;
        var pageRequest =
            request
            ?? (
                boundary.HasValue
                    ? new HistoryPageRequest(boundary, Inclusive: true)
                    : new(AnchorId: selected)
            );
        ClearDetail();
        _state.BattlePage = HistoryPage<HistoryBattleRecord>.Empty;
        if (run == null)
        {
            _requestUiRefresh();
            return;
        }
        _battleReads.Submit(
            () =>
            {
                var page = _dataService.LoadBattles(run.RunId, pageRequest);
                if (selected != null && !page.Rows.Any(b => b.BattleId == selected))
                {
                    var anchored = _dataService.LoadBattles(run.RunId, new(AnchorId: selected));
                    if (anchored.Rows.Any(b => b.BattleId == selected))
                        return anchored;
                }
                return page;
            },
            (page, error) =>
            {
                if (
                    !_session.IsCurrent(session)
                    || _state.SectionMode != HistorySectionMode.Runs
                    || GetSelectedRun()?.RunId != run.RunId
                )
                    return;
                if (error != null)
                    SetStatusMessage(
                        HistoryPanelText.HistoryLoadFailed(error.Message),
                        StatusSeverity.Failure
                    );
                _state.BattlePage = page ?? HistoryPage<HistoryBattleRecord>.Empty;
                _state.SelectedBattleIndex = Math.Max(
                    0,
                    _state.BattlePage.FindIndex(b => b.BattleId == selected)
                );
                LoadSelectedDetail();
                _requestUiRefresh();
            }
        );
    }

    private void ClearDetail()
    {
        _detailReads.Clear();
        _state.DetailSnapshots = null;
        _state.DetailBattleId = null;
        _state.DetailFailed = false;
        _state.DetailLoading = false;
        _requestPreviewRefresh();
    }

    private void LoadSelectedDetail()
    {
        ClearDetail();
        var battle = ActiveBattle();
        if (battle == null)
            return;
        var account = _state.CachedAccountId ?? "";
        var session = _session.Version;
        _state.DetailLoading = true;
        _state.DetailBattleId = battle.BattleId;
        _requestPreviewRefresh();
        _detailReads.Submit(
            () => _dataService.LoadDetail(battle, account),
            (snapshots, error) =>
            {
                if (
                    !_session.IsCurrent(session)
                    || ActiveBattle()?.BattleId != battle.BattleId
                    || account != (_state.CachedAccountId ?? "")
                )
                    return;
                _state.DetailLoading = false;
                _state.DetailSnapshots = snapshots;
                _state.DetailFailed = error != null;
                if (error != null)
                {
                    SetStatusMessage(
                        HistoryPanelText.HistoryLoadFailed(error.Message),
                        StatusSeverity.Failure
                    );
                    _detailFailures.Report(
                        battle.BattleId,
                        error.Message,
                        HistoryPanelPreviewPayloadReasonCode.PayloadInvalid,
                        error
                    );
                }
                else
                    _detailFailures.Clear(battle.BattleId);
                _requestPreviewRefresh();
                _requestUiRefresh();
            }
        );
    }

    private void ObserveAccount()
    {
        var account = NormalizeAccountId(BppClientCacheBridge.TryGetProfileAccountId());
        if (account == _state.CachedAccountId)
            return;
        _session.Begin();
        _state.CachedAccountId = account;
        _state.GhostPage = HistoryPage<HistoryBattleRecord>.Empty;
        _state.ReplayActionInProgress =
            _state.GhostSyncInProgress =
            _state.AccountLinkInProgress =
                false;
        _state.ServerHealthProbeInProgress = false;
        RefreshAccountLinkIdentityFromGame();
        ClearDetail();
        RefreshData();
        StartGhostMaintenance();
    }

    private void StartGhostMaintenance()
    {
        var account = _state.CachedAccountId ?? "";
        var session = _session.Version;
        var token = _session.Token;
        _maintenanceReads.Submit(
            () => new(_dataService.MaintainGhosts(account, token)),
            (result, error) =>
            {
                if (
                    _session.IsCurrent(session)
                    && result?.Restored > 0
                    && _state.SectionMode == HistorySectionMode.Ghost
                )
                    RefreshGhostData();
            }
        );
    }
}
