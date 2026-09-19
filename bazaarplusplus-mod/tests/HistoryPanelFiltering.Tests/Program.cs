#nullable enable
using System.Collections;
using System.Reflection;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

using var uiContext = new TestUiContext();
SynchronizationContext.SetSynchronizationContext(uiContext);

var modAssembly = Assembly.Load("BazaarPlusPlus");
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

TestRunHeroRosterAddsOneCanonicalTheDragonsAfterTheExistingSeven();
TestRunHeroPresentationTreatsAliasesAsSelectedAndDisplaysCanonicalName();
TestCoordinatorCanonicalizesAliasFilterState();
TestStateSelectedRunUsesFilteredRunList();
TestPageReplacementUpdatesSelection();
TestCoordinatorRunSelectionUsesFilteredSpace();
TestReplayReturnPreservesSelectionAndFilters();
TestGhostListTextMarksOnlyTheFinalBattle();

Console.WriteLine("HistoryPanelFiltering checks passed.");

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

void TestRunHeroPresentationTreatsAliasesAsSelectedAndDisplaysCanonicalName()
{
    Assert(
        (bool)InvokeStatic(heroPresentationType, "IsSelected", "Hero8", "TheDragons")!,
        "A canonical chip should stay selected for legacy filter state."
    );
    Assert(
        (bool)InvokeStatic(heroPresentationType, "IsSelected", "TheDragons", "Hero8")!,
        "Alias-selected state should be symmetric."
    );
    Assert(
        (string)InvokeStatic(heroPresentationType, "DisplayName", "Hero8")! == "The Dragons",
        "Legacy history should display the canonical native The Dragons name."
    );
    Assert(
        (string)InvokeStatic(heroPresentationType, "DisplayName", "TheDragons")! == "The Dragons",
        "Canonical history should display the native The Dragons name."
    );
}

void TestCoordinatorCanonicalizesAliasFilterState()
{
    var state =
        Activator.CreateInstance(stateType)
        ?? throw new InvalidOperationException("HistoryPanelState should construct.");
    var dataService = Construct(dataServiceType, null, null);
    // Single 7-arg ctor arity match (issue #167): runState, dataService, replayService,
    // serverHealthProbe, accountLinkClient, isBazaarDbAccountLinkAvailable, combatReplayDirectoryPath.
    var dependencies = Construct(dependenciesType, null, dataService, null, null, null, null, null);
    var coordinator =
        Activator.CreateInstance(
            coordinatorType,
            state,
            dependencies,
            (Action)(() => { }),
            (Action)(() => { }),
            (Action<bool>)(_ => { })
        ) ?? throw new InvalidOperationException("HistoryPanelCoordinator should construct.");

    Invoke(coordinatorType, coordinator, "SetRunHeroFilter", "Hero8");
    Assert(
        GetNullableString(state, "SelectedRunHero") == "TheDragons",
        "Legacy filter input should persist as canonical History UI state."
    );

    Invoke(coordinatorType, coordinator, "SetRunHeroFilter", "TheDragons");
    Assert(
        GetNullableString(state, "SelectedRunHero") == null,
        "Selecting either alias again should toggle the canonical filter off."
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
    // Single 7-arg ctor arity match (issue #167): runState, dataService, replayService,
    // serverHealthProbe, accountLinkClient, isBazaarDbAccountLinkAvailable, combatReplayDirectoryPath.
    var dependencies = Construct(dependenciesType, null, dataService, null, null, null, null, null);
    var coordinator =
        Activator.CreateInstance(
            coordinatorType,
            state,
            dependencies,
            (Action)(() => { }),
            (Action)(() => { }),
            (Action<bool>)(_ => { })
        ) ?? throw new InvalidOperationException("HistoryPanelCoordinator should construct.");

    Invoke(coordinatorType, coordinator, "SelectRun", 1);
    var selected = Invoke(coordinatorType, coordinator, "GetSelectedRun");

    Assert(
        GetString(selected, "RunId") == "raw-3",
        "SelectRun should interpret indexes in the current filtered database page."
    );
}

void TestReplayReturnPreservesSelectionAndFilters()
{
    var state = Activator.CreateInstance(stateType)!;
    var firstRun = CreateRun("run-1", "Vanessa");
    var selectedRun = CreateRun("run-2", "Vanessa");
    var selectedBattle = CreateBattle("battle-2", 12, "Lost");
    SetPage(state, "RunPage", firstRun, selectedRun);
    SetPage(state, "BattlePage", CreateBattle("battle-1", 11, "Won"), selectedBattle);
    stateType.GetProperty("SelectedRunIndex")!.SetValue(state, 1);
    stateType.GetProperty("SelectedBattleIndex")!.SetValue(state, 1);
    stateType.GetProperty("SelectedGhostBattleIndex")!.SetValue(state, 2);
    stateType.GetProperty("SelectedRunHero")!.SetValue(state, "Vanessa");
    stateType.GetProperty("GhostDayMin10")!.SetValue(state, true);
    stateType
        .GetProperty("GhostBattleFilter")!
        .SetValue(state, Enum.Parse(ghostFilterType, "ILost"));
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
            INSERT INTO runs (run_id,hero,game_mode,status,started_at_utc,last_seen_at_utc)
            VALUES ('run-1','Vanessa','Ranked','completed','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z'),
                ('run-2','Vanessa','Ranked','completed','2026-01-02T00:00:00Z','2026-01-02T00:00:00Z');
            INSERT INTO battles (battle_id,run_id,source,recorded_at_utc,combat_kind,local_payload_state)
            VALUES ('battle-1','run-2','LOCAL','2026-01-02T00:00:00Z','PVPCombat','missing'),
                ('battle-2','run-2','LOCAL','2026-01-01T00:00:00Z','PVPCombat','missing');
            """;
        command.ExecuteNonQuery();
    }
    var repository = Construct(
        RequireType("BazaarPlusPlus.Game.HistoryPanel.Storage.HistoryPanelRepository"),
        databasePath
    );
    var dataService = Construct(dataServiceType, repository, null);
    var dependencies = Construct(dependenciesType, null, dataService, null, null, null, null, null);
    var previewRequests = 0;
    using var coordinator = (IDisposable)
        Activator.CreateInstance(
            coordinatorType,
            state,
            dependencies,
            (Action)(() => { }),
            (Action)(() => previewRequests++),
            (Action<bool>)(_ => { })
        )!;

    // This capsule has no Unity PlayerPrefs; exercise the panel as a signed-out client.
    var bridge = RequireType("BazaarPlusPlus.GameInterop.BppClientCacheBridge");
    var cacheType = bridge.GetField(
        "_clientCacheType",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;
    var resolved = bridge.GetField(
        "_clientCacheTypeResolved",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;
    var previousType = cacheType.GetValue(null);
    var previousResolved = resolved.GetValue(null);
    cacheType.SetValue(null, null);
    resolved.SetValue(null, true);
    try
    {
        Invoke(coordinatorType, coordinator, "OnPanelHidden");
        previewRequests = 0;
        Invoke(coordinatorType, coordinator, "OnPanelShown", true);
        uiContext.Until(() =>
            stateType.GetProperty("DetailBattleId")!.GetValue(state)?.ToString() == "battle-2"
            && !(bool)stateType.GetProperty("DetailLoading")!.GetValue(state)!
        );

        Assert(
            GetList(state, "Runs").Count == 2
                && GetString(Invoke(coordinatorType, coordinator, "GetSelectedRun"), "RunId")
                    == "run-2",
            "Returning from replay must reload the page and retain the selected run by ID."
        );
        Assert(
            GetString(Invoke(stateType, state, "GetSelectedBattle"), "BattleId") == "battle-2",
            "Returning from replay must retain the selected battle, not the latest battle."
        );
        Assert(
            (int)stateType.GetProperty("SelectedRunIndex")!.GetValue(state)! == 0,
            "Reloading may change the selected index while retaining run identity."
        );
        Assert(
            GetNullableString(state, "SelectedRunHero") == "Vanessa"
                && (bool)stateType.GetProperty("GhostDayMin10")!.GetValue(state)!
                && stateType.GetProperty("GhostBattleFilter")!.GetValue(state)!.ToString()
                    == "ILost"
                && (int)stateType.GetProperty("SelectedGhostBattleIndex")!.GetValue(state)! == 2,
            "Returning from replay must retain both run and ghost filters and selection."
        );
        Assert(
            previewRequests > 0,
            "Returning from replay must refresh the selected preview after loading its detail."
        );

        Invoke(coordinatorType, coordinator, "OnPanelHidden");
        Invoke(coordinatorType, coordinator, "OnPanelShown", false);
        uiContext.Until(() => !(bool)stateType.GetProperty("PageLoading")!.GetValue(state)!);
        Assert(
            GetList(state, "Runs").Count == 2
                && !ReferenceEquals(GetList(state, "Runs")[0], selectedRun),
            "A normal panel open must read persisted records."
        );

        SetPage(state, "GhostPage", CreateBattle("previous-account-battle", 12, "Won"));
        stateType.GetProperty("CachedAccountId")!.SetValue(state, "previous-account");
        Invoke(coordinatorType, coordinator, "Tick", 0f);
        Assert(
            GetList(state, "GhostBattles").Count == 0
                && Invoke(
                    stateType,
                    state,
                    "GetSelectedGhostBattle",
                    GetList(state, "GhostBattles")
                ) == null,
            "An account change must immediately clear the old Ghost page and its selection."
        );
        Invoke(coordinatorType, coordinator, "OnPanelHidden");
    }
    finally
    {
        cacheType.SetValue(null, previousType);
        resolved.SetValue(null, previousResolved);
        db.Close();
        File.Delete(databasePath);
    }
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
            false,
            false
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
    var rows = Array.CreateInstance(property.PropertyType.GetGenericArguments()[0], records.Length);
    for (var i = 0; i < records.Length; i++)
        rows.SetValue(records[i], i);
    property.SetValue(state, Construct(property.PropertyType, rows, null, null, false, false));
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

Type RequireType(string fullName) =>
    modAssembly.GetType(fullName)
    ?? throw new InvalidOperationException($"{fullName} should exist.");

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
