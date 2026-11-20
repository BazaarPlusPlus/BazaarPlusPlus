using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Core.Cards.LiveCards;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.LiveBuildPanel.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.LiveBuild;

// Returning a pending rental detaches it before view teardown, so destroying the old view
// cannot destroy a card that is still setting up.
internal sealed class TouchLiveBuildPanel
{
    private const float ScrollLogStep = 40f;

    private readonly Transform _parent;
    private readonly TMP_FontAsset _font;
    private readonly LiveBuildPanelState _state;
    private readonly TouchLiveBuildData _data;
    private readonly Commands _commands;
    private readonly TouchViewSlot<TouchLiveBuildView> _slot = new(view => view.Destroy());
    private readonly TouchRelayoutDebounce _relayout = new(string.Empty);
    private float _loggedScroll;
    private BppItemBoardId _selectedBoard = BppItemBoardId.LiveBoard;

    public TouchLiveBuildPanel(
        Transform parent,
        TMP_FontAsset font,
        TouchLiveBuildData data,
        Action<string> requestClose
    )
    {
        if (requestClose == null)
            throw new ArgumentNullException(nameof(requestClose));
        _parent = parent;
        _font = font;
        _data = data;
        data.Warm("panel_created");
        _state = new LiveBuildPanelState(
            data.Recommendations,
            () => DateTimeOffset.UtcNow,
            () => MobilePlatform.Port.HeroName(CardHero.TheDragons)
        );
        _commands = new Commands(
            requestClose,
            Refresh,
            () => Browse(-1),
            () => Browse(+1),
            ToggleCandidate,
            CycleBoard,
            ToggleIncludeTiny
        );
        _state.BeginCorpusLoad();
    }

    internal sealed record Commands(
        Action<string> Close,
        Action Refresh,
        Action Previous,
        Action Next,
        Action<Guid> Toggle,
        Action CycleBoard,
        Action ToggleIncludeTiny
    );

    public bool IsOpen => _slot.View?.IsVisible == true;

    public long Opened { get; private set; }

    public long Closed { get; private set; }

    public long Refreshes { get; private set; }

    public long RefreshesCompleted { get; private set; }

    public long Relayouts { get; private set; }

    public int Leases => _slot.View?.Leases ?? 0;

    public bool Open(string source)
    {
        if (IsOpen)
            return true;
        _selectedBoard = BppItemBoardId.LiveBoard;
        var view = EnsureView();
        if (view == null)
            return false;
        var outcome = LiveCardSnapshotRead.Read(
            MobilePlatform.Port.WalkLiveCards,
            new BppLogEvent(
                BppLogFeatureScope.Touch,
                "touch.live_build.live_snapshot.degraded",
                storm: ["section", "reason_code"]
            ),
            LiveCardSnapshotLogValues.MemberName
        );
        _state.Open(outcome.Snapshot, _data.NextSupporters(1));
        view.SetVisible(true);
        _loggedScroll = 0f;
        var snapshot = _state.BuildSnapshot();
        view.Refresh(snapshot, boards: true, _selectedBoard);
        Opened++;
        var layout = view.Layout;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.live_build.opened"),
            ("source", source),
            ("hero", snapshot.Hero),
            ("shop", snapshot.Shop.Cards.Count),
            ("stash", snapshot.Stash.Cards.Count),
            ("board", snapshot.Board.Cards.Count),
            ("matches_state", snapshot.MatchesState),
            ("corpus_state", snapshot.CorpusState),
            ("supporters", snapshot.Supporters.Count),
            ("screen", _slot.ScreenKey),
            ("units_per_dp", Math.Round(layout.Metrics.UnitsPerDp, 3)),
            ("card_scale", Math.Round(layout.CardScale, 3)),
            ("leases", view.Leases)
        );
        return true;
    }

    public bool Close(string reason)
    {
        var view = _slot.View;
        if (view == null || !view.IsVisible)
            return false;
        var returned = view.ReturnAll(reason);
        _state.Close();
        view.SetVisible(false);
        Closed++;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.live_build.closed"),
            ("reason", reason),
            ("returned", returned),
            ("rental_leases", MobilePlatform.Port.CardRental.ActiveLeases),
            ("rental_balanced", MobilePlatform.Port.CardRental.IsBalanced)
        );
        return true;
    }

    public bool IsInspecting => IsOpen && _slot.View!.Inspection.IsOpen;

    public void CloseInspection() => _slot.View?.Inspection.Close("back_key");

    public void Tick()
    {
        var view = _slot.View;
        if (view == null || !view.IsVisible)
            return;
        var key = TouchScreen.Key();
        var settingUp =
            !string.Equals(key, _relayout.LaidOut, StringComparison.Ordinal) && view.SettingUp;
        if (_relayout.Observe(key, settingUp))
        {
            if (!Relayout())
                _commands.Close("relayout_failed");
            return;
        }
        view.Tick();
        var offset = view.ScrollOffset;
        if (Math.Abs(offset - _loggedScroll) >= ScrollLogStep && view.ScrollSettled)
        {
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Touch, "touch.live_build.scrolled"),
                ("from", Math.Round(_loggedScroll)),
                ("to", Math.Round(offset)),
                ("max", Math.Round(view.ScrollMax))
            );
            _loggedScroll = offset;
        }
    }

    public void Dispose()
    {
        Close("disposed");
        _state.Destroy();
        _slot.Drop();
    }

    private TouchLiveBuildView? EnsureView()
    {
        var key = TouchScreen.Key();
        if (_slot.IsCurrent(key) && !_slot.View!.NeedsSkinRefresh)
            return _slot.View;
        var before = _slot.ScreenKey;
        var open = IsOpen;
        var view = _slot.Rebuild(
            key,
            () =>
                new TouchLiveBuildView(
                    _parent,
                    _font,
                    LiveBuildTouchLayout.For(TouchScreen.Read()),
                    _commands
                ),
            exception => LayoutFailed(exception, before, key, open)
        );
        if (view != null)
            _relayout.LaidOutAt(key);
        return view;
    }

    private void LayoutFailed(Exception exception, string before, string key, bool open)
    {
        if (open)
        {
            _state.Close();
            Closed++;
        }
        BppLog.ErrorEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.live_build.layout_failed"),
            exception,
            ("from", before),
            ("to", key),
            ("open", open),
            ("rental_leases", MobilePlatform.Port.CardRental.ActiveLeases)
        );
    }

    private bool Relayout()
    {
        var before = _slot.ScreenKey;
        var view = EnsureView();
        if (view == null)
            return false;
        view.SetVisible(true);
        view.Refresh(_state.BuildSnapshot(), boards: true, _selectedBoard);
        Relayouts++;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.live_build.relayout"),
            ("from", before),
            ("to", _slot.ScreenKey),
            ("units_per_dp", Math.Round(view.Layout.Metrics.UnitsPerDp, 3)),
            ("card_scale", Math.Round(view.Layout.CardScale, 3)),
            ("rental_leases", MobilePlatform.Port.CardRental.ActiveLeases),
            ("held_frames", _relayout.HeldFrames)
        );
        return true;
    }

    private void CycleBoard()
    {
        _selectedBoard = _selectedBoard switch
        {
            BppItemBoardId.LiveBoard => BppItemBoardId.LiveStash,
            BppItemBoardId.LiveStash => BppItemBoardId.LiveShop,
            _ => BppItemBoardId.LiveBoard,
        };
        _slot.View?.Refresh(_state.BuildSnapshot(), boards: true, _selectedBoard);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.live_build.board_selected"),
            ("board", _selectedBoard),
            ("leases", Leases)
        );
    }

    private void ToggleCandidate(Guid templateId)
    {
        if (!_state.ToggleCandidate(templateId))
            return;
        var snapshot = _state.BuildSnapshot();
        _slot.View?.Refresh(snapshot, boards: true, _selectedBoard);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.live_build.candidate_toggled"),
            ("candidates", snapshot.CandidateTemplateIds.Count),
            ("matches_state", snapshot.MatchesState),
            ("recommendations", snapshot.RecommendationCount),
            ("final_build", snapshot.FinalBuild.Cards.Count)
        );
    }

    private void Browse(int step)
    {
        if (!(step < 0 ? _state.Previous() : _state.Next()))
            return;
        var snapshot = _state.BuildSnapshot();
        _slot.View?.Refresh(snapshot, boards: true, _selectedBoard);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.live_build.browsed"),
            ("index", snapshot.RecommendationIndex),
            ("count", snapshot.RecommendationCount)
        );
    }

    private void ToggleIncludeTiny()
    {
        if (_state.ToggleIncludeTiny())
            _slot.View?.Refresh(_state.BuildSnapshot(), boards: true, _selectedBoard);
    }

    private void Refresh()
    {
        var request = _state.BeginRefresh();
        if (request == LiveBuildRefreshRequest.Ignore)
            return;
        Refreshes++;
        _slot.View?.Refresh(_state.BuildSnapshot(), boards: false, _selectedBoard);
        _ = RefreshAsync(_state, request, Guid.NewGuid());
    }

    // The fetch can complete on a pool thread. CompleteRefresh and redraw need the main thread.
    private async Task RefreshAsync(
        LiveBuildPanelState state,
        LiveBuildRefreshRequest request,
        Guid requestId
    )
    {
        var result = await state.FetchRefreshAsync(request).ConfigureAwait(false);
        var platform = MobilePlatform.Port;
        var fetchedOnMain = platform.IsMainThread;
        platform.PostToMainThread(() => CompleteRefresh(state, result, requestId, fetchedOnMain));
    }

    private void CompleteRefresh(
        LiveBuildPanelState state,
        BuildRecommendationRefreshResult result,
        Guid requestId,
        bool fetchedOnMain
    )
    {
        var effect = state.CompleteRefresh(result, requestId);
        RefreshesCompleted++;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.live_build.refresh_completed"),
            ("request_id", requestId, BppLogCorrelationPolicy.Short),
            ("succeeded", result.Succeeded),
            ("outcome", result.Outcome),
            ("effect", effect),
            ("fetched_on_main_thread", fetchedOnMain),
            ("completed_on_main_thread", MobilePlatform.Port.IsMainThread)
        );
        if (!ReferenceEquals(state, _state) || !IsOpen)
            return;
        switch (effect)
        {
            case LiveBuildRefreshEffect.Recommendations:
                _slot.View!.Refresh(_state.BuildSnapshot(), boards: true, _selectedBoard);
                break;
            case LiveBuildRefreshEffect.Status:
                _slot.View!.Refresh(_state.BuildSnapshot(), boards: false, _selectedBoard);
                break;
        }
    }
}
