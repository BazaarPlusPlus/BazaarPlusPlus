using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.Ui;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Core.Cards.LiveCards;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.LiveBuildPanel.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTMPro;
using UnityEngine;

namespace BazaarPlusPlus.Android.LiveBuild;

/// <summary>
/// The Android shell around Core's <see cref="LiveBuildPanelState"/>, the counterpart of the
/// desktop's <c>LiveBuildPanel</c>: the live read (<see cref="AndroidLiveCardSnapshotReader"/>),
/// the touch view, and the manual refresh. Core owns the behavior and the data; this shell feeds
/// it taps and renders its snapshot. It is a Main Overlay Panel: the Overlay Panel Host opens and
/// closes it (<see cref="AndroidOverlayPanels"/>), for the touch menu, the back key, combat, a
/// scene change, another panel and the setting switched off; its close button and a failed
/// relayout request the close. Closing returns every rented card.
/// A refresh's fetch finishes on a pool thread, so its result is posted to
/// <see cref="AndroidMainThread"/> before <see cref="LiveBuildPanelState.CompleteRefresh"/> runs,
/// as the state requires. The view is laid out for the screen it opened on and laid out again
/// when the resolution, density, or safe area changes, once the new screen has held still
/// (<see cref="TouchRelayoutDebounce"/>) and no card is still setting up; a view that fails to
/// build leaves the panel closed, and the next open builds again. A card returned while
/// it sets up leaves the old view at once (<see cref="CardRentalCore{TCard, TParent}"/>), so
/// destroying the view never takes one.
/// </summary>
internal sealed class AndroidLiveBuildPanel
{
    private const float ScrollLogStep = 40f;

    private readonly Transform _parent;
    private readonly TMP_FontAsset _font;
    private readonly LiveBuildPanelState _state;
    private readonly AndroidLiveBuildData _data;
    private readonly Commands _commands;
    private AndroidLiveBuildView? _view;
    private string _screenKey = string.Empty;
    private readonly TouchRelayoutDebounce _relayout = new(string.Empty);
    private float _loggedScroll;
    private BppItemBoardId _selectedBoard = BppItemBoardId.LiveBoard;

    public AndroidLiveBuildPanel(
        Transform parent,
        TMP_FontAsset font,
        AndroidLiveBuildData data,
        Action<string> requestClose
    )
    {
        ArgumentNullException.ThrowIfNull(requestClose);
        _parent = parent;
        _font = font;
        _data = data;
        data.Warm("panel_created");
        _state = new LiveBuildPanelState(
            data.Recommendations,
            () => DateTimeOffset.UtcNow,
            AndroidHeroNames.TheDragons
        );
        _commands = new Commands(
            requestClose,
            Refresh,
            () => Browse(-1),
            () => Browse(+1),
            ToggleCandidate,
            CycleBoard
        );
        _state.BeginCorpusLoad();
    }

    /// <summary>What the view's controls call.</summary>
    internal sealed record Commands(
        Action<string> Close,
        Action Refresh,
        Action Previous,
        Action Next,
        Action<Guid> Toggle,
        Action CycleBoard
    );

    public bool IsOpen => _view?.IsVisible == true;

    public long Opened { get; private set; }

    public long Closed { get; private set; }

    public long Refreshes { get; private set; }

    public long RefreshesCompleted { get; private set; }

    public long Relayouts { get; private set; }

    /// <summary>Cards the panel's boards and inspection hold.</summary>
    public int Leases => _view?.Leases ?? 0;

    /// <summary>False when no view could be built: the panel stays closed.</summary>
    public bool Open(string source)
    {
        if (IsOpen)
            return true;
        _selectedBoard = BppItemBoardId.LiveBoard;
        var view = EnsureView();
        if (view == null)
            return false;
        var outcome = LiveCardSnapshotRead.Read(
            AndroidLiveCardSnapshotReader.Walk,
            new BppLogEvent(
                BppLogFeatureScope.Android,
                "android.live_build.live_snapshot.degraded",
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
            new BppLogEvent(BppLogFeatureScope.Android, "android.live_build.opened"),
            ("source", source),
            ("hero", snapshot.Hero),
            ("shop", snapshot.Shop.Cards.Count),
            ("stash", snapshot.Stash.Cards.Count),
            ("board", snapshot.Board.Cards.Count),
            ("matches_state", snapshot.MatchesState),
            ("corpus_state", snapshot.CorpusState),
            ("supporters", snapshot.Supporters.Count),
            ("screen", _screenKey),
            ("units_per_dp", Math.Round(layout.Metrics.UnitsPerDp, 3)),
            ("card_scale", Math.Round(layout.CardScale, 3)),
            ("leases", view.Leases)
        );
        return true;
    }

    /// <summary>False when it was not open.</summary>
    public bool Close(string reason)
    {
        var view = _view;
        if (view == null || !view.IsVisible)
            return false;
        var returned = view.ReturnAll(reason);
        _state.Close();
        view.SetVisible(false);
        Closed++;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.live_build.closed"),
            ("reason", reason),
            ("returned", returned),
            ("rental_leases", AndroidCardRental.ActiveLeases),
            ("rental_balanced", AndroidCardRental.IsBalanced)
        );
        return true;
    }

    /// <summary>A card is inspected: the back key closes it before the panel.</summary>
    public bool IsInspecting => IsOpen && _view!.Inspection.IsOpen;

    public void CloseInspection() => _view?.Inspection.Close("back_key");

    /// <summary>Main thread, every frame.</summary>
    public void Tick()
    {
        var view = _view;
        if (view == null || !view.IsVisible)
            return;
        if (_relayout.Observe(AndroidTouchScreen.Key(), view.SettingUp))
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
                new BppLogEvent(BppLogFeatureScope.Android, "android.live_build.scrolled"),
                ("from", Math.Round(_loggedScroll)),
                ("to", Math.Round(offset)),
                ("max", Math.Round(view.ScrollMax))
            );
            _loggedScroll = offset;
        }
    }

    /// <summary>The panel's lifetime ends: no later refresh completion has a view effect.</summary>
    public void Dispose()
    {
        Close("disposed");
        _state.Destroy();
        _view?.Destroy();
        _view = null;
    }

    // A view that fails to build leaves no view, never the destroyed one, and records no screen:
    // the panel reads closed, the old view's cards already returned, and the next Open builds again.
    private AndroidLiveBuildView? EnsureView()
    {
        var key = AndroidTouchScreen.Key();
        if (
            _view != null
            && !_view.NeedsSkinRefresh
            && string.Equals(key, _screenKey, StringComparison.Ordinal)
        )
            return _view;
        var before = _screenKey;
        var open = IsOpen;
        _view?.Destroy();
        _view = null;
        _screenKey = string.Empty;
        try
        {
            var layout = LiveBuildTouchLayout.For(AndroidTouchScreen.Read());
            _view = new AndroidLiveBuildView(_parent, _font, layout, _commands);
        }
        catch (Exception exception)
        {
            if (open)
            {
                _state.Close();
                Closed++;
            }
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.live_build.layout_failed"),
                exception,
                ("from", before),
                ("to", key),
                ("open", open),
                ("rental_leases", AndroidCardRental.ActiveLeases)
            );
            return null;
        }
        _screenKey = key;
        _relayout.LaidOutAt(key);
        return _view;
    }

    // The screen changed under the open panel: the old view returns its cards, and a view laid out
    // for the new screen renders the same state. False when no view could be built.
    private bool Relayout()
    {
        var before = _screenKey;
        var view = EnsureView();
        if (view == null)
            return false;
        view.SetVisible(true);
        view.Refresh(_state.BuildSnapshot(), boards: true, _selectedBoard);
        Relayouts++;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.live_build.relayout"),
            ("from", before),
            ("to", _screenKey),
            ("units_per_dp", Math.Round(view.Layout.Metrics.UnitsPerDp, 3)),
            ("card_scale", Math.Round(view.Layout.CardScale, 3)),
            ("rental_leases", AndroidCardRental.ActiveLeases),
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
        _view?.Refresh(_state.BuildSnapshot(), boards: true, _selectedBoard);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.live_build.board_selected"),
            ("board", _selectedBoard),
            ("leases", Leases)
        );
    }

    private void ToggleCandidate(Guid templateId)
    {
        if (!_state.ToggleCandidate(templateId))
            return;
        var snapshot = _state.BuildSnapshot();
        _view?.Refresh(snapshot, boards: true, _selectedBoard);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.live_build.candidate_toggled"),
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
        _view?.Refresh(snapshot, boards: true, _selectedBoard);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.live_build.browsed"),
            ("index", snapshot.RecommendationIndex),
            ("count", snapshot.RecommendationCount)
        );
    }

    private void Refresh()
    {
        var request = _state.BeginRefresh();
        if (request == LiveBuildRefreshRequest.Ignore)
            return;
        Refreshes++;
        _view?.Refresh(_state.BuildSnapshot(), boards: false, _selectedBoard);
        _ = RefreshAsync(_state, request, Guid.NewGuid());
    }

    // The catalog refresh resumes without a context, so the fetch may finish on a pool thread:
    // its result is posted to the main thread, where CompleteRefresh and the redraw run.
    private async Task RefreshAsync(
        LiveBuildPanelState state,
        LiveBuildRefreshRequest request,
        Guid requestId
    )
    {
        var result = await state.FetchRefreshAsync(request).ConfigureAwait(false);
        var fetchedOnMain = AndroidMainThread.Host.IsCurrentThread;
        AndroidMainThread.Host.Post(
            _ => CompleteRefresh(state, result, requestId, fetchedOnMain),
            null
        );
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
            new BppLogEvent(BppLogFeatureScope.Android, "android.live_build.refresh_completed"),
            ("request_id", requestId, BppLogCorrelationPolicy.Short),
            ("succeeded", result.Succeeded),
            ("outcome", result.Outcome),
            ("effect", effect),
            ("fetched_on_main_thread", fetchedOnMain),
            ("completed_on_main_thread", AndroidMainThread.Host.IsCurrentThread)
        );
        if (!ReferenceEquals(state, _state) || !IsOpen)
            return;
        switch (effect)
        {
            case LiveBuildRefreshEffect.Recommendations:
                _view!.Refresh(_state.BuildSnapshot(), boards: true, _selectedBoard);
                break;
            case LiveBuildRefreshEffect.Status:
                _view!.Refresh(_state.BuildSnapshot(), boards: false, _selectedBoard);
                break;
        }
    }
}
