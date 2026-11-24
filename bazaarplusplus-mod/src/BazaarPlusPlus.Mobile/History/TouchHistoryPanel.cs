using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile.Ui;

namespace BazaarPlusPlus.Mobile.History;

// Entry points need the host's main-thread scope so coordinator continuations can touch Unity.
internal sealed class TouchHistoryPanel : IDisposable
{
    private const string ReplayStarted = "replay_started";

    private readonly HistoryPanelState _state = new();
    private readonly HistoryTouchNavigation _navigation = new();
    private readonly HistoryPanelCoordinator _coordinator;
    private readonly HistoryPanelDependencies _dependencies;
    private readonly IRunContext _runContext;
    private readonly IHistoryCardTemplates _templates;
    private readonly ISupporterRotation? _supporters;
    private readonly Func<string, OverlayRequestOutcome> _requestOpen;
    private readonly Func<string, OverlayRequestOutcome> _requestClose;
    private readonly TouchHistoryBoard _upper;
    private readonly TouchHistoryBoard _lower;
    private readonly TouchViewSlot<TouchHistoryPanelView> _slot;
    private TouchHistoryInspection? _inspection;
    private TouchHistorySkillsSheet? _skills;
    private readonly INativeSkillOverlay _nativeSkills = MobilePlatform.Port.NativeSkillOverlay(
        TouchHistoryPanelView.SortingOrder + 1
    );
    private IReadOnlyList<IReadOnlyList<BppItemBoardCard>> _skillSides = [];
    private TouchHistoryMoreDialog? _more;
    private TouchHistoryNoteDialog? _note;
    private TouchHistoryOverlays? _overlays;
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

    public TouchHistoryPanel(
        TouchHistoryData data,
        Func<string, OverlayRequestOutcome> requestOpen,
        Func<string, OverlayRequestOutcome> requestClose
    )
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        _requestOpen = requestOpen ?? throw new ArgumentNullException(nameof(requestOpen));
        _requestClose = requestClose ?? throw new ArgumentNullException(nameof(requestClose));
        _dependencies = data.Dependencies;
        _templates = data.Templates;
        _supporters = data.Supporters;
        _runContext = _dependencies.RunContext;
        _slot = new TouchViewSlot<TouchHistoryPanelView>(DropView);
        _upper = new TouchHistoryBoard("history.upper", card => Inspect(card, "upper"));
        _lower = new TouchHistoryBoard("history.lower", card => Inspect(card, "lower"));
        _coordinator = new HistoryPanelCoordinator(
            _state,
            _dependencies,
            RefreshUi,
            RefreshBoards,
            new HistoryPanelReplayReturnPort(
                HideForReplay,
                ReturnFromReplay,
                data.SavedReplays,
                data.LobbyActive
            )
        );
    }

    public bool IsVisible { get; private set; }

    public bool HasModalBackStep => _overlays?.AnyOpen == true || View?.HasDetailBackStep == true;

    public long OpenSessions => IsVisible ? 1 : 0;

    public long Opened => Interlocked.Read(ref _opened);

    public int ViewRoots => View?.RootCount ?? 0;

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

    public void DismissModalBackStep()
    {
        if (_overlays?.AnyOpen == true)
            _overlays.DismissTop("back_key");
        else
            View?.BackToArchive();
    }

    public void RefreshLocalization()
    {
        if (IsVisible)
            RefreshUi();
    }

    public void Tick(float deltaTime, bool isOpen)
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
            if (!_slot.IsCurrent(TouchScreen.Key()) && !Relayout())
            {
                _requestClose("relayout_failed");
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
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.tick_failed"),
                exception
            );
        }
    }

    private void HideForReplay() => _requestClose(ReplayStarted);

    // Defer replay return during combat so it can be retried on a later frame.
    private void ReturnFromReplay() => _requestOpen("replay_returned");

    // Return cards before the scene transition invalidates their leases. Dialog drafts can stay.
    public void OnSceneChanged()
    {
        var dialogsKept = _overlays?.SceneChanged("scene_changed") ?? 0;
        var returned = ReturnBoards();
        _boardsDirty = IsVisible;
        if (!IsVisible)
            return;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.scene_changed"),
            ("returned", returned),
            ("dialogs_kept", dialogsKept),
            ("note_draft_kept", _note?.IsOpen == true),
            ("note_length", _note?.Draft?.Text.Length ?? 0)
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        using var scope = MobilePlatform.Port.EnterMainThread();
        if (IsVisible)
            _coordinator.OnPanelHidden();
        IsVisible = false;
        _coordinator.Dispose();
        ReturnCards("disposed");
        _slot.Drop();
    }

    private TouchHistoryPanelView? View => _slot.View;

    private HistoryBoardFacts PlayerStatus => _shownBoth ? _lower.Facts() : _upper.Facts();

    private HistoryBoardFacts OpponentStatus => _shownBoth ? _upper.Facts() : default;

    public bool Open(string reason)
    {
        if (_disposed)
            return false;
        if (IsVisible)
            return true;
        if (!EnsureView())
            return false;
        // Ghost replays count their snapshot templates on a pool thread; the templates they read
        // are readied here, on the main thread, before any download can start.
        _templates.EnsureBuilt();
        IsVisible = true;
        Interlocked.Increment(ref _opened);
        var resumeSelection = _coordinator.OnPanelShown();
        View!.SetVisible(true);
        RefreshUi();
        FlushUi();
        RefreshBoards();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.opened"),
            ("reason", reason),
            ("resume_selection", resumeSelection),
            ("section", _state.SectionMode),
            ("record_visible", _model?.RecordAndReplayButtonVisible ?? false),
            ("screen", _slot.ScreenKey)
        );
        return true;
    }

    // Preserve the pending return only for a replay start, including while the panel is already hidden.
    public void Hide(string reason)
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
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.closed"),
            ("reason", reason),
            ("return_after_replay", _coordinator.ReplayReturnPending),
            ("returned", returned),
            ("rental_leases", MobilePlatform.Port.CardRental.ActiveLeases)
        );
    }

    private void CancelReturn(string reason)
    {
        if (!_coordinator.CancelReplayReturn())
            return;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.return_cancelled"),
            ("reason", reason)
        );
    }

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

    private bool EnsureView()
    {
        var key = TouchScreen.Key();
        if (_slot.IsCurrent(key) && !TouchHistoryUi.Skin.NeedsRefresh)
            return true;
        var font = MobilePlatform.Port.GameFont();
        if (font == null)
        {
            _slot.Drop();
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.open_skipped"),
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

    private TouchHistoryPanelView BuildView(TMP_FontAsset font)
    {
        var view = new TouchHistoryPanelView(
            font,
            new TouchHistoryPanelView.ViewActions(
                () => _requestClose("close_button"),
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
                _uiRefresh.Guard(OpenSkills),
                _uiRefresh.Guard<string>(_coordinator.SetRunHeroFilter),
                _uiRefresh.Guard<GhostBattleFilter>(_coordinator.SetGhostBattleFilter),
                _uiRefresh.Guard(_coordinator.ToggleGhostDayMin10)
            ),
            _supporters,
            _navigation
        );
        TouchHistoryNoteDialog? note = null;
        try
        {
            var layout = view.Layout;
            _upper.Attach(view.UpperBoard, layout);
            _lower.Attach(view.LowerBoard, layout);
            // Later siblings draw over the sheet and its rented cards.
            var skills = new TouchHistorySkillsSheet(view.Surface, font, layout, PickSkillSide);
            var inspection = new TouchHistoryInspection(view.Surface, font, layout);
            var more = new TouchHistoryMoreDialog(
                view.Surface,
                font,
                layout,
                new TouchHistoryMoreDialog.Actions(
                    () => _ = _coordinator.TryCheckServerHealthAsync(),
                    _coordinator.ToggleAccountLinkForm,
                    _uiRefresh.Guard<string?>(code =>
                        _ = _coordinator.TryRedeemBazaarDbAccountAsync(code)
                    ),
                    _uiRefresh.Guard(_coordinator.MarkAccountLinkedManually),
                    _coordinator.ToggleBppAccountForm,
                    _coordinator.OpenBppAccountActivateDevice,
                    _uiRefresh.Guard<string?>(input =>
                        _ = _coordinator.TrySubmitBppAccountInputAsync(input)
                    )
                )
            );
            note = new TouchHistoryNoteDialog(
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
            _overlays = new TouchHistoryOverlays(
                [note, more, view.FilterOverlay],
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

    private void DropView(TouchHistoryPanelView view)
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

    private static void LayoutFailed(
        Exception exception,
        string before,
        string key,
        bool visible
    ) =>
        BppLog.ErrorEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.layout_failed"),
            exception,
            ("from", before),
            ("to", key),
            ("visible", visible),
            ("rental_leases", MobilePlatform.Port.CardRental.ActiveLeases)
        );

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
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.relayout"),
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
            OpponentStatus,
            TouchHistoryPanelView.HeroLabel,
            DateTimeOffset.Now
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
            var socketEffect = (Func<Guid, CardTier, string?>)_templates.SocketEffectAttribute;
            var upper = both ? snapshots.OpponentHand : snapshots.PlayerHand;
            _upper.Render(
                HistoryBoardProjection.Items(upper, has, socketEffect, battle.BattleId + "|upper")
            );
            if (both)
                _lower.Render(
                    HistoryBoardProjection.Items(
                        snapshots.PlayerHand,
                        has,
                        socketEffect,
                        battle.BattleId + "|lower"
                    )
                );
            else
                _lower.ReturnAll();
            _boardRenders++;
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.boards_rendered"),
                ("section", _state.SectionMode),
                ("both_boards", both),
                ("day", battle.Day),
                ("upper_leases", _upper.Leases),
                ("lower_leases", _lower.Leases),
                ("rental_leases", MobilePlatform.Port.CardRental.ActiveLeases)
            );
        }
        view.SetSkillsAvailable(snapshots != null);
        RefreshBoardMessages();
    }

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

    // Keep the picker open so the native overlay returns to it.
    private void PickSkillSide(int side)
    {
        if (side < 0 || side >= _skillSides.Count)
            return;
        var skills = _skillSides[side].Select(CardRentalSubject.Of).ToArray();
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

    private void Delete()
    {
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.delete_tapped"),
            ("section", _state.SectionMode),
            ("enabled", _model?.DeleteButtonEnabled ?? false)
        );
        _coordinator.DeleteSelected();
    }

    private void Favorite()
    {
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.favorite_tapped"),
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

    private sealed class NativeSkillsLayer(INativeSkillOverlay overlay) : IHistoryCardOverlay
    {
        public bool IsOpen => overlay.IsOpen;

        public bool Close(string reason) => overlay.Close(reason);

        public void ReturnCards() { }
    }
}
