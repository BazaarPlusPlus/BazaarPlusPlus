#nullable enable
using System.Collections;
using System.Reflection;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.AccountLink;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Favorites;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

using var uiContext = new TestUiContext();
SynchronizationContext.SetSynchronizationContext(uiContext);

var coreAssembly = Assembly.Load("BazaarPlusPlus.Core");
var storageAssembly = Assembly.Load("BazaarPlusPlus.Storage");
var stateType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelState");
var runRecordType = RequireType("BazaarPlusPlus.Game.HistoryPanel.Data.HistoryRunRecord");
var battleRecordType = RequireType("BazaarPlusPlus.Game.HistoryPanel.Data.HistoryBattleRecord");
var snapshotCountsType = RequireType(
    "BazaarPlusPlus.Game.HistoryPanel.Data.HistoryBattleSnapshotCounts"
);
var battleSourceType = RequireType("BazaarPlusPlus.Game.HistoryPanel.Data.HistoryBattleSource");
var ghostFilterType = RequireType("BazaarPlusPlus.Game.HistoryPanel.GhostBattleFilter");
var heroPresentationType = RequireType(
    "BazaarPlusPlus.Game.HistoryPanel.HistoryPanelHeroPresentation"
);
var coordinatorType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelCoordinator");
var dependenciesType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelDependencies");
var dataServiceType = RequireType(
    "BazaarPlusPlus.Game.HistoryPanel.Storage.HistoryPanelDataService"
);
var cursorType = RequireType("BazaarPlusPlus.Game.HistoryPanel.Storage.HistoryCursor");

TestUiRefreshConsumesLatestStateOnce();
TestRunHeroRosterAddsOneCanonicalTheDragonsAfterTheExistingSeven();
TestStateSelectedRunUsesFilteredRunList();
TestPageReplacementUpdatesSelection();
TestCoordinatorRunSelectionUsesFilteredSpace();
TestAcceptedReplayReturnsWithSelection();
TestDeleteAndFavoriteDispatchBySection();
TestOffThreadDeleteAndRemovalPublishInTheirSection();
TestDeleteLeaseFailurePaths();
TestDeletePromptMentionsMp4OnlyWhereTheHostRecords();
TestGhostListTextMarksOnlyTheFinalBattle();
TestRunRatingShowsItsChangeOnlyWhenKnown();
TestAccountArrivalSyncsOnlyTheOpenGhostSection();
TestAccountLinkActionHandsAccountChangeToTheObserver();
TestFailedSyncMarksTheEmptyGhostListIncomplete();
TestSessionBoundariesRestartACancelledSync();
TestAccountChangeMidSyncSyncsTheNewAccount();

Console.WriteLine("HistoryPanelFiltering checks passed.");

void TestUiRefreshConsumesLatestStateOnce()
{
    var refresh = new HistoryPanelUiRefresh();
    var state = new HistoryPanelState();
    var models = new List<HistoryPanelViewModel>();
    void Render() =>
        models.Add(
            HistoryPanelViewModelBuilder.Build(state, null, null, null, 0f, [], default, default)
        );
    var selected = 0;
    var select = refresh.Guard<int>(index => selected = index);
    var deletes = 0;
    var delete = refresh.Guard(() => deletes++);
    refresh.Flush(Render);
    Assert(models.Count == 0, "A clean frame must not build a model.");
    state.SectionMode = HistorySectionMode.Ghost;
    refresh.Request();
    select(1);
    delete();
    Assert(
        selected == 0 && deletes == 0,
        "Stale row and delete inputs must not act in a new section."
    );
    state.StatusMessage = "latest error";
    state.StatusSeverity = StatusSeverity.Failure;
    refresh.Request();
    refresh.Request();
    refresh.Flush(Render);
    Assert(
        models.Count == 1 && models[0].SectionMode == HistorySectionMode.Ghost,
        "One frame must build one model from the latest section."
    );
    Assert(
        models[0].StatusMessage == "latest error",
        "Coalescing must preserve the final failure."
    );
    select(2);
    delete();
    Assert(selected == 2 && deletes == 1, "Published controls must act again.");
    refresh.Request();
    refresh.Flush(() =>
    {
        select(3);
        Assert(selected == 2, "A partially rendered model must not accept reentrant input.");
        Render();
        refresh.Request();
    });
    Assert(models.Count == 2 && !refresh.CanAct, "A reentrant request belongs to the next frame.");
    refresh.Flush(Render);
    Assert(models.Count == 3 && refresh.CanAct, "The next frame consumes the reentrant request.");
    refresh.Request();
    try
    {
        refresh.Flush(() => throw new IOException("render failed"));
        throw new InvalidOperationException("Rendering must propagate failures.");
    }
    catch (IOException) { }
    Assert(!refresh.CanAct, "A failed render must remain dirty for retry.");
    // Hidden hosts do not flush. A reopen requests and immediately renders the latest state.
    state.StatusMessage = "completed while hidden";
    refresh.Request();
    refresh.Flush(Render);
    refresh.Flush(Render);
    Assert(
        models.Count == 4 && models[^1].StatusMessage == "completed while hidden" && refresh.CanAct,
        "Reopening must show the latest state once, including updates while hidden."
    );
}

void TestRunHeroRosterAddsOneCanonicalTheDragonsAfterTheExistingSeven()
{
    var roster = (IEnumerable)
        heroPresentationType
            .GetProperty(
                "RunFilterHeroIds",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            )!
            .GetValue(null)!;
    var heroIds = roster.Cast<string>().ToArray();

    Assert(
        heroIds.SequenceEqual([
            "Vanessa",
            "Pygmalien",
            "Dooley",
            "Mak",
            "Jules",
            "Karnok",
            "Stelle",
            "TheDragons",
        ]),
        "History should preserve the existing seven hero order and append one canonical The Dragons chip."
    );
    Assert(
        heroIds.Count(hero => hero == "TheDragons") == 1,
        "History should expose exactly one canonical The Dragons chip."
    );
    Assert(
        !heroIds.Contains("Hero8"),
        "History should never expose a separate legacy The Dragons chip."
    );
}

void TestStateSelectedRunUsesFilteredRunList()
{
    var state =
        Activator.CreateInstance(stateType)
        ?? throw new InvalidOperationException("HistoryPanelState should construct.");
    stateType.GetProperty("SelectedRunIndex")!.SetValue(state, 1);

    var runs = CreateRunList(CreateRun("raw-1", "Vanessa"), CreateRun("raw-3", "Vanessa"));
    var selected = Invoke(stateType, state, "GetSelectedRun", runs);

    Assert(GetString(selected, "RunId") == "raw-3", "Selected run should come from filtered runs.");
}

void TestPageReplacementUpdatesSelection()
{
    foreach (var pageName in new[] { "RunPage", "BattlePage", "GhostPage" })
    {
        var state = Activator.CreateInstance(stateType)!;
        var isRun = pageName == "RunPage";
        var rowsName =
            isRun ? "Runs"
            : pageName == "BattlePage" ? "Battles"
            : "GhostBattles";
        var selectedMethod =
            isRun ? "GetSelectedRun"
            : pageName == "BattlePage" ? "GetSelectedBattle"
            : "GetSelectedGhostBattle";
        object? Selected() =>
            rowsName == "Battles"
                ? Invoke(stateType, state, selectedMethod)
                : Invoke(stateType, state, selectedMethod, GetList(state, rowsName));
        var first = isRun ? CreateRun("first", "Vanessa") : CreateBattle("first", 10, "Won");
        var next = isRun ? CreateRun("next", "Mak") : CreateBattle("next", 12, "Lost");
        SetPage(state, pageName, first);
        Assert(ReferenceEquals(Selected(), first), "Selection must read the published page.");
        SetPage(state, pageName, next);
        Assert(
            GetList(state, rowsName).Count == 1 && ReferenceEquals(Selected(), next),
            "Replacing a page must replace its visible rows and selection together."
        );
        SetPage(state, pageName);
        Assert(
            GetList(state, rowsName).Count == 0 && Selected() == null,
            "Clearing a page must not retain a row selected from the previous page."
        );
    }
}

void TestCoordinatorRunSelectionUsesFilteredSpace()
{
    var state =
        Activator.CreateInstance(stateType)
        ?? throw new InvalidOperationException("HistoryPanelState should construct.");
    SetPage(state, "RunPage", CreateRun("raw-1", "Vanessa"), CreateRun("raw-3", "Vanessa"));
    stateType.GetProperty("SelectedRunHero")!.SetValue(state, "Vanessa");

    var dataService = Construct(dataServiceType, null, null);
    var dependencies = Dependencies(dataService);
    var coordinator =
        Activator.CreateInstance(
            coordinatorType,
            state,
            dependencies,
            (Action)(() => { }),
            (Action)(() => { }),
            NoReplayReturn()
        ) ?? throw new InvalidOperationException("HistoryPanelCoordinator should construct.");

    Invoke(coordinatorType, coordinator, "SelectRun", 1);
    var selected = Invoke(coordinatorType, coordinator, "GetSelectedRun");

    Assert(
        GetString(selected, "RunId") == "raw-3",
        "SelectRun should interpret indexes in the current filtered database page."
    );
}

// An accepted replay arms the return before it hides the panel; the coordinator brings the panel
// back once no saved replay session remains and the lobby is active (a null runtime keeps it
// waiting), resuming the selection and filters. A show that does not open keeps the return for
// the next tick; a cancel drops it, and the next open is an ordinary one.
void TestAcceptedReplayReturnsWithSelection()
{
    var state = new HistoryPanelState();
    var firstRun = CreateRun("run-1", "Vanessa");
    var selectedRun = CreateRun("run-2", "Vanessa");
    var selectedBattle = CreateBattle("battle-2", 12, "Lost");
    SetPage(state, "RunPage", firstRun, selectedRun);
    SetPage(state, "BattlePage", CreateBattle("battle-1", 11, "Won"), selectedBattle);
    state.SelectedRunIndex = 1;
    state.SelectedBattleIndex = 1;
    state.SelectedGhostBattleIndex = 2;
    state.SelectedRunHero = "Vanessa";
    state.GhostDayMin10 = true;
    state.GhostBattleFilter = GhostBattleFilter.ILost;
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"bpp-history-selection-{Guid.NewGuid():N}.sqlite3"
    );
    using var db = new SqliteConnection($"Data Source={databasePath}");
    db.Open();
    RunLogSchema.EnsureInitialized(db);
    using (var command = db.CreateCommand())
    {
        command.CommandText = """
            INSERT INTO runs (run_id,hero,game_mode,status,started_at_ms,last_seen_at_ms)
            VALUES ('run-1','Vanessa','Ranked','completed',1767225600000,1767225600000),
                ('run-2','Vanessa','Ranked','completed',1767312000000,1767312000000);
            INSERT INTO battles (battle_id,run_id,source,recorded_at_ms,combat_kind,local_payload_state)
            VALUES ('battle-1','run-2','LOCAL',1767312000000,'PVPCombat','missing'),
                ('battle-2','run-2','LOCAL',1767225600000,'PVPCombat','missing');
            """;
        command.ExecuteNonQuery();
    }
    var repository = new HistoryPanelRepository(databasePath);
    var dependencies = Dependencies(
        new HistoryPanelDataService(repository, null),
        replay: new AcceptingReplay()
    );
    var previewRequests = 0;
    var hides = 0;
    var pendingAtHide = false;
    var shows = 0;
    var showOpens = true;
    bool? resumed = null;
    SavedReplaysStub? savedReplays = null;
    var lobbyActive = false;
    HistoryPanelCoordinator? coordinator = null;
    // The host's side: its hide closes the panel and its show, when it opens, begins a session.
    var port = new HistoryPanelReplayReturnPort(
        () =>
        {
            hides++;
            pendingAtHide = coordinator!.ReplayReturnPending;
            coordinator.OnPanelHidden();
        },
        () =>
        {
            shows++;
            if (showOpens)
                resumed = coordinator!.OnPanelShown();
        },
        () => savedReplays,
        () => lobbyActive
    );
    coordinator = new HistoryPanelCoordinator(
        state,
        dependencies,
        () => { },
        () => previewRequests++,
        port
    );

    void ReplaySelected()
    {
        var before = hides;
        _ = coordinator.TryReplaySelectedBattleAsync(
            HistoryPanelViewModelBuilder.ActiveSelectedBattle(state, coordinator),
            false
        );
        uiContext.Until(() => hides == before + 1);
        Assert(
            pendingAtHide && coordinator.ReplayReturnPending,
            "An accepted replay must arm the return before its hide, which must keep it."
        );
    }

    void UntilDetail(string battleId) =>
        uiContext.Until(() => state.DetailBattleId == battleId && !state.DetailLoading);

    // The dependencies' profile reads no account: exercise the panel as a signed-out client.
    try
    {
        Assert(!coordinator.OnPanelShown(), "An ordinary open must not resume a selection.");
        UntilDetail("battle-2");
        Assert(
            state.Runs.Count == 2 && !ReferenceEquals(state.Runs[0], selectedRun),
            "An ordinary panel open must read persisted records."
        );

        ReplaySelected();
        coordinator.TickHidden();
        Assert(shows == 0, "A null replay runtime must keep the return waiting.");
        savedReplays = new SavedReplaysStub { HasSavedReplaySession = true };
        coordinator.TickHidden();
        Assert(shows == 0, "A running replay session must keep the return waiting.");
        savedReplays.HasSavedReplaySession = false;
        coordinator.TickHidden();
        Assert(shows == 0, "The return must wait for the lobby.");
        lobbyActive = true;
        previewRequests = 0;
        coordinator.TickHidden();
        Assert(
            shows == 1 && resumed == true && !coordinator.ReplayReturnPending,
            "The return must show the panel once, resuming the selection and consuming the return."
        );
        UntilDetail("battle-2");

        Assert(
            state.Runs.Count == 2
                && state.GetSelectedRun(coordinator.GetFilteredRuns())?.RunId == "run-2",
            "Returning from replay must reload the page and retain the selected run by ID."
        );
        Assert(
            state.GetSelectedBattle()?.BattleId == "battle-2",
            "Returning from replay must retain the selected battle, not the latest battle."
        );
        Assert(
            state.SelectedRunIndex == 0,
            "Reloading may change the selected index while retaining run identity."
        );
        Assert(
            state.SelectedRunHero == "Vanessa"
                && state.GhostDayMin10
                && state.GhostBattleFilter == GhostBattleFilter.ILost
                && state.SelectedGhostBattleIndex == 2,
            "Returning from replay must retain both run and ghost filters and selection."
        );
        Assert(
            previewRequests > 0,
            "Returning from replay must refresh the selected preview after loading its detail."
        );
        coordinator.TickHidden();
        Assert(shows == 1, "A consumed return must not show the panel again.");

        // A show that does not open (a combat, a missing font) keeps the return for the next tick.
        showOpens = false;
        ReplaySelected();
        coordinator.TickHidden();
        coordinator.TickHidden();
        Assert(
            shows == 3 && coordinator.ReplayReturnPending,
            "A show that does not open must keep the return and try again on the next tick."
        );
        Assert(
            coordinator.CancelReplayReturn() && !coordinator.CancelReplayReturn(),
            "A cancel drops a pending return once and is a no-op afterwards."
        );
        coordinator.TickHidden();
        Assert(shows == 3, "A cancelled return must not show the panel.");
        Assert(!coordinator.OnPanelShown(), "The open after a cancel must be an ordinary one.");
        uiContext.Until(() => !state.PageLoading);
        Assert(state.Runs.Count == 2, "The ordinary open after a cancel reads persisted records.");

        SetPage(state, "GhostPage", CreateBattle("previous-account-battle", 12, "Won"));
        state.CachedAccountId = "previous-account";
        coordinator.Tick(0f);
        Assert(
            state.GhostBattles.Count == 0
                && state.GetSelectedGhostBattle(state.GhostBattles) == null,
            "An account change must immediately clear the old Ghost page and its selection."
        );
        coordinator.OnPanelHidden();
    }
    finally
    {
        DisposeAndDrainReads(coordinator);
        db.Close();
        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
    }
}

// The delete and favorite buttons dispatch by section inside the coordinator: Favorites removes
// the selected favorite or hands its note to the host's dialog, every other section deletes the
// selected run or favorites the active battle, and nothing selected does nothing.
void TestDeleteAndFavoriteDispatchBySection()
{
    var state = new HistoryPanelState();
    SetPage(state, "RunPage", CreateRun("run-9", "Vanessa"));
    // No database is opened: arming a confirmation and returning a favorite read only state.
    var repository = new HistoryPanelRepository(
        Path.Combine(Path.GetTempPath(), $"bpp-history-dispatch-{Guid.NewGuid():N}.sqlite3")
    );
    var favorites = new HistoryFavoriteService(
        repository,
        string.Empty,
        (string _, out PvpBattleManifest? _, out PvpReplayPayload? _) =>
            throw new InvalidOperationException("Dispatch tests load no replay."),
        (_, _, _) => throw new InvalidOperationException("Dispatch tests load no Ghost replay."),
        (out string _) => throw new InvalidOperationException("Dispatch tests replay nothing."),
        (_, _, _, _) => throw new InvalidOperationException("Dispatch tests replay nothing.")
    );
    var dependencies = new HistoryPanelDependencies(
        new StubRunContext(),
        new HistoryPanelDataService(repository, null),
        new AcceptingReplay(),
        null,
        null,
        null,
        new BazaarDbAccountLinkStore(_ => false, _ => { }),
        () => 0f,
        () => null
    )
    {
        Favorites = favorites,
    };
    using var coordinator = new HistoryPanelCoordinator(
        state,
        dependencies,
        () => { },
        () => { },
        NoReplayReturn()
    );

    coordinator.DeleteSelected();
    Assert(
        coordinator.IsDeleteRunConfirmationActive("run-9", 0f),
        "Delete in Runs must arm the selected run's confirmation."
    );
    Assert(
        coordinator.FavoriteSelected() == null && !state.FavoriteActionInProgress,
        "Favorite in Runs favorites the active battle and hands no note to edit."
    );

    state.SectionMode = HistorySectionMode.Favorites;
    var status = state.StatusMessage;
    Assert(
        coordinator.FavoriteSelected() == null && state.StatusMessage == status,
        "Favorite in Favorites with nothing selected must do nothing."
    );
    coordinator.DeleteSelected();
    Assert(
        coordinator.IsDeleteRunConfirmationActive("run-9", 0f) && state.StatusMessage == status,
        "Delete in Favorites with nothing selected must do nothing, not delete the run."
    );

    var favorite = new HistoryFavoriteRecord(
        (HistoryBattleRecord)CreateBattle("favorite-1", 9, "Won"),
        DateTimeOffset.Parse("2026-06-19T00:00:00Z"),
        "keep this one"
    );
    SetPage(state, "FavoritePage", favorite);
    Assert(
        ReferenceEquals(coordinator.FavoriteSelected(), favorite)
            && !state.FavoriteActionInProgress
            && state.StatusMessage == status,
        "Favorite in Favorites must hand back the selected favorite and change nothing."
    );
    coordinator.DeleteSelected();
    Assert(
        coordinator.IsRemoveFavoriteConfirmationActive("favorite-1", 0f)
            && !coordinator.IsDeleteRunConfirmationActive("run-9", 0f),
        "Delete in Favorites must arm the selected favorite's confirmation, not the run's."
    );
}

// A confirmed run delete and a favorite removal run their SQLite and file work off the caller's
// thread. Until a delete publishes, its run stays listed, Delete is disabled and nothing may start
// loading the run's replays; then the run leaves the list through the same reload a synchronous
// delete used. A delete cannot be cancelled, so one that crosses a close and reopen still owns its
// flag and still reloads the open list. A removal that finishes after the user left Favorites
// does not load its page into the open section.
void TestDeleteLeaseFailurePaths()
{
    foreach (var failure in new[] { "unavailable", "protected", "database", "cleanup" })
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"bpp-delete-failure-{Guid.NewGuid():N}.sqlite3"
        );
        using var db = new SqliteConnection($"Data Source={path}");
        db.Open();
        RunLogSchema.EnsureInitialized(db);
        void Execute(string sql)
        {
            using var command = db.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        Execute(
            """
            INSERT INTO runs(run_id,hero,game_mode,status,started_at_ms,last_seen_at_ms)
            VALUES ('delete-me','Vanessa','Unranked','abandoned',0,0);
            INSERT INTO battles(battle_id,run_id,source,recorded_at_ms,combat_kind,local_payload_state)
            VALUES ('battle','delete-me','LOCAL',0,'PVPCombat','missing');
            """
        );
        var state = new HistoryPanelState();
        var replay = new AcceptingReplay();
        using var gate = replay.PayloadGate;
        using var entered = new ManualResetEventSlim();
        replay.PrepareMaintenance = () =>
            () =>
            {
                entered.Set();
                return failure == "unavailable"
                    ? null
                    : gate.AcquireMaintenance(CancellationToken.None);
            };
        replay.Cleanup = _ =>
        {
            using var competing = gate.TryAcquirePlayback();
            Assert(competing == null, "Cleanup errors still occur inside the exclusive lease.");
            throw new IOException("Injected payload cleanup failure.");
        };
        var dependencies = new HistoryPanelDependencies(
            new StubRunContext(),
            new HistoryPanelDataService(new HistoryPanelRepository(path), null),
            replay,
            null,
            null,
            null,
            new BazaarDbAccountLinkStore(_ => false, _ => { }),
            () => 0f,
            () => null
        );
        var coordinator = new HistoryPanelCoordinator(
            state,
            dependencies,
            () => { },
            () => { },
            NoReplayReturn()
        );
        try
        {
            coordinator.OnPanelShown();
            uiContext.Until(() => !state.PageLoading && !state.DetailLoading);
            using var playing = gate.TryAcquirePlayback();
            coordinator.DeleteSelected();
            coordinator.DeleteSelected();
            Assert(
                entered.Wait(TimeSpan.FromSeconds(5)),
                "The delete must reach the lease factory."
            );
            if (failure == "protected")
                Execute(
                    "UPDATE runs SET status='completed', game_mode='ranked' WHERE run_id='delete-me'"
                );
            if (failure == "database")
                Execute(
                    "CREATE TRIGGER refuse_delete BEFORE DELETE ON runs BEGIN SELECT RAISE(ABORT,'injected'); END"
                );
            playing!.Dispose();
            uiContext.Until(() => !state.RunDeleteInProgress && !state.PageLoading);
            using var query = db.CreateCommand();
            query.CommandText = "SELECT COUNT(*) FROM runs WHERE run_id='delete-me'";
            var count = (long)query.ExecuteScalar()!;
            Assert(
                count == (failure == "cleanup" ? 0 : 1),
                $"{failure}: only a post-commit cleanup error may leave the run deleted."
            );
            Assert(
                replay.CleanedBattleIds.Count == (failure == "cleanup" ? 1 : 0),
                $"{failure}: a refused or failed transaction must not clean payloads."
            );
            using var available = gate.TryAcquirePlayback();
            Assert(available != null, $"{failure}: every exit must release the maintenance lease.");
            Assert(
                !state.RunDeleteInProgress,
                $"{failure}: every exit must clear the pending state."
            );
        }
        finally
        {
            DisposeAndDrainReads(coordinator);
            db.Close();
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}

void TestOffThreadDeleteAndRemovalPublishInTheirSection()
{
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"bpp-history-delete-{Guid.NewGuid():N}.sqlite3"
    );
    using (var db = new SqliteConnection($"Data Source={databasePath}"))
    {
        db.Open();
        RunLogSchema.EnsureInitialized(db);
        using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO runs (run_id,hero,game_mode,status,started_at_ms,last_seen_at_ms)
            VALUES ('run-keep','Vanessa','Unranked','abandoned',1767225600000,1767225600000),
                ('run-gone','Vanessa','Unranked','abandoned',1767312000000,1767312000000),
                ('run-late','Vanessa','Unranked','abandoned',1767398400000,1767398400000);
            INSERT INTO battles (battle_id,run_id,source,recorded_at_ms,combat_kind,local_payload_state)
            VALUES ('battle-gone','run-gone','LOCAL',1767312000000,'PVPCombat','missing'),
                ('battle-late','run-late','LOCAL',1767398400000,'PVPCombat','missing'),
                ('battle-fav','run-keep','LOCAL',1767225600000,'PVPCombat','missing');
            """;
        command.ExecuteNonQuery();
    }
    var repository = new HistoryPanelRepository(databasePath);
    Assert(
        repository.AddFavorite("battle-fav", false, null, DateTimeOffset.UtcNow),
        "The scenario's favorite must insert."
    );
    var state = new HistoryPanelState();
    var replay = new AcceptingReplay();
    var replayDirectory = databasePath + ".replays";
    Directory.CreateDirectory(replayDirectory);
    var payloadPath = Path.Combine(replayDirectory, "battle-gone.payload.mpack.gz");
    File.WriteAllBytes(payloadPath, [1, 2, 3]);
    using var maintenanceEntered = new ManualResetEventSlim();
    var runtime = new SavedReplaysStub
    {
        Maintenance = () =>
        {
            Assert(
                SynchronizationContext.Current != uiContext,
                "Only a worker may wait for the lease."
            );
            maintenanceEntered.Set();
            return replay.PayloadGate.AcquireMaintenance(CancellationToken.None);
        },
    };
    var realReplay = new HistoryPanelReplayService(
        () =>
        {
            Assert(
                SynchronizationContext.Current == uiContext,
                "Capture the host runtime on the UI thread."
            );
            return runtime;
        },
        new NoRecording(),
        replayDirectory,
        () => null
    );
    replay.PrepareMaintenance = realReplay.PreparePayloadMaintenance;
    replay.Cleanup = ids =>
    {
        using var competing = replay.PayloadGate.TryAcquirePlayback();
        Assert(competing == null, "The delete lease must cover payload cleanup too.");
        return realReplay.CleanupReplayPayloads(ids);
    };
    var dependencies = new HistoryPanelDependencies(
        new StubRunContext(),
        new HistoryPanelDataService(repository, null),
        replay,
        null,
        null,
        null,
        new BazaarDbAccountLinkStore(_ => false, _ => { }),
        () => 0f,
        () => null
    )
    {
        Favorites = new HistoryFavoriteService(
            repository,
            string.Empty,
            (string _, out PvpBattleManifest? _, out PvpReplayPayload? _) =>
                throw new InvalidOperationException("Nothing may load a replay to favorite."),
            (_, _, _) => throw new InvalidOperationException("No Ghost replay is loaded."),
            (out string _) => throw new InvalidOperationException("No favorite replays."),
            (_, _, _, _) => throw new InvalidOperationException("No favorite replays.")
        ),
    };
    var refresh = new HistoryPanelUiRefresh();
    var immediateBuilds = 0;
    var frameBuilds = 0;
    HistoryPanelViewModel? published = null;
    HistoryPanelCoordinator coordinator = null!;
    coordinator = new HistoryPanelCoordinator(
        state,
        dependencies,
        () =>
        {
            // The former host built on every notification. Count the actual builder, then
            // compare the new host's one consumption at a simulated visible-frame boundary.
            _ = Model();
            immediateBuilds++;
            refresh.Request();
        },
        () => { },
        NoReplayReturn()
    );
    HistoryPanelViewModel Model() =>
        HistoryPanelViewModelBuilder.Build(
            state,
            coordinator,
            new StubRunContext(),
            null,
            0f,
            Array.Empty<BazaarPlusPlus.Game.Supporters.BPPSupporterSample>(),
            default,
            default
        );
    void Frame(string stage)
    {
        var before = frameBuilds;
        refresh.Flush(() =>
        {
            published = Model();
            frameBuilds++;
        });
        refresh.Flush(() =>
        {
            published = Model();
            frameBuilds++;
        });
        Assert(
            frameBuilds == before + 1,
            $"{stage}: dirty consumes once and clean consumes nothing."
        );
        Assert(
            published!.StatusMessage == state.StatusMessage,
            $"{stage}: the rendered model keeps the latest completion or failure message."
        );
        Console.WriteLine(
            $"History refresh {stage}: immediate builds={immediateBuilds}, frame builds={frameBuilds}"
        );
    }
    void SelectRun(string runId)
    {
        coordinator.SelectRun(state.Runs.ToList().FindIndex(run => run.RunId == runId));
        // The run's battle page, its detail and its favorite check have all published.
        uiContext.Until(() =>
            state.GetSelectedBattle() is { } battle
            && battle.RunId == runId
            && !state.DetailLoading
            && state.FavoriteCheckBattleId == battle.BattleId
        );
    }
    try
    {
        coordinator.OnPanelShown();
        uiContext.Until(() => !state.PageLoading && !state.DetailLoading);
        Frame("open");
        var selectionBuilds = immediateBuilds;
        SelectRun("run-gone");
        Frame("selection");
        Assert(
            immediateBuilds - selectionBuilds > 1,
            "Selection must reproduce redundant full model builds."
        );
        Assert(
            Model() is { ReplayButtonEnabled: true, FavoriteButtonEnabled: true },
            "Before the delete the run's battle can replay and be favorited."
        );

        var replayResult = new TaskCompletionSource<HistoryPanelReplayAttemptResult>();
        replay.PendingReplay = replayResult.Task;
        var pendingReplay = coordinator.TryReplaySelectedBattleAsync(
            state.GetSelectedBattle(),
            recordVideo: false
        );
        Assert(replay.CanCommit?.Invoke() == true, "The replay starts with an active selection.");
        using var playing = replay.PayloadGate.TryAcquirePlayback();
        Assert(playing != null, "The playing replay must own the real gate.");
        coordinator.DeleteSelected();
        coordinator.DeleteSelected();
        Assert(
            maintenanceEntered.Wait(TimeSpan.FromSeconds(5)),
            "The worker must reach maintenance acquisition."
        );
        using (var db = new SqliteConnection($"Data Source={databasePath}"))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM runs WHERE run_id='run-gone'";
            Assert(
                (long)command.ExecuteScalar()! == 1 && File.Exists(payloadPath),
                "Playback must protect both the DB row and file before the delete transaction."
            );
        }
        Frame("delete pending");
        Assert(
            published!.DeleteButtonBusy && published.ReplayButtonBusy,
            "The pending frame must disable delete and replay."
        );
        var uiResponded = false;
        uiContext.Post(_ => uiResponded = true, null);
        uiContext.Until(() => uiResponded);
        Assert(
            state.RunDeleteInProgress
                && state.StatusSeverity == StatusSeverity.Pending
                && state.Runs.Any(run => run.RunId == "run-gone"),
            "Until the off-thread delete publishes, the run stays listed and the line is pending."
        );
        Assert(
            Model()
                is {
                    DeleteButtonEnabled: false,
                    DeleteButtonBusy: true,
                    ReplayButtonEnabled: false,
                    ReplayButtonBusy: true,
                    RecordAndReplayButtonEnabled: false,
                    FavoriteButtonEnabled: false,
                },
            "While the delete runs, Delete and the run's replay and favorite actions are disabled."
        );
        coordinator.FavoriteSelected();
        Assert(
            !state.FavoriteActionInProgress,
            "Favorite must not start loading a battle of the run being deleted."
        );
        coordinator.DeleteSelected();
        Assert(
            replay.CanCommit?.Invoke() == false,
            "A replay already loading cannot commit after deletion starts."
        );
        replayResult.SetCanceled();
        uiContext.Until(() => pendingReplay.IsCompleted);
        coordinator
            .TryReplaySelectedBattleAsync(state.GetSelectedBattle(), recordVideo: false)
            .GetAwaiter()
            .GetResult();
        Assert(
            !state.ReplayActionInProgress,
            "The coordinator refuses replay even if a click races rendering."
        );
        playing!.Dispose();

        uiContext.Until(() => !state.RunDeleteInProgress && !state.PageLoading);
        Assert(
            !File.Exists(payloadPath),
            "The payload is removed after playback releases its lease."
        );
        using (var available = replay.PayloadGate.TryAcquirePlayback())
            Assert(available != null, "A completed delete releases its maintenance lease.");
        Assert(
            state.Runs.Select(run => run.RunId).SequenceEqual(["run-late", "run-keep"]),
            "The deleted run must leave the list once the delete publishes."
        );
        Assert(
            state.StatusSeverity == StatusSeverity.Success
                && replay.CleanedBattleIds.SequenceEqual(["battle-gone"]),
            "The delete must clean the run's replay payloads once and report success."
        );

        Frame("delete completed");
        Assert(
            !published!.DeleteButtonBusy,
            "The completion frame must release the delete busy state."
        );

        // A delete that crosses a close and reopen keeps its flag, then reloads the open list and
        // drops its pending line.
        SelectRun("run-late");
        coordinator.DeleteSelected();
        coordinator.DeleteSelected();
        var deleting = state.StatusMessage;
        coordinator.OnPanelHidden();
        coordinator.OnPanelShown();
        Assert(
            state.RunDeleteInProgress && !Model().DeleteButtonEnabled,
            "A session boundary must not end a delete that cannot be cancelled."
        );
        uiContext.Until(() => !state.RunDeleteInProgress && !state.PageLoading);
        Assert(
            state.Runs.Select(run => run.RunId).SequenceEqual(["run-keep"])
                && state.StatusMessage != deleting
                && Model().DeleteButtonEnabled,
            "A delete finishing in the next session must reload the list and clear its line."
        );

        // A removal finishing after the user left Favorites publishes nothing into Runs.
        coordinator.SetSectionMode(HistorySectionMode.Favorites);
        uiContext.Until(() => !state.PageLoading && !state.DetailLoading);
        Assert(state.Favorites.Count == 1, "The scenario's favorite must list.");
        coordinator.DeleteSelected();
        coordinator.DeleteSelected();
        Assert(
            state.FavoriteActionInProgress && !Model().DeleteButtonEnabled,
            "Remove must stay disabled until the off-thread removal publishes."
        );
        Frame("favorite removal pending");
        Assert(!published!.DeleteButtonEnabled, "Favorite removal must remain visibly busy.");
        coordinator.SetSectionMode(HistorySectionMode.Runs);
        uiContext.Until(() => !state.FavoriteActionInProgress && !state.PageLoading);
        Assert(
            state.SectionMode == HistorySectionMode.Runs
                && state.Runs.Select(run => run.RunId).SequenceEqual(["run-keep"])
                && state.StatusMessage != HistoryPanelText.FavoriteRemoved(),
            "A removal finishing in Runs must not load the favorites page or report into Runs."
        );
        Frame("favorite removal completed in Runs");
        Assert(
            immediateBuilds > frameBuilds,
            "Coordinator callbacks must build fewer models after coalescing."
        );
        Assert(!repository.IsFavorite("battle-fav"), "The removal itself must complete.");
        coordinator.OnPanelHidden();
    }
    finally
    {
        DisposeAndDrainReads(coordinator);
        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
        Directory.Delete(replayDirectory, recursive: true);
        replay.PayloadGate.Dispose();
    }
}

// A host that records no video (Android) has no MP4 to keep: its delete prompt does not promise
// one, and the desktop's, which records, still does.
void TestDeletePromptMentionsMp4OnlyWhereTheHostRecords()
{
    foreach (var recordingSupported in new[] { false, true })
    {
        var state = new HistoryPanelState();
        SetPage(state, "RunPage", CreateRun("run-9", "Vanessa"));
        var repository = new HistoryPanelRepository(
            Path.Combine(Path.GetTempPath(), $"bpp-history-prompt-{Guid.NewGuid():N}.sqlite3")
        );
        var dependencies = new HistoryPanelDependencies(
            new StubRunContext(),
            new HistoryPanelDataService(repository, null),
            new AcceptingReplay { RecordingSupported = recordingSupported },
            null,
            null,
            null,
            new BazaarDbAccountLinkStore(_ => false, _ => { }),
            () => 0f,
            () => null
        );
        using var coordinator = new HistoryPanelCoordinator(
            state,
            dependencies,
            () => { },
            () => { },
            NoReplayReturn()
        );

        coordinator.DeleteSelected();

        Assert(
            coordinator.IsDeleteRunConfirmationActive("run-9", 0f)
                && state.StatusMessage?.Contains("run-9", StringComparison.Ordinal) == true,
            "Delete must arm the confirmation and name the run."
        );
        Assert(
            state.StatusMessage!.Contains("MP4", StringComparison.Ordinal) == recordingSupported,
            recordingSupported
                ? "A host that records keeps saying its MP4 recordings are kept."
                : "A host that records no video must not mention MP4 recordings."
        );
    }
}

void TestAccountArrivalSyncsOnlyTheOpenGhostSection()
{
    WithProfileCapsule(
        "Ghost",
        (state, coordinator, syncAttempts) =>
        {
            Invoke(coordinatorType, coordinator, "OnPanelShown");
            uiContext.Until(() => !(bool)stateType.GetProperty("PageLoading")!.GetValue(state)!);
            Assert(syncAttempts() == 1, "Entering Ghost before the profile loads tries one sync.");

            FakeClientCache.SetAccountId("account-arrived");
            Invoke(coordinatorType, coordinator, "Tick", 0f);
            Assert(
                syncAttempts() == 2,
                "The profile account arriving while Ghost is open must sync Ghost battles."
            );
            uiContext.Until(() => !(bool)stateType.GetProperty("PageLoading")!.GetValue(state)!);
            Assert(
                GetNullableString(state, "CachedAccountId") == "account-arrived",
                "The Ghost list must reload for the arrived account."
            );

            Invoke(coordinatorType, coordinator, "Tick", 0f);
            Assert(syncAttempts() == 2, "An unchanged account must not sync again.");

            // The tick reads the profile once a second, not every frame: a scenario that needs a
            // second observation advances the clock by at least a second.
            FakeClientCache.SetAccountId("account-switched");
            Invoke(coordinatorType, coordinator, "Tick", 0.5f);
            Assert(
                syncAttempts() == 2
                    && GetNullableString(state, "CachedAccountId") == "account-arrived",
                "A tick within a second of the last profile read must not read it again."
            );
            Invoke(coordinatorType, coordinator, "Tick", 1f);
            Assert(
                syncAttempts() == 3
                    && GetNullableString(state, "CachedAccountId") == "account-switched",
                "The first tick a second later must observe the switched account and sync it."
            );
            uiContext.Until(() => !(bool)stateType.GetProperty("PageLoading")!.GetValue(state)!);
            Invoke(coordinatorType, coordinator, "OnPanelHidden");
        }
    );

    WithProfileCapsule(
        "Ghost",
        (state, coordinator, syncAttempts) =>
        {
            FakeClientCache.SetAccountId("account-known");
            Invoke(coordinatorType, coordinator, "OnPanelShown");
            Invoke(coordinatorType, coordinator, "Tick", 0f);
            uiContext.Until(() => !(bool)stateType.GetProperty("PageLoading")!.GetValue(state)!);
            Assert(
                syncAttempts() == 1,
                "Opening Ghost with a new account adopts it in the opening session and syncs once."
            );
            Invoke(coordinatorType, coordinator, "OnPanelHidden");
        }
    );

    WithProfileCapsule(
        "Runs",
        (state, coordinator, syncAttempts) =>
        {
            Invoke(coordinatorType, coordinator, "OnPanelShown");
            uiContext.Until(() => !(bool)stateType.GetProperty("PageLoading")!.GetValue(state)!);

            FakeClientCache.SetAccountId("account-arrived");
            Invoke(coordinatorType, coordinator, "Tick", 0f);
            uiContext.Until(() => !(bool)stateType.GetProperty("PageLoading")!.GetValue(state)!);
            Assert(
                syncAttempts() == 0,
                "The profile account arriving while Runs is open must not sync Ghost battles."
            );
            Assert(
                GetNullableString(state, "CachedAccountId") == "account-arrived",
                "Runs must still observe the arrived account."
            );
            Invoke(coordinatorType, coordinator, "OnPanelHidden");
        }
    );
}

void TestAccountLinkActionHandsAccountChangeToTheObserver()
{
    WithProfileCapsule(
        "Ghost",
        (state, coordinator, syncAttempts) =>
        {
            // The signed-out Ghost read is still in flight when the account-link row reads the
            // profile; that read must not strand the list in its loading state.
            Invoke(coordinatorType, coordinator, "OnPanelShown");
            Assert(syncAttempts() == 1, "Entering Ghost before the profile loads tries one sync.");

            FakeClientCache.SetAccountId("account-linked");
            Invoke(coordinatorType, coordinator, "ToggleAccountLinkForm");
            Assert(
                syncAttempts() == 2,
                "An account change first seen by the account-link row must still sync Ghost battles."
            );
            uiContext.Until(() => !(bool)stateType.GetProperty("PageLoading")!.GetValue(state)!);
            Assert(
                GetNullableString(state, "CachedAccountId") == "account-linked",
                "The Ghost list must reload for the account the link row saw."
            );

            Invoke(coordinatorType, coordinator, "Tick", 0f);
            Assert(syncAttempts() == 2, "The next tick must not handle the same change again.");
            Invoke(coordinatorType, coordinator, "OnPanelHidden");
        }
    );
}

void TestFailedSyncMarksTheEmptyGhostListIncomplete()
{
    WithProfileCapsule(
        "Ghost",
        (state, coordinator, syncAttempts) =>
        {
            FakeClientCache.SetAccountId("account-known");
            Invoke(coordinatorType, coordinator, "OnPanelShown");
            uiContext.Until(() => !(bool)stateType.GetProperty("PageLoading")!.GetValue(state)!);
            Assert(
                syncAttempts() == 1 && GhostSync(state) == "Failed",
                "The capsule's Ghost sync fails for want of an account id."
            );
            Assert(
                ArchiveListMessage(state) == HistoryText("GhostSyncIncomplete"),
                "An empty Ghost list after a failed sync must not claim nothing is saved."
            );
            Assert(
                GetNullableString(state, "StatusMessage")?.Contains("player_account_id_unavailable")
                    == true,
                "The status bar must keep the sync failure detail."
            );
            Invoke(coordinatorType, coordinator, "OnPanelHidden");
        }
    );
}

// A sync cancelled by its session returns without touching state, so each session boundary must
// reset the phase; otherwise the next entry reports "already running" and never syncs again.
void TestSessionBoundariesRestartACancelledSync()
{
    WithProfileCapsule(
        "Ghost",
        (state, coordinator, syncAttempts) =>
        {
            FakeClientCache.SetAccountId("account-known");
            Invoke(coordinatorType, coordinator, "OnPanelShown");
            Assert(
                syncAttempts() == 1 && GhostSync(state) == "Running",
                "The held Ghost sync stays in flight."
            );
            Invoke(coordinatorType, coordinator, "OnPanelHidden");
            Assert(GhostSync(state) == "NotStarted", "Closing History ends the sync phase.");
            Invoke(coordinatorType, coordinator, "OnPanelShown");
            AssertSyncRestarted(state, syncAttempts, "Reopening History after a cancelled sync");
            Invoke(coordinatorType, coordinator, "OnPanelHidden");
        },
        holdSync: true
    );
}

// ObserveAccount cancels the old account's sync and starts the new one before the cancelled request
// has unwound; the new sync must wait for it instead of failing as already running.
void TestAccountChangeMidSyncSyncsTheNewAccount()
{
    WithProfileCapsule(
        "Ghost",
        (state, coordinator, syncAttempts) =>
        {
            FakeClientCache.SetAccountId("account-a");
            Invoke(coordinatorType, coordinator, "OnPanelShown");
            Assert(
                syncAttempts() == 1 && GhostSync(state) == "Running",
                "The held Ghost sync stays in flight."
            );
            FakeClientCache.SetAccountId("account-b");
            Invoke(coordinatorType, coordinator, "Tick", 0f);
            AssertSyncRestarted(state, syncAttempts, "A profile change during a sync");
            Invoke(coordinatorType, coordinator, "OnPanelHidden");
        },
        holdSync: true
    );
}

// The cancelled request is still unwinding when the new session starts its sync, so that sync first
// waits in place; once the UI context lets the old request finish, the new one reaches the server.
void AssertSyncRestarted(object state, Func<int> syncAttempts, string when)
{
    Assert(
        GetNullableString(state, "StatusMessage") != HistoryText("GhostSyncAlreadyRunning")
            && GhostSync(state) == "Running"
            && syncAttempts() == 1,
        $"{when} must queue a new sync behind the cancelled one, not fail it."
    );
    uiContext.Until(() => syncAttempts() == 2);
    Assert(GhostSync(state) == "Running", $"{when} must run the new sync.");
}

string GhostSync(object state) => stateType.GetProperty("GhostSync")!.GetValue(state)!.ToString()!;

string HistoryText(string name) =>
    (string)InvokeStatic(RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelText"), name)!;

string ArchiveListMessage(object state)
{
    var board = Activator.CreateInstance(
        RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryBoardFacts")
    );
    var facts = InvokeStatic(
        RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryArchiveFacts"),
        "Observe",
        state,
        null,
        board,
        board
    );
    var status = InvokeStatic(
        RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelDecisions"),
        "ArchiveStatus",
        facts
    );
    return GetString(status, "ListMessage")!;
}

// A History capsule whose game profile is a settable fake and whose Ghost sync service counts
// attempts: the account resolver runs once per sync. By default it reports no identity, so the sync
// fails before any request; holdSync supplies one and holds the request until its session ends.
void WithProfileCapsule(
    string section,
    Action<object, IDisposable, Func<int>> body,
    bool holdSync = false
)
{
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"bpp-history-account-{Guid.NewGuid():N}.sqlite3"
    );
    using (var db = new SqliteConnection($"Data Source={databasePath}"))
    {
        db.Open();
        RunLogSchema.EnsureInitialized(db);
    }
    var repository = Construct(
        RequireType("BazaarPlusPlus.Game.HistoryPanel.Storage.HistoryPanelRepository"),
        databasePath
    );
    using var api =
        BazaarPlusPlus.ModApi.Clients.ModApiSession.TryCreate(
            "https://api.example",
            "test",
            platformKey: null,
            "OnlineClient",
            TimeSpan.FromSeconds(5),
            holdSync ? new HoldingHandler() : new RefusingHandler()
        ) ?? throw new InvalidOperationException("Test Mod API session should construct.");
    var syncAttempts = 0;
    var syncService = Construct(
        RequireType("BazaarPlusPlus.Game.HistoryPanel.Ghost.GhostBattleSyncService"),
        repository,
        api,
        (Func<string?>)(
            () =>
            {
                syncAttempts++;
                return holdSync ? "sync-account" : null;
            }
        ),
        UnusedPvpBattleEvidence.Instance,
        (Func<Guid, bool>)(_ => true)
    );
    var dataService = Construct(dataServiceType, repository, syncService);
    // The profile account is the settable fake; the BazaarDB link hints stay in memory.
    var linkHints = new HashSet<string>();
    var dependencies = Dependencies(
        dataService,
        () => FakeClientCache.Profile.Value?.AccountId,
        new BazaarDbAccountLinkStore(linkHints.Contains, key => linkHints.Add(key))
    );
    var state = Activator.CreateInstance(stateType)!;
    stateType
        .GetProperty("SectionMode")!
        .SetValue(
            state,
            Enum.Parse(RequireType("BazaarPlusPlus.Game.HistoryPanel.HistorySectionMode"), section)
        );
    using var coordinator = (IDisposable)
        Activator.CreateInstance(
            coordinatorType,
            state,
            dependencies,
            (Action)(() => { }),
            (Action)(() => { }),
            NoReplayReturn()
        )!;

    FakeClientCache.SetAccountId(null);
    try
    {
        body(state, coordinator, () => syncAttempts);
    }
    finally
    {
        DisposeAndDrainReads(coordinator);
        api.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
    }
}

// Closing a panel discards publication, but a Task.Run database read may still own its SQLite
// handle. Teardown must pump the UI context until those reads return before deleting the file.
// Reflection is confined to fixture cleanup; the scenarios exercise the coordinator's API.
void DisposeAndDrainReads(IDisposable coordinator)
{
    coordinator.Dispose();
    var reads = coordinatorType
        .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .Where(field =>
            field.FieldType.IsGenericType
            && field.FieldType.GetGenericTypeDefinition().FullName
                == "BazaarPlusPlus.Game.HistoryPanel.LatestHistoryRead`1"
        )
        .Select(field => field.GetValue(coordinator)!)
        .ToArray();
    uiContext.Until(() =>
        reads.All(read =>
            !(bool)
                read.GetType()
                    .GetField("_running", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(read)!
        )
    );
}

object CreateRun(string runId, string hero)
{
    return Activator.CreateInstance(
            runRecordType,
            runId,
            hero,
            "Ranked",
            DateTimeOffset.Parse("2026-06-19T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-19T00:10:00Z"),
            DateTimeOffset.Parse("2026-06-19T00:10:00Z"),
            10,
            1,
            40,
            0,
            5,
            1,
            10,
            "Bronze",
            100,
            null,
            1,
            0,
            "finished"
        ) ?? throw new InvalidOperationException("HistoryRunRecord should construct.");
}

void TestGhostListTextMarksOnlyTheFinalBattle()
{
    var formatterType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelFormatter");
    var final = (string)
        InvokeStatic(
            formatterType,
            "GhostListText",
            CreateBattle("final", 12, "Win", isFinalBattle: true)
        )!;
    var ongoing = (string)
        InvokeStatic(formatterType, "GhostListText", CreateBattle("ongoing", 12, "Win"))!;
    Assert(
        final.Split('\n')[1] == "D12 · Final",
        $"A run-ending Ghost must mark its day line; got {final}."
    );
    Assert(
        ongoing.Split('\n')[1] == "D12",
        $"An ordinary Ghost must keep the bare day line; got {ongoing}."
    );
}

void TestRunRatingShowsItsChangeOnlyWhenKnown()
{
    var formatterType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelFormatter");
    string Rating(int rating, int? delta) =>
        (string)InvokeStatic(formatterType, "RatingWithDelta", rating, delta)!;
    Assert(Rating(1520, null) == "1520", "An unknown change must show the rating alone.");
    Assert(
        Rating(1520, 16) == "1520 <color=#9ECC8A>+16</color>",
        $"A gain must read as +N; got {Rating(1520, 16)}."
    );
    Assert(
        Rating(1520, -12) == "1520 <color=#E5664F>-12</color>",
        $"A loss must read as -N; got {Rating(1520, -12)}."
    );
    Assert(Rating(1520, 0) == "1520 ±0", "A known zero change must read as ±0, not vanish.");
}

object CreateBattle(string battleId, int? day, string result, bool isFinalBattle = false)
{
    var counts =
        Activator.CreateInstance(snapshotCountsType, 0, 0, 0, 0)
        ?? throw new InvalidOperationException("HistoryBattleSnapshotCounts should construct.");

    return Activator.CreateInstance(
            battleRecordType,
            battleId,
            "run-1",
            DateTimeOffset.Parse("2026-06-19T00:00:00Z"),
            day,
            1,
            null,
            "Vanessa",
            null,
            null,
            null,
            null,
            null,
            "Opponent",
            "Mak",
            null,
            null,
            null,
            null,
            null,
            null,
            "PVPCombat",
            result,
            null,
            null,
            counts,
            isFinalBattle,
            Enum.Parse(battleSourceType, "Ghost"),
            Enum.Parse(
                RequireType("BazaarPlusPlus.Game.HistoryPanel.Data.ReplayAvailability"),
                "Unavailable"
            )
        ) ?? throw new InvalidOperationException("HistoryBattleRecord should construct.");
}

IList CreateRunList(params object[] runs)
{
    var listType = typeof(List<>).MakeGenericType(runRecordType);
    var list = (IList)(Activator.CreateInstance(listType) ?? throw new InvalidOperationException());
    foreach (var run in runs)
        list.Add(run);
    return list;
}

void SetPage(object state, string propertyName, params object[] records)
{
    var property = stateType.GetProperty(propertyName)!;
    var pageType = property.PropertyType;
    var counted = pageType.GetGenericTypeDefinition().Name == "HistoryCountedPage`1";
    const BindingFlags factory = BindingFlags.Public | BindingFlags.Static;
    if (records.Length == 0)
    {
        property.SetValue(
            state,
            counted
                ? pageType.GetMethod("Empty", factory)!.Invoke(null, [0L])
                : pageType.GetProperty("Empty", factory)!.GetValue(null)
        );
        return;
    }
    var rows = Array.CreateInstance(pageType.GetGenericArguments()[0], records.Length);
    for (var i = 0; i < records.Length; i++)
        rows.SetValue(records[i], i);
    var anchor = Construct(cursorType, 1767225600000L, "anchor");
    property.SetValue(
        state,
        counted
            ? Construct(pageType, rows, anchor, anchor, 1L, (long)records.Length)
            : Construct(pageType, rows, anchor, anchor, false, false)
    );
}

IList GetList(object instance, string propertyName) =>
    (IList)(
        instance.GetType().GetProperty(propertyName)?.GetValue(instance)
        ?? throw new InvalidOperationException($"{propertyName} should be a list.")
    );

object? Invoke(Type type, object instance, string methodName, params object?[] args)
{
    var method =
        type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SingleOrDefault(method =>
                method.Name == methodName && method.GetParameters().Length == args.Length
            )
        ?? throw new MissingMethodException(type.FullName, methodName);
    return method.Invoke(instance, args);
}

object? InvokeStatic(Type type, string methodName, params object?[] args)
{
    var method =
        type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .SingleOrDefault(method =>
                method.Name == methodName && method.GetParameters().Length == args.Length
            )
        ?? throw new MissingMethodException(type.FullName, methodName);
    return method.Invoke(null, args);
}

string? GetString(object? instance, string propertyName) =>
    (string?)(
        instance?.GetType().GetProperty(propertyName)?.GetValue(instance)
        ?? throw new InvalidOperationException($"{propertyName} should exist.")
    );

string? GetNullableString(object instance, string propertyName)
{
    var property =
        instance.GetType().GetProperty(propertyName)
        ?? throw new InvalidOperationException($"{propertyName} should exist.");
    return (string?)property.GetValue(instance);
}

// History's services live in Core and its SQL, records and pages in Storage.
Type RequireType(string fullName) =>
    coreAssembly.GetType(fullName)
    ?? storageAssembly.GetType(fullName)
    ?? throw new InvalidOperationException($"{fullName} should exist.");

// The coordinator's dependencies with the host ports a test supplies: the profile account (none
// by default), a clock, in-memory BazaarDB link hints, and a replay service where a test replays.
HistoryPanelDependencies Dependencies(
    object dataService,
    Func<string?>? profileAccountId = null,
    BazaarDbAccountLinkStore? accountLinkStore = null,
    IHistoryPanelReplay? replay = null
) =>
    new HistoryPanelDependencies(
        null!,
        (HistoryPanelDataService)dataService,
        replay!,
        null,
        null,
        null,
        accountLinkStore ?? new BazaarDbAccountLinkStore(_ => false, _ => { }),
        () => 0f,
        profileAccountId ?? (() => null)
    );

// A replay return the scenario never arms.
HistoryPanelReplayReturnPort NoReplayReturn() => new(() => { }, () => { }, () => null, () => false);

object Construct(Type type, params object?[] args)
{
    var constructor =
        type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SingleOrDefault(ctor => ctor.GetParameters().Length == args.Length)
        ?? throw new MissingMethodException(type.FullName, ".ctor");
    return constructor.Invoke(args);
}

void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

// Stands in for the game's client-cache profile the host's account port reads.
internal static class FakeClientCache
{
    public static FakeObservable Profile { get; } = new();

    public static void SetAccountId(string? accountId) =>
        Profile.Value = accountId == null ? null : new FakeProfile(accountId);
}

internal sealed class FakeObservable
{
    public bool HasData => Value != null;

    public FakeProfile? Value { get; set; }
}

internal sealed record FakeProfile(string AccountId);

internal sealed class RefusingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("History account tests must not reach the network.");
}

internal sealed class HoldingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>();
        // Like a real transport, a cancelled request unwinds after the session boundary returns: the
        // cancellation completes only when the test next pumps the UI context, so History starts the
        // next sync while the cancelled one still holds the Ghost sync service.
        cancellationToken.Register(() =>
        {
            if (SynchronizationContext.Current is { } context)
                context.Post(_ => pending.TrySetCanceled(cancellationToken), null);
            else
                pending.TrySetCanceled(cancellationToken);
        });
        return pending.Task;
    }
}

// The Ghost sync service's game-typed reads; these tests never download a replay.
internal sealed class UnusedPvpBattleEvidence : IPvpBattleEvidenceBinding
{
    public static UnusedPvpBattleEvidence Instance { get; } = new();

    public int CardEnumValue(PvpCardEnum kind, string name) =>
        throw new InvalidOperationException("History filtering tests read no card enums.");

    public bool TryCardEnumName(PvpCardEnum kind, int value, out string name) =>
        throw new InvalidOperationException("History filtering tests read no card enums.");

    public void VerifyReplay(PvpReplayPayload payload) =>
        throw new InvalidOperationException("History filtering tests verify no replay.");
}

// Every replay is accepted; the scenarios never reach a recorder or a payload.
internal sealed class AcceptingReplay : IHistoryPanelReplay
{
    public bool RecordingSupported { get; init; }

    public ReplayPayloadOperationGate PayloadGate { get; } = new();

    public Func<Func<IDisposable?>>? PrepareMaintenance { get; set; }

    public Func<IDisposable?> PreparePayloadMaintenance() =>
        PrepareMaintenance?.Invoke()
        ?? (() => PayloadGate.AcquireMaintenance(CancellationToken.None));

    public Func<IReadOnlyList<string>, ReplayPayloadCleanupResult>? Cleanup { get; set; }

    public bool CanReplayBattle(HistoryBattleRecord? battle, out string reason)
    {
        reason = string.Empty;
        return battle != null;
    }

    public bool CanRecordReplay(
        HistoryBattleRecord? battle,
        out string reason,
        out HistoryPanelReplayReasonCode reasonCode
    ) => CanRecordLoadedReplay(out reason, out reasonCode);

    public bool CanRecordLoadedReplay(out string reason)
    {
        reason = "no recorder";
        return false;
    }

    public bool CanRecordLoadedReplay(
        out string reason,
        out HistoryPanelReplayReasonCode reasonCode
    )
    {
        reasonCode = HistoryPanelReplayReasonCode.Completed;
        return CanRecordLoadedReplay(out reason);
    }

    public void PrewarmRecordingAvailability() { }

    public Task<HistoryPanelReplayAttemptResult>? PendingReplay { get; set; }

    public Func<bool>? CanCommit { get; private set; }

    public Task<HistoryPanelReplayAttemptResult> ReplayBattleAsync(
        HistoryBattleRecord? battle,
        bool recordVideo,
        CancellationToken cancellationToken,
        Func<bool>? canCommit = null
    )
    {
        CanCommit = canCommit;
        return PendingReplay
            ?? Task.FromResult(HistoryPanelReplayAttemptResult.Success("replay started"));
    }

    // The battles whose payloads a delete asked to clean, in order.
    public List<string> CleanedBattleIds { get; } = [];

    public ReplayPayloadCleanupResult CleanupReplayPayloads(IReadOnlyList<string> battleIds)
    {
        CleanedBattleIds.AddRange(battleIds);
        return Cleanup?.Invoke(battleIds) ?? new ReplayPayloadCleanupResult(0, null);
    }

    public string GetReplayActionLabel(HistoryBattleRecord? battle) => "Replay";
}

// The host's saved-replay runtime as the return reads it: only whether a session is running.
internal sealed class SavedReplaysStub : ISavedReplayRuntime
{
    public bool HasSavedReplaySession { get; set; }

    public Func<IDisposable?> Maintenance { get; set; } = () => throw Unused();

    public IDisposable? AcquirePayloadMaintenance() => Maintenance();

    public bool CanReplaySavedCombats(out string reason) => throw Unused();

    public bool CanReplaySavedBattle(string battleId, out string reason) => throw Unused();

    public Task<bool> ReplaySavedAsync(
        string battleId,
        bool recordVideo,
        CancellationToken cancellationToken = default,
        Func<bool>? canCommit = null
    ) => throw Unused();

    public bool ReplayLoadedBattle(
        PvpBattleManifest manifest,
        PvpReplayPayload payload,
        bool recordVideo,
        CombatReplayPlaybackSource source
    ) => throw Unused();

    public SavedBattleLoadOutcome TryLoadSavedBattle(
        string battleId,
        out PvpBattleManifest? manifest,
        out PvpReplayPayload? payload
    ) => throw Unused();

    private static InvalidOperationException Unused() =>
        new("The replay return reads only HasSavedReplaySession.");
}

internal sealed class StubRunContext : IRunContext
{
    public bool IsInGameRun { get; set; }

    public string? CurrentServerRunId { get; set; }

    public RunExitKind LastRunExitKind { get; set; }
}

internal sealed class NoRecording : IReplayRecordingAvailability
{
    public bool Supported => false;

    public bool CanRecord() => false;

    public void Prewarm() { }
}
