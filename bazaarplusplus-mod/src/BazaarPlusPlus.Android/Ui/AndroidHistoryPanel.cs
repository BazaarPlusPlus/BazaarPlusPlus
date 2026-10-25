using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Android.Settings;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTMPro;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The Android twin of the desktop's <c>HistoryPanel</c>: it owns History's state and Core's
/// <see cref="HistoryPanelCoordinator"/> for the host's lifetime and renders Core's view model
/// through <see cref="AndroidHistoryPanelView"/>. The selected battle's boards are the game's own
/// cards, rented into the view's two board regions (<see cref="AndroidHistoryBoard"/>, the
/// counterpart of the desktop's <c>HistoryPanel.NativeBoards</c>); a tap inspects a card, and the
/// skills sheet shows both players' skills. Favorites, delete (refused for a run Seal eligibility
/// protects), the filters, the More dialog with the account link, and the note editor act through
/// the coordinator as the desktop's do. It is a Main Overlay Panel: its lifecycle is the Overlay
/// Panel Host's (<see cref="AndroidOverlayPanels"/>), which opens it from the touch menu and the
/// settings row, and closes it for the back key (an open dialog first), combat, a scene change
/// into combat (<see cref="SceneChangeClosePolicy.OnlyWhenInCombat"/>), another panel opening, and
/// <c>[Android] HistoryPanel</c> switching off (from the settings row or the More dialog's own
/// switch); the close button and a replay start request a close. Every other scene change returns
/// its cards, since the rental destroys them, and renders them again on the next frame. A new
/// screen size lays the view out again and keeps an open note's draft. A replay started from it
/// reopens it with the selection kept once the replay session is over and the lobby is the active
/// scene, unless the panel was closed or switched off meanwhile. The Record action is not offered:
/// the host records no video. Every entry point runs inside the main-thread pump's scope, so the
/// coordinator's continuations resume on the main thread.
/// </summary>
internal sealed class AndroidHistoryPanel : IDisposable
{
    public const string PanelId = "history";
    private const string ReplayStarted = "replay_started";
    private const string SwitchedOff = "switched_off";

    private readonly HistoryPanelState _state = new();
    private readonly HistoryPanelCoordinator _coordinator;
    private readonly HistoryPanelDependencies _dependencies;
    private readonly IRunContext _runContext;
    private readonly Func<ISavedReplayRuntime?> _runtime;
    private readonly AndroidCardTemplateIndex _templates;
    private readonly Func<TMP_FontAsset?> _font;
    private readonly ISetting<bool> _enabled;
    private readonly AndroidHistoryBoard _upper;
    private readonly AndroidHistoryBoard _lower;
    private readonly AndroidPanelHandle _handle;
    private AndroidHistoryPanelView? _view;
    private AndroidHistoryInspection? _inspection;
    private AndroidHistorySkillsSheet? _skills;
    private AndroidHistoryMoreDialog? _more;
    private AndroidHistoryNoteDialog? _note;
    private HistoryPanelViewModel? _model;
    private PvpBattleSnapshots? _shownSnapshots;
    private string? _shownBattleId;
    private bool _shownBoth;
    private (HistoryBoardFacts Player, HistoryBoardFacts Opponent) _shownFacts;
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
        ISetting<bool> enabled,
        AndroidOverlayPanels panels
    )
    {
        ArgumentNullException.ThrowIfNull(panels);
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _font = font ?? throw new ArgumentNullException(nameof(font));
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _runContext = dependencies.RunContext;
        _upper = new AndroidHistoryBoard("history.upper", card => Inspect(card, false, "upper"));
        _lower = new AndroidHistoryBoard("history.lower", card => Inspect(card, false, "lower"));
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
            Enabled = () => _enabled.Value,
            IsModalInputActive = () =>
                _note?.IsOpen == true
                || _more?.IsOpen == true
                || _inspection?.IsOpen == true
                || _skills?.IsOpen == true,
            DismissModal = DismissTopDialog,
            OnSceneChanged = OnSceneChanged,
            // A pending return from a replay is dropped, the panel open or not.
            OnSwitchedOff = () => CancelReturn(SwitchedOff),
        });
    }

    public bool IsVisible { get; private set; }

    /// <summary>Panel sessions opened and not yet closed: 0 or 1, for the hooks summary.</summary>
    public long OpenSessions => IsVisible ? 1 : 0;

    public long Opened => Interlocked.Read(ref _opened);

    public int ViewRoots => _view?.RootCount ?? 0;

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
                    case OverlayRequestOutcome.Disabled:
                        CancelReturn(SwitchedOff);
                        LogOpenSkipped(SwitchedOff);
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
            if (_view != null && _view.ScreenKey != AndroidTouchScreen.Key() && !Relayout())
            {
                _handle.RequestClose("relayout_failed");
                return;
            }
            _coordinator.Tick(_dependencies.UnscaledTime());
            if (_boardsDirty)
            {
                _boardsDirty = false;
                RefreshBoards();
            }
            var facts = (PlayerStatus, OpponentStatus);
            if (facts != _shownFacts)
                RefreshBoardMessages();
            _view?.Tick();
            _note?.Tick();
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("history_tick", exception);
        }
    }

    // The coordinator hides the panel for a replay it started and keeps the return pending.
    private void HideForReplay() => _handle.RequestClose(ReplayStarted);

    // The coordinator's return from a replay. A combat keeps the return pending, so it is tried
    // again next frame; a switched-off panel drops it.
    private void ReturnFromReplay()
    {
        if (_handle.RequestOpen("replay_returned") != OverlayRequestOutcome.Disabled)
            return;
        CancelReturn(SwitchedOff);
        LogOpenSkipped(SwitchedOff);
    }

    // A scene loaded and the panel is still open, or was closed: the rental destroys every card
    // right after this, so the cards go back first and come again next frame.
    private void OnSceneChanged()
    {
        ReturnCards("scene_changed");
        _boardsDirty = IsVisible;
    }

    // The back key while a dialog is up: the note, then More, then the inspection, then skills.
    private void DismissTopDialog()
    {
        _ =
            _note?.Close("back_key") == true
            || _more?.Close("back_key") == true
            || _inspection?.Close("back_key") == true
            || _skills?.Close("back_key") == true;
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
        _note?.Dispose();
        _view?.Dispose();
        _view = null;
    }

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
        _view!.SetVisible(true);
        RefreshUi();
        RefreshBoards();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.opened"),
            ("reason", reason),
            ("resume_selection", resumeSelection),
            ("section", _state.SectionMode),
            ("template_index", _templates.Count),
            ("record_visible", _model?.RecordAndReplayButtonVisible ?? false),
            ("screen", _view.ScreenKey)
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
        _view?.SetVisible(false);
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

    // The More dialog's switch writes [Android] HistoryPanel as the settings row does; the host
    // sees the switch-off on its next frame, closes the panel and releases the rented cards.
    private void TurnOff()
    {
        _enabled.Value = false;
        AndroidSettingsDock.LogChanged("Android", "HistoryPanel", false, source: "history_more");
    }

    // Every overlay closes and every card goes back to the rental.
    private int ReturnCards(string reason)
    {
        _note?.Close(reason);
        _more?.Close(reason);
        _inspection?.Close(reason);
        _skills?.Close(reason);
        _inspection?.Return();
        _skills?.ReturnAll();
        var returned = _upper.ReturnAll() + _lower.ReturnAll();
        _shownSnapshots = null;
        _shownBattleId = null;
        return returned;
    }

    private bool EnsureView()
    {
        if (_view != null && _view.ScreenKey == AndroidTouchScreen.Key())
            return true;
        if (_view != null)
            DropView();
        var font = _font();
        if (font == null)
        {
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.history.open_skipped"),
                ("reason_code", "font_unavailable")
            );
            return false;
        }
        var view = new AndroidHistoryPanelView(
            font,
            new AndroidHistoryPanelView.ViewActions(
                () => _handle.RequestClose("close_button"),
                () => _ = _coordinator.TryReplaySelectedBattleAsync(ActiveSelectedBattle, false),
                _coordinator.SelectRun,
                _coordinator.SelectBattle,
                _coordinator.SetSectionMode,
                _coordinator.PageArchive,
                _coordinator.PageBattles,
                hero =>
                {
                    LogFilter("hero", hero);
                    _coordinator.SetRunHeroFilter(hero);
                },
                filter =>
                {
                    LogFilter("ghost_outcome", filter.ToString());
                    _coordinator.SetGhostBattleFilter(filter);
                },
                () =>
                {
                    LogFilter("ghost_day_min_10", "toggle");
                    _coordinator.ToggleGhostDayMin10();
                },
                OpenMore,
                Delete,
                Favorite,
                OpenSkills
            )
        );
        _view = view;
        var layout = view.Layout;
        _upper.Attach(view.UpperBoard, layout);
        _lower.Attach(view.LowerBoard, layout);
        // Later siblings draw over the sheet and its rented cards.
        _skills = new AndroidHistorySkillsSheet(
            view.Surface,
            font,
            layout,
            card => Inspect(card, true, "skills")
        );
        _inspection = new AndroidHistoryInspection(view.Surface, font, layout);
        _more = new AndroidHistoryMoreDialog(
            view.Surface,
            font,
            layout,
            new AndroidHistoryMoreDialog.Actions(
                () => _ = _coordinator.TryCheckServerHealthAsync(),
                _coordinator.ToggleAccountLinkForm,
                code => _ = _coordinator.TryRedeemBazaarDbAccountAsync(code),
                _coordinator.MarkAccountLinkedManually,
                TurnOff
            )
        );
        _note = new AndroidHistoryNoteDialog(
            view.Surface,
            font,
            layout,
            _coordinator.TrySaveFavoriteNoteAsync
        );
        _coordinator.PrewarmRecordingAvailability();
        return true;
    }

    // The overlays live under the view, so they go with it.
    private void DropView()
    {
        ReturnCards("relayout");
        _note?.Dispose();
        _view?.Dispose();
        _view = null;
        _skills = null;
        _inspection = null;
        _more = null;
        _note = null;
    }

    // The new view reopens an open note on its draft; the other overlays close. False when no view
    // could be built: the tick then requests the close.
    private bool Relayout()
    {
        var from = _view?.ScreenKey;
        var draft = _note?.Draft;
        DropView();
        if (!EnsureView())
            return false;
        _relayouts++;
        _view!.SetVisible(true);
        RefreshUi();
        RefreshBoards();
        if (draft is { } kept)
            _note?.Open(kept.BattleId, kept.Text);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.relayout"),
            ("from", from),
            ("to", _view.ScreenKey),
            ("note_draft_kept", draft != null && _note?.IsOpen == true),
            ("note_length", draft?.Text.Length ?? 0),
            ("rental_leases", AndroidCardRental.ActiveLeases)
        );
        return true;
    }

    private HistoryBattleRecord? ActiveSelectedBattle =>
        HistoryPanelViewModelBuilder.ActiveSelectedBattle(_state, _coordinator);

    private void RefreshUi()
    {
        if (_view == null || _disposed)
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
        _view.Refresh(model);
        _more?.Refresh(model);
        _note?.Refresh(model);
        RefreshBoardMessages();
    }

    private void RefreshBoards()
    {
        if (!IsVisible || _view == null)
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
        _view.SetSkillsAvailable(snapshots != null);
        RefreshBoardMessages();
    }

    // Every refresh re-derives both board messages from the boards' facts, as the desktop's
    // RefreshNativeHistoryMessages does.
    private void RefreshBoardMessages()
    {
        if (_view == null)
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
            _view.SetBoardStatus(status.OpponentBoardMessage, status.PlayerBoardMessage);
        else
            _view.SetBoardStatus(status.PlayerBoardMessage, string.Empty);
    }

    private void Inspect(BppItemBoardCard card, bool skill, string source) =>
        _inspection?.Open(card, skill, source);

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

    private static void LogFilter(string filter, string value) =>
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.filter_tapped"),
            ("filter", filter),
            ("value", value)
        );

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
        _skills.Open(rows);
    }
}
