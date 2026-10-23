using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTMPro;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The Android twin of the desktop's <c>HistoryPanel</c>: it owns History's state and Core's
/// <see cref="HistoryPanelCoordinator"/> for the host's lifetime and renders Core's view model
/// through <see cref="AndroidHistoryPanelView"/>. The touch launcher opens it; the close button,
/// a scene change into combat (the desktop's <c>SceneChangeClosePolicy.OnlyWhenInCombat</c>), and
/// a replay start close it. A replay started from it reopens it with the selection kept once
/// the replay session is over and the lobby is the active scene, polled every frame as the
/// desktop's tick does. Every entry point runs inside the main-thread pump's scope, so the
/// coordinator's continuations resume on the main thread.
/// </summary>
internal sealed class AndroidHistoryPanel : IDisposable
{
    private readonly HistoryPanelState _state = new();
    private readonly HistoryPanelCoordinator _coordinator;
    private readonly HistoryPanelDependencies _dependencies;
    private readonly IRunContext _runContext;
    private readonly Func<ISavedReplayRuntime?> _runtime;
    private readonly AndroidCardTemplateIndex _templates;
    private readonly Func<TMP_FontAsset?> _font;
    private readonly IHistoryBoardSurface _boards;
    private AndroidHistoryPanelView? _view;
    private bool _returnAfterReplay;
    private bool _disposed;
    private long _opened;

    public AndroidHistoryPanel(
        HistoryPanelDependencies dependencies,
        Func<ISavedReplayRuntime?> runtime,
        AndroidCardTemplateIndex templates,
        Func<TMP_FontAsset?> font,
        IHistoryBoardSurface? boards = null
    )
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _font = font ?? throw new ArgumentNullException(nameof(font));
        _boards = boards ?? new NoHistoryBoards();
        _runContext = dependencies.RunContext;
        _coordinator = new HistoryPanelCoordinator(
            _state,
            dependencies,
            RefreshUi,
            RefreshBoards,
            OnReplayVisibilityChange
        );
    }

    public bool IsVisible { get; private set; }

    /// <summary>Panel sessions opened and not yet closed: 0 or 1, for the hooks summary.</summary>
    public long OpenSessions => IsVisible ? 1 : 0;

    public long Opened => Interlocked.Read(ref _opened);

    public int ViewRoots => _view?.RootCount ?? 0;

    /// <summary>Main thread: opens the panel unless a combat is running or no font is ready.</summary>
    public void Open(string reason) =>
        AndroidUiActions.Run("history_open", () => OpenInternal(reason, resumeSelection: false));

    public void Close(string reason) => AndroidUiActions.Run("history_close", () => Hide(reason));

    /// <summary>Main thread, every frame.</summary>
    public void Tick()
    {
        if (_disposed)
            return;
        using var scope = AndroidMainThread.Host.Enter();
        try
        {
            if (
                _returnAfterReplay
                && !IsVisible
                && _runtime() is { HasSavedReplaySession: false }
                && AndroidLobbyScene.IsActive()
            )
                OpenInternal("replay_returned", resumeSelection: true);

            if (!IsVisible)
                return;
            _coordinator.Tick(_dependencies.UnscaledTime());
            _view?.Tick();
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("history_tick", exception);
        }
    }

    /// <summary>A scene loaded: the panel stays open across scenes, except into a combat.</summary>
    public void OnSceneLoaded(string sceneName)
    {
        if (!IsVisible || !AndroidCombatProbe.IsInCombat())
            return;
        Close($"scene_combat:{sceneName}");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        using var scope = AndroidMainThread.Host.Enter();
        if (IsVisible)
            _coordinator.OnPanelHidden();
        IsVisible = false;
        _coordinator.Dispose();
        _boards.Dispose();
        _view?.Dispose();
        _view = null;
    }

    private void OpenInternal(string reason, bool resumeSelection)
    {
        if (_disposed || IsVisible)
            return;
        if (AndroidCombatProbe.IsInCombat())
        {
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.history.open_skipped"),
                ("reason_code", "combat_active")
            );
            return;
        }
        if (!EnsureView())
            return;
        // Ghost replays count their snapshot templates on a pool thread; the index they read is
        // built here, on the main thread, before any download can start.
        _templates.EnsureBuilt();
        resumeSelection |= _returnAfterReplay;
        _returnAfterReplay = false;
        IsVisible = true;
        Interlocked.Increment(ref _opened);
        _coordinator.OnPanelShown(resumeSelection);
        _view!.SetVisible(true);
        RefreshUi();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.opened"),
            ("reason", reason),
            ("resume_selection", resumeSelection),
            ("section", _state.SectionMode),
            ("template_index", _templates.Count)
        );
    }

    private void Hide(string reason)
    {
        if (!IsVisible)
            return;
        IsVisible = false;
        _coordinator.OnPanelHidden();
        _boards.Hide();
        _view?.SetVisible(false);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.closed"),
            ("reason", reason),
            ("return_after_replay", _returnAfterReplay)
        );
    }

    private bool EnsureView()
    {
        if (_view != null)
            return true;
        var font = _font();
        if (font == null)
        {
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.history.open_skipped"),
                ("reason_code", "font_unavailable")
            );
            return false;
        }
        _view = new AndroidHistoryPanelView(
            font,
            new AndroidHistoryPanelView.ViewActions(
                () => Hide("close_button"),
                () => _ = _coordinator.TryReplaySelectedBattleAsync(ActiveSelectedBattle, false),
                _coordinator.SelectRun,
                _coordinator.SelectBattle,
                _coordinator.SetSectionMode,
                _coordinator.PageArchive,
                _coordinator.PageBattles
            )
        );
        _view.PlayerBoundsChanged += _boards.SetPlayerBounds;
        _view.OpponentBoundsChanged += _boards.SetOpponentBounds;
        _view.EnsureCreated();
        _coordinator.PrewarmRecordingAvailability();
        return true;
    }

    private Game.HistoryPanel.Data.HistoryBattleRecord? ActiveSelectedBattle =>
        HistoryPanelViewModelBuilder.ActiveSelectedBattle(_state, _coordinator);

    // A replay start hides the panel and remembers to come back once the replay is over.
    private void OnReplayVisibilityChange(bool visible)
    {
        _returnAfterReplay = !visible;
        if (visible)
            OpenInternal("replay_visibility", resumeSelection: false);
        else
            Hide("replay_started");
    }

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
            _boards.PlayerStatus,
            _boards.OpponentStatus
        );
        _view.Refresh(model);
        RefreshBoardMessages();
    }

    private void RefreshBoards()
    {
        if (!IsVisible)
            return;
        var battle = ActiveSelectedBattle;
        var snapshots =
            _state.DetailBattleId == battle?.BattleId ? _coordinator.DetailSnapshots : null;
        _boards.Show(
            battle?.BattleId,
            snapshots,
            HistoryPanelViewModelBuilder.ShowsBothBoards(_state)
        );
        RefreshBoardMessages();
    }

    // Every refresh re-derives both board messages from the stored board status, as the
    // desktop's RefreshNativeHistoryMessages does.
    private void RefreshBoardMessages()
    {
        if (_view == null)
            return;
        var status = HistoryPanelViewModelBuilder.ArchiveStatus(
            _state,
            _coordinator,
            _boards.PlayerStatus,
            _boards.OpponentStatus
        );
        _view.SetBoardStatus(status.PlayerBoardMessage, status.OpponentBoardMessage);
    }
}
