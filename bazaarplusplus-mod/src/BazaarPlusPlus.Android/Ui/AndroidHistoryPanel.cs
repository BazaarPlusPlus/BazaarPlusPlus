using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Android.Supporters;
using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The Android twin of the desktop's <c>HistoryPanel</c>: it owns History's state and Core's
/// <see cref="HistoryPanelCoordinator"/> for the host's lifetime and renders Core's view model
/// through <see cref="AndroidHistoryPanelView"/>. The selected battle's boards are the game's own
/// cards, rented into the view's two board regions (<see cref="AndroidHistoryBoard"/>, the
/// counterpart of the desktop's <c>HistoryPanel.NativeBoards</c>); a tap inspects a card, and the
/// skills sheet shows both players' skills. Favorites, delete (refused for a run Seal eligibility
/// protects), the More dialog with the account link, and the note editor act through
/// the coordinator as the desktop's do. It is a Main Overlay Panel: its lifecycle is the Overlay
/// Panel Host's (<see cref="AndroidOverlayPanels"/>), which opens it from the touch menu and the
/// settings row, and closes it for the back key (an open dialog first), combat, a scene change
/// into combat (<see cref="SceneChangeClosePolicy.OnlyWhenInCombat"/>), another panel opening, and
/// <c>[Android] HistoryPanel</c> switching off (from the settings row or the More dialog's own
/// switch); the close button and a replay start request a close. On Android a combat loads no
/// scene, so the host's per-frame combat rule is what closes it for one. Every other scene change
/// returns its cards, since the rental destroys them, and renders them again on the next frame;
/// the note and More dialogs hold no card and stay open (<see cref="AndroidHistoryOverlays"/>). A
/// new screen size lays the view out again and keeps an open note's draft; the view's lifetime is
/// Core's <see cref="TouchViewSlot{TView}"/>, so a view that fails to build leaves the panel
/// closed, and the next open builds again. A replay started from it
/// reopens it with the selection kept once the replay session is over and the lobby is the active
/// scene, unless the panel was closed meanwhile. The Record action is not offered:
/// the host records no video. Every entry point runs inside the main-thread pump's scope, so the
/// coordinator's continuations resume on the main thread.
/// </summary>
internal sealed class AndroidHistoryPanel : IDisposable
{
    public const string PanelId = "history";
    private const string ReplayStarted = "replay_started";

    private readonly HistoryPanelState _state = new();
    private readonly HistoryPanelCoordinator _coordinator;
    private readonly HistoryPanelDependencies _dependencies;
    private readonly IRunContext _runContext;
    private readonly Func<ISavedReplayRuntime?> _runtime;
    private readonly AndroidCardTemplateIndex _templates;
    private readonly Func<TMP_FontAsset?> _font;
    private readonly AndroidSupporterData? _supporters;
    private readonly AndroidHistoryBoard _upper;
    private readonly AndroidHistoryBoard _lower;
    private readonly AndroidPanelHandle _handle;
    private readonly TouchViewSlot<AndroidHistoryPanelView> _slot;
    private AndroidHistoryInspection? _inspection;
    private AndroidHistorySkillsSheet? _skills;
    private readonly AndroidNativeSkillOverlay _nativeSkills = new(
        AndroidHistoryPanelView.SortingOrder + 1
    );
    private IReadOnlyList<IReadOnlyList<BppItemBoardCard>> _skillSides = [];
    private AndroidHistoryMoreDialog? _more;
    private AndroidHistoryNoteDialog? _note;
    private AndroidHistoryOverlays? _overlays;
    private HistoryPanelViewModel? _model;
    private PvpBattleSnapshots? _shownSnapshots;
    private string? _shownBattleId;
    private bool _shownBoth;
    private (HistoryBoardFacts Player, HistoryBoardFacts Opponent) _shownFacts;
    private readonly HistoryPanelUiRefresh _uiRefresh = new();
    private bool _boardsDirty;
    private bool _disposed;
    private long _opened;
    private long _relayouts;
    private long _boardRenders;

    public AndroidHistoryPanel(
        HistoryPanelDependencies dependencies,
        Func<ISavedReplayRuntime?> runtime,
        AndroidCardTemplateIndex templates,
        Func<TMP_FontAsset?> font,
        AndroidOverlayPanels panels,
        AndroidSupporterData? supporters
    )
    {
        ArgumentNullException.ThrowIfNull(panels);
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _font = font ?? throw new ArgumentNullException(nameof(font));
        _supporters = supporters;
        _runContext = dependencies.RunContext;
        _slot = new TouchViewSlot<AndroidHistoryPanelView>(DropView);
        _upper = new AndroidHistoryBoard("history.upper", card => Inspect(card, "upper"));
        _lower = new AndroidHistoryBoard("history.lower", card => Inspect(card, "lower"));
        _coordinator = new HistoryPanelCoordinator(
            _state,
            dependencies,
            RefreshUi,
            RefreshBoards,
            new HistoryPanelReplayReturnPort(
                HideForReplay,
                ReturnFromReplay,
                _runtime,
                AndroidLobbyScene.IsActive
            )
        );
        _handle = panels.Register(handle => new OverlayPanelRegistration(
            PanelId,
            onOpen: () => OpenInternal(handle.OpenSource),
            onClose: reason => Hide(handle.Describe(reason)),
            tick: Tick
        )
        {
            // The panel stays open across scenes, except into a combat, as on the desktop.
            SceneChangeClose = SceneChangeClosePolicy.OnlyWhenInCombat,
            IsModalInputActive = () => _overlays?.AnyOpen == true,
            DismissModal = () => _overlays?.DismissTop("back_key"),
            OnSceneChanged = OnSceneChanged,
        });
    }

    public bool IsVisible { get; private set; }

    /// <summary>Panel sessions opened and not yet closed: 0 or 1, for the hooks summary.</summary>
    public long OpenSessions => IsVisible ? 1 : 0;

    public long Opened => Interlocked.Read(ref _opened);

    public int ViewRoots => View?.RootCount ?? 0;

    /// <summary>Cards History holds from the rental: both boards, the skills sheet, the inspection.</summary>
    public int Leases =>
        _upper.Leases + _lower.Leases + (_skills?.Leases ?? 0) + (_inspection?.Leases ?? 0);

    public BppLogField[] Summary() =>
        [
            ("history_open_sessions", OpenSessions),
            ("history_opened", Opened),
            ("history_view_roots", ViewRoots),
            ("history_leases", Leases),
            ("history_board_renders", _boardRenders),
            ("history_relayouts", _relayouts),
        ];

    /// <summary>Main thread: opens the panel unless a combat is running, it is off, or no font is ready.</summary>
    public void Open(string reason) =>
        AndroidUiActions.Run(
            "history_open",
            () =>
            {
                switch (_handle.RequestOpen(reason))
                {
                    case OverlayRequestOutcome.SuppressedByCombat:
                        LogOpenSkipped("combat_active");
                        break;
                }
            }
        );

    // The host's tick, every frame: the return from a replay while closed, the open panel's work
    // while open.
    private void Tick(float deltaTime, bool isOpen)
    {
        if (_disposed)
            return;
        try
        {
            if (!isOpen)
            {
                _coordinator.TickHidden();
                return;
            }
            // A visible panel with no view, its last build failed, builds again or closes.
            if (!_slot.IsCurrent(AndroidTouchScreen.Key()) && !Relayout())
            {
                _handle.RequestClose("relayout_failed");
                return;
            }
            _coordinator.Tick(_dependencies.UnscaledTime());
            FlushUi();
            if (_boardsDirty)
            {
                _boardsDirty = false;
                RefreshBoards();
            }
            var facts = (PlayerStatus, OpponentStatus);
            if (facts != _shownFacts)
                RefreshBoardMessages();
            View?.Tick();
            _nativeSkills.Tick();
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("history_tick", exception);
        }
    }

    // The coordinator hides the panel for a replay it started and keeps the return pending.
    private void HideForReplay() => _handle.RequestClose(ReplayStarted);

    // The coordinator's return from a replay. A combat keeps the return pending, so it is tried
    // again next frame.
    private void ReturnFromReplay() => _handle.RequestOpen("replay_returned");

    // A scene loaded and the panel is still open, or was closed: the rental destroys every card
    // right after this, so the cards go back first and come again next frame. The dialogs hold no
    // card and stay open, a note on its unsaved draft.
    private void OnSceneChanged()
    {
        var dialogsKept = _overlays?.SceneChanged("scene_changed") ?? 0;
        var returned = ReturnBoards();
        _boardsDirty = IsVisible;
        if (!IsVisible)
            return;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.scene_changed"),
            ("returned", returned),
            ("dialogs_kept", dialogsKept),
            ("note_draft_kept", _note?.IsOpen == true),
            ("note_length", _note?.Draft?.Text.Length ?? 0)
        );
    }

    private static void LogOpenSkipped(string reasonCode) =>
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.open_skipped"),
            ("reason_code", reasonCode)
        );

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        using var scope = AndroidMainThread.Host.Enter();
        _handle.Dispose();
        if (IsVisible)
            _coordinator.OnPanelHidden();
        IsVisible = false;
        _coordinator.Dispose();
        ReturnCards("disposed");
        _slot.Drop();
    }

    private AndroidHistoryPanelView? View => _slot.View;

    private HistoryBoardFacts PlayerStatus => _shownBoth ? _lower.Facts() : _upper.Facts();

    private HistoryBoardFacts OpponentStatus => _shownBoth ? _upper.Facts() : default;

    // The host's open: the switch and the combat gate already passed. False when no view could be
    // built, and the panel stays closed. A return from a replay resumes its selection.
    private bool OpenInternal(string reason)
    {
        if (_disposed)
            return false;
        if (IsVisible)
            return true;
        if (!EnsureView())
            return false;
        // Ghost replays count their snapshot templates on a pool thread; the index they read is
        // built here, on the main thread, before any download can start.
        _templates.EnsureBuilt();
        IsVisible = true;
        Interlocked.Increment(ref _opened);
        var resumeSelection = _coordinator.OnPanelShown();
        View!.SetVisible(true);
        RefreshUi();
        FlushUi();
        RefreshBoards();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.opened"),
            ("reason", reason),
            ("resume_selection", resumeSelection),
            ("section", _state.SectionMode),
            ("template_index", _templates.Count),
            ("record_visible", _model?.RecordAndReplayButtonVisible ?? false),
            ("screen", _slot.ScreenKey)
        );
        return true;
    }

    // Every close but a replay start drops a pending return from a replay, the panel already
    // hidden by the replay included.
    private void Hide(string reason)
    {
        if (reason != ReplayStarted)
            CancelReturn(reason);
        if (!IsVisible)
            return;
        IsVisible = false;
        _coordinator.OnPanelHidden();
        var returned = ReturnCards(reason);
        View?.SetVisible(false);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.closed"),
            ("reason", reason),
            ("return_after_replay", _coordinator.ReplayReturnPending),
            ("returned", returned),
            ("rental_leases", AndroidCardRental.ActiveLeases)
        );
    }

    private void CancelReturn(string reason)
    {
        if (!_coordinator.CancelReplayReturn())
            return;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.return_cancelled"),
            ("reason", reason)
        );
    }

    // Every overlay closes and every card goes back to the rental.
    private int ReturnCards(string reason)
    {
        _overlays?.CloseAll(reason);
        return ReturnBoards();
    }

    private int ReturnBoards()
    {
        var returned = _upper.ReturnAll() + _lower.ReturnAll();
        _shownSnapshots = null;
        _shownBattleId = null;
        return returned;
    }

    // The view for the current screen, built when the screen changed or the last build failed.
    // False when none could be built: no font yet, or the build threw (LayoutFailed).
    private bool EnsureView()
    {
        var key = AndroidTouchScreen.Key();
        if (_slot.IsCurrent(key) && !AndroidHistoryUi.Skin.NeedsRefresh)
            return true;
        var font = _font();
        if (font == null)
        {
            _slot.Drop();
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.history.open_skipped"),
                ("reason_code", "font_unavailable")
            );
            return false;
        }
        var before = _slot.ScreenKey;
        var visible = IsVisible;
        // Rebuild, not Ensure: a stale skin rebuilds the view for the same screen.
        return _slot.Rebuild(
                key,
                () => BuildView(font),
                exception => LayoutFailed(exception, before, key, visible)
            ) != null;
    }

    // The view and the overlays laid over it. The view destroys its own canvas when it throws; a
    // throw after it disposes it, and with it every overlay under its surface. The overlay fields
    // are set only once every overlay is built.
    private AndroidHistoryPanelView BuildView(TMP_FontAsset font)
    {
        var view = new AndroidHistoryPanelView(
            font,
            new AndroidHistoryPanelView.ViewActions(
                () => _handle.RequestClose("close_button"),
                _uiRefresh.Guard(() =>
                    _ = _coordinator.TryReplaySelectedBattleAsync(ActiveSelectedBattle, false)
                ),
                _uiRefresh.Guard<int>(_coordinator.SelectRun),
                _uiRefresh.Guard<int>(_coordinator.SelectBattle),
                _coordinator.SetSectionMode,
                _uiRefresh.Guard<int>(_coordinator.PageArchive),
                _uiRefresh.Guard<int>(_coordinator.PageBattles),
                OpenMore,
                _uiRefresh.Guard(Delete),
                _uiRefresh.Guard(Favorite),
                _uiRefresh.Guard(OpenSkills)
            ),
            _supporters
        );
        AndroidHistoryNoteDialog? note = null;
        try
        {
            var layout = view.Layout;
            _upper.Attach(view.UpperBoard, layout);
            _lower.Attach(view.LowerBoard, layout);
            // Later siblings draw over the sheet and its rented cards.
            var skills = new AndroidHistorySkillsSheet(view.Surface, font, layout, PickSkillSide);
            var inspection = new AndroidHistoryInspection(view.Surface, font, layout);
            var more = new AndroidHistoryMoreDialog(
                view.Surface,
                font,
                layout,
                new AndroidHistoryMoreDialog.Actions(
                    () => _ = _coordinator.TryCheckServerHealthAsync(),
                    _coordinator.ToggleAccountLinkForm,
                    _uiRefresh.Guard<string?>(code =>
                        _ = _coordinator.TryRedeemBazaarDbAccountAsync(code)
                    ),
                    _uiRefresh.Guard(_coordinator.MarkAccountLinkedManually)
                )
            );
            note = new AndroidHistoryNoteDialog(
                view.Surface,
                font,
                layout,
                (battleId, text) =>
                    _uiRefresh.CanAct
                        ? _coordinator.TrySaveFavoriteNoteAsync(battleId, text)
                        : Task.FromResult(false)
            );
            _coordinator.PrewarmRecordingAvailability();
            _skills = skills;
            _inspection = inspection;
            _more = more;
            _note = note;
            _overlays = new AndroidHistoryOverlays(
                [note, more],
                [new NativeSkillsLayer(_nativeSkills), inspection, skills]
            );
        }
        catch
        {
            note?.Dispose();
            view.Dispose();
            throw;
        }
        return view;
    }

    // The overlays live under the view, so they go with it.
    private void DropView(AndroidHistoryPanelView view)
    {
        ReturnCards("relayout");
        _note?.Dispose();
        view.Dispose();
        _skills = null;
        _inspection = null;
        _more = null;
        _note = null;
        _overlays = null;
    }

    // The build threw: the slot holds no view, the old view's cards already returned. An open
    // panel's tick then requests the close, and the next open builds again.
    private static void LayoutFailed(
        Exception exception,
        string before,
        string key,
        bool visible
    ) =>
        BppLog.ErrorEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.layout_failed"),
            exception,
            ("from", before),
            ("to", key),
            ("visible", visible),
            ("rental_leases", AndroidCardRental.ActiveLeases)
        );

    // The new view reopens an open note on its draft; the other overlays close. False when no view
    // could be built: the tick then requests the close.
    private bool Relayout()
    {
        var from = _slot.ScreenKey;
        var draft = _note?.Draft;
        if (!EnsureView())
            return false;
        _relayouts++;
        var view = View!;
        view.SetVisible(true);
        RefreshUi();
        FlushUi();
        RefreshBoards();
        if (draft is { } kept)
            _note?.Open(kept.BattleId, kept.Text);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.relayout"),
            ("from", from),
            ("to", _slot.ScreenKey),
            ("note_draft_kept", draft != null && _note?.IsOpen == true),
            ("note_length", draft?.Text.Length ?? 0)
        );
        return true;
    }

    private HistoryBattleRecord? ActiveSelectedBattle =>
        HistoryPanelViewModelBuilder.ActiveSelectedBattle(_state, _coordinator);

    private void RefreshUi() => _uiRefresh.Request();

    private void FlushUi() => _uiRefresh.Flush(RenderUi);

    private void RenderUi()
    {
        if (View is not { } view || _disposed)
            return;
        var model = HistoryPanelViewModelBuilder.Build(
            _state,
            _coordinator,
            _runContext,
            _dependencies.AccountLinkGate,
            _dependencies.UnscaledTime(),
            Array.Empty<BPPSupporterSample>(),
            PlayerStatus,
            OpponentStatus
        );
        _model = model;
        view.Refresh(model);
        _more?.Refresh(model);
        _note?.Refresh(model);
        RefreshBoardMessages();
    }

    private void RefreshBoards()
    {
        if (!IsVisible || View is not { } view)
            return;
        var battle = ActiveSelectedBattle;
        var snapshots =
            _state.DetailBattleId == battle?.BattleId ? _coordinator.DetailSnapshots : null;
        var both = HistoryPanelViewModelBuilder.ShowsBothBoards(_state);
        if (
            snapshots != null
            && ReferenceEquals(snapshots, _shownSnapshots)
            && both == _shownBoth
            && battle?.BattleId == _shownBattleId
        )
        {
            RefreshBoardMessages();
            return;
        }
        if (battle?.BattleId != _shownBattleId)
        {
            _nativeSkills.Close("battle_changed");
            _skills?.Close("battle_changed");
            _inspection?.Close("battle_changed");
        }
        _shownSnapshots = snapshots;
        _shownBattleId = battle?.BattleId;
        _shownBoth = both;
        if (snapshots == null || battle == null)
        {
            _upper.ReturnAll();
            _lower.ReturnAll();
        }
        else
        {
            var has = (Func<Guid, bool>)_templates.Contains;
            var upper = both ? snapshots.OpponentHand : snapshots.PlayerHand;
            _upper.Render(HistoryBoardProjection.Items(upper, has, battle.BattleId + "|upper"));
            if (both)
                _lower.Render(
                    HistoryBoardProjection.Items(
                        snapshots.PlayerHand,
                        has,
                        battle.BattleId + "|lower"
                    )
                );
            else
                _lower.ReturnAll();
            _boardRenders++;
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.history.boards_rendered"),
                ("section", _state.SectionMode),
                ("both_boards", both),
                ("day", battle.Day),
                ("upper_leases", _upper.Leases),
                ("lower_leases", _lower.Leases),
                ("rental_leases", AndroidCardRental.ActiveLeases)
            );
        }
        view.SetSkillsAvailable(snapshots != null);
        RefreshBoardMessages();
    }

    // Every refresh re-derives both board messages from the boards' facts, as the desktop's
    // RefreshNativeHistoryMessages does.
    private void RefreshBoardMessages()
    {
        if (View is not { } view)
            return;
        var player = PlayerStatus;
        var opponent = OpponentStatus;
        _shownFacts = (player, opponent);
        var status = HistoryPanelViewModelBuilder.ArchiveStatus(
            _state,
            _coordinator,
            player,
            opponent
        );
        if (_shownBoth)
            view.SetBoardStatus(status.OpponentBoardMessage, status.PlayerBoardMessage);
        else
            view.SetBoardStatus(status.PlayerBoardMessage, string.Empty);
    }

    private void Inspect(BppItemBoardCard card, string source) => _inspection?.Open(card, source);

    // The picker stays open under the game's overlay, so closing the overlay returns to it.
    private void PickSkillSide(int side)
    {
        if (side < 0 || side >= _skillSides.Count)
            return;
        var skills = _skillSides[side]
            .Select(skill => new CardRentalSubject(skill.TemplateId, skill.Tier, null))
            .ToArray();
        _nativeSkills.Open(
            skills,
            !_shownBoth ? "challenger"
                : side == 0 ? "opponent"
                : "player"
        );
    }

    private void OpenMore()
    {
        if (_more == null)
            return;
        if (_model != null)
            _more.Refresh(_model);
        _more.Open();
    }

    // Favorites removes the favorite (confirmed by a second tap); Runs deletes the run, which the
    // coordinator refuses with the reason on the status line when the button reads unavailable.
    private void Delete()
    {
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.delete_tapped"),
            ("section", _state.SectionMode),
            ("enabled", _model?.DeleteButtonEnabled ?? false)
        );
        _coordinator.DeleteSelected();
    }

    // Favorites edits the note; the other sections favorite the selected battle.
    private void Favorite()
    {
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.favorite_tapped"),
            ("section", _state.SectionMode)
        );
        if (_coordinator.FavoriteSelected() is { } favorite)
            _note?.Open(favorite.BattleId, favorite.Note);
    }

    private void OpenSkills()
    {
        if (_skills == null || _shownSnapshots is not { } snapshots)
            return;
        var has = (Func<Guid, bool>)_templates.Contains;
        IReadOnlyList<(string, IReadOnlyList<BppItemBoardCard>)> rows = _shownBoth
            ?
            [
                (
                    HistoryPanelText.BoardOpponent(),
                    HistoryBoardProjection.Skills(snapshots.OpponentSkills, has)
                ),
                (
                    HistoryPanelText.BoardYou(),
                    HistoryBoardProjection.Skills(snapshots.PlayerSkills, has)
                ),
            ]
            :
            [
                (
                    HistoryPanelText.BoardChallenger(),
                    HistoryBoardProjection.Skills(snapshots.PlayerSkills, has)
                ),
            ];
        _skillSides = rows.Select(row => row.Item2).ToArray();
        _skills.Open(rows);
    }

    // The game's skill overlay, opened from the skills sheet, sits over it and closes with it. It
    // rents no card: its proxies are destroyed when it closes.
    private sealed class NativeSkillsLayer(AndroidNativeSkillOverlay overlay) : IHistoryCardOverlay
    {
        public bool IsOpen => overlay.IsOpen;

        public bool Close(string reason) => overlay.Close(reason);

        public void ReturnCards() { }
    }
}
