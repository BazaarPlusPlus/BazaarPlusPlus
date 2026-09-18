#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

internal static class HistoryPaginationTests
{
    internal static void Run(string root)
    {
        foreach (var size in new[] { 1000, 10000 })
            CheckRuns(Path.Combine(root, $"pages-{size}.sqlite3"), size);
        CheckFilterCases(Path.Combine(root, "filter-cases.sqlite3"));
    }

    private static void CheckRuns(string path, int count)
    {
        using var db = new SqliteConnection($"Data Source={path}");
        db.Open();
        RunLogSchema.EnsureInitialized(db);
        Execute(db, "DROP INDEX idx_runs_history_recent; DROP INDEX idx_runs_history_hero;");
        var build = Stopwatch.StartNew();
        Execute(
            db,
            $$"""
            WITH RECURSIVE n(x) AS (VALUES(0) UNION ALL SELECT x+1 FROM n WHERE x+1 < {{count}})
            INSERT INTO runs (run_id,hero,game_mode,status,started_at_utc,last_seen_at_utc)
            SELECT printf('r%05d',x), CASE WHEN x%97=0 THEN char(9)||'HeRo8'||char(160) ELSE 'Vanessa' END,
                'Ranked','completed',strftime('%Y-%m-%dT%H:%M:%SZ','2026-01-01',printf('+%d seconds',x/3)),
                strftime('%Y-%m-%dT%H:%M:%SZ','2026-01-01',printf('+%d seconds',x/3)) FROM n;
            WITH RECURSIVE n(x) AS (VALUES(0) UNION ALL SELECT x+1 FROM n WHERE x+1 < 10)
            INSERT INTO battles (battle_id,run_id,source,recorded_at_utc,local_payload_state,combat_kind)
            SELECT run_id||'-'||x,run_id,'LOCAL',last_seen_at_utc,'missing','PVPCombat' FROM runs,n;
            INSERT INTO battle_snapshots (battle_id,player_hand_json,player_skills_json,opponent_hand_json,opponent_skills_json)
            SELECT battle_id,'{"Items":[],"Status":"CapturedEmpty","Source":"Unknown"}',
                '{"Items":[],"Status":"CapturedEmpty","Source":"Unknown"}',
                '{"Items":[],"Status":"CapturedEmpty","Source":"Unknown"}',
                '{"Items":[],"Status":"CapturedEmpty","Source":"Unknown"}' FROM battles;
            """
        );
        var seedMs = build.Elapsed.TotalMilliseconds;
        build.Restart();
        RunLogSchema.EnsureInitialized(db);
        var indexMs = build.Elapsed.TotalMilliseconds;
        var repository = new HistoryPanelRepository(path);
        var expected = Enumerable.Range(0, count).Reverse().Select(i => $"r{i:00000}").ToArray();
        var visited = new List<string>();
        var pages = new List<HistoryPage<HistoryRunRecord>>();
        var page = repository.ListRuns(new());
        while (true)
        {
            Check(page.Rows.Count is > 0 and <= 40, "Every run page must be bounded and nonempty.");
            pages.Add(page);
            visited.AddRange(page.Rows.Select(r => r.RunId));
            if (!page.HasOlder)
                break;
            page = repository.ListRuns(new(page.Last));
        }
        Check(
            visited.SequenceEqual(expected),
            "Full run traversal must preserve every ID exactly once, including tied timestamps."
        );
        for (var i = pages.Count - 1; i > 0; i--)
        {
            page = repository.ListRuns(new(page.First, Newer: true));
            Check(
                page.Rows.Select(r => r.RunId)
                    .SequenceEqual(pages[i - 1].Rows.Select(r => r.RunId)),
                "Newer must invert older even for tied timestamps."
            );
        }
        Check(!page.HasNewer, "The newest page must have no newer records.");
        var dragons = new List<string>();
        page = repository.ListRuns(new(), "TheDragons");
        while (true)
        {
            dragons.AddRange(page.Rows.Select(r => r.RunId));
            if (!page.HasOlder)
                break;
            page = repository.ListRuns(new(page.Last), "Hero8");
        }
        Check(
            dragons.SequenceEqual(expected.Where(id => int.Parse(id[1..]) % 97 == 0)),
            "Sparse hero filters and whitespace aliases must be applied before LIMIT."
        );
        var anchor = repository.ListRuns(new(AnchorId: expected[count / 2]));
        Check(
            anchor.Rows[0].RunId == expected[count / 2],
            "ID anchor must survive deep selection."
        );
        Execute(
            db,
            "UPDATE runs SET last_seen_at_utc='2026-09-01T00:00:00Z' WHERE run_id='r00000';"
        );
        var preserved = repository.ListRuns(new(pages[^1].First, Inclusive: true));
        Check(
            preserved.Rows[0].RunId == pages[^1].Rows[0].RunId,
            "New insertions or recency changes must not displace the page anchor."
        );
        var latest = repository.ListRuns(new());
        Check(latest.Rows[0].RunId == "r00000", "Latest must reveal newer data.");
        Check(
            repository.ListRuns(new(), "missing").Rows.Count == 0,
            "Empty filters must produce an empty page."
        );
        var battlePage = repository.ListBattles(expected[0], new(Limit: 3));
        Check(
            battlePage.Rows.Count == 3 && battlePage.HasOlder,
            "Battle summary pages must be bounded."
        );
        Check(
            repository.LoadSnapshots(expected[0], battlePage.Rows[0].BattleId) != null,
            "Chosen snapshot must remain readable."
        );
        Check(
            repository.LoadSnapshots("wrong-run", battlePage.Rows[0].BattleId) == null,
            "Detail identity must include run ID."
        );
        var plan = Plan(
            db,
            $"SELECT run_id FROM runs WHERE {RunLogSchema.HistoryRunTime} <= '2026-01-01T00:00:01Z' AND ({RunLogSchema.HistoryRunTime} < '2026-01-01T00:00:01Z' OR run_id < 'r00004') ORDER BY {RunLogSchema.HistoryRunTime} DESC,run_id DESC LIMIT 41;"
        );
        Check(
            plan.Contains("SEARCH runs", StringComparison.Ordinal)
                && plan.Contains("idx_runs_history_recent", StringComparison.Ordinal),
            "Deep page must seek the recency index: " + plan
        );
        var times = Measure(() => repository.ListRuns(new()), 30);
        var deep = Measure(() => repository.ListRuns(new(pages[^1].First, Inclusive: true)), 30);
        using var version = db.CreateCommand();
        version.CommandText = "SELECT sqlite_version()";
        Console.WriteLine(
            $"History benchmark SQLite={version.ExecuteScalar()} runs={count} battles={count * 10} bytes={new FileInfo(path).Length} seed_ms={seedMs:F1} index_ms={indexMs:F1} n=30 warm_p50_ms={times.P50:F3} warm_p95_ms={times.P95:F3} deep_p50_ms={deep.P50:F3} deep_p95_ms={deep.P95:F3} plan={plan}"
        );
        if (count == 1000)
            CheckGhosts(db, repository);
    }

    private static void CheckGhosts(SqliteConnection db, HistoryPanelRepository repository)
    {
        Execute(
            db,
            """
            WITH RECURSIVE n(x) AS (VALUES(0) UNION ALL SELECT x+1 FROM n WHERE x+1 < 367)
            INSERT INTO battles (battle_id,source,remote_battle_id,uploader_account_id,local_player_account_id,bundle_id,recorded_at_utc,day,winner_combatant_id,result,ghost_replay_state,combat_kind)
            SELECT printf('g%04d',x),'GHOST',printf('remote%04d',x),'uploader',CASE WHEN x%2=0 THEN 'account-a' ELSE 'account-b' END,'bundle',
                '2026-09-01T00:00:00Z',CASE WHEN x%7=0 THEN 12 ELSE 2 END,
                CASE WHEN x%3=0 THEN ' OpPoNeNt ' ELSE ' PLAYER ' END,'won',
                CASE WHEN x%5=0 THEN 'local_ready' ELSE 'remote_available' END,'PVPCombat' FROM n;
            """
        );
        var ids = new List<string>();
        var page = repository.ListGhostBattles("account-a", GhostBattleFilter.All, false, new());
        while (true)
        {
            ids.AddRange(page.Rows.Select(b => b.BattleId));
            if (!page.HasOlder)
                break;
            page = repository.ListGhostBattles(
                "account-a",
                GhostBattleFilter.All,
                false,
                new(page.Last)
            );
        }
        Check(
            ids.Count == 184 && ids.Distinct().Count() == 184,
            "Ghost traversal must exceed 100 and isolate the local account."
        );
        var filtered = repository.ListGhostBattles(
            "account-a",
            GhostBattleFilter.IWon,
            true,
            new()
        );
        Check(
            filtered
                .Rows.Select(b => b.BattleId)
                .SequenceEqual(
                    Enumerable
                        .Range(0, 367)
                        .Where(x => x % 2 == 0 && x % 3 == 0 && x % 7 == 0)
                        .Reverse()
                        .Select(x => $"g{x:0000}")
                ),
            "Winner precedence and day filtering must precede the page limit."
        );
        Check(
            filtered.Rows.All(HistoryPanelFormatter.IsBattleWin),
            "SQL filtering and local-perspective outcome display must agree."
        );
        Check(
            repository.ListGhostBattles("", GhostBattleFilter.All, false, new()).Rows.Count == 0,
            "Signed out must not expose any account's Ghost records."
        );
        repository.MarkOldUndownloadedGhostBattlesDeleted(
            DateTimeOffset.Parse("2026-09-14T00:00:00Z")
        );
        var retained = repository.ListGhostBattles(
            "account-a",
            GhostBattleFilter.All,
            false,
            new()
        );
        Check(
            retained.Rows.Count == 37 && retained.Rows.All(b => b.ReplayDownloaded),
            "Downloaded facts must survive the discovery retention window."
        );
        Execute(db, "UPDATE battles SET deleted_at_utc='old' WHERE battle_id='g0000';");
        var hidden = repository.ListHiddenGhosts("account-a", null).Rows.Single();
        Execute(db, "UPDATE battles SET bundle_id='changed' WHERE battle_id='g0000';");
        Check(
            !repository.RestoreHiddenGhost(hidden),
            "Recovery must not publish after bundle identity changes."
        );
        hidden = repository.ListHiddenGhosts("account-a", null).Rows.Single();
        Check(
            repository.RestoreHiddenGhost(hidden),
            "Unchanged matching recovery candidate must become visible."
        );
    }

    private static void CheckFilterCases(string path)
    {
        using var db = new SqliteConnection($"Data Source={path}");
        db.Open();
        RunLogSchema.EnsureInitialized(db);
        Execute(
            db,
            """
            INSERT INTO runs (run_id,hero,game_mode,status,started_at_utc,last_seen_at_utc)
            VALUES ('legacy','Hero8','Ranked','completed','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z'),
                ('canonical','TheDragons','Ranked','completed','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z'),
                ('other','Vanessa','Ranked','completed','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
            WITH days(day) AS (VALUES(NULL),(9),(10),(12)),
                outcomes(name,winner,result) AS (
                    VALUES('win',NULL,' Lost '),('loss',NULL,' WoN '),
                        ('winner-win',' OpPoNeNt ','won'),('winner-loss',' PLAYER ','loss'),
                        ('unknown',NULL,'unknown')
                )
            INSERT INTO battles (battle_id,source,remote_battle_id,uploader_account_id,local_player_account_id,recorded_at_utc,day,winner_combatant_id,result,combat_kind)
            SELECT coalesce(day,'null')||'-'||name,'GHOST',coalesce(day,'null')||'-'||name,'uploader','account','2026-01-01T00:00:00Z',
                day,winner,result,'PVPCombat' FROM days,outcomes;
            """
        );
        var repository = new HistoryPanelRepository(path);
        foreach (var hero in new string?[] { null, "", " " })
            Check(
                repository.ListRuns(new(), hero).Rows.Count == 3,
                "An empty hero selection must include every run."
            );
        foreach (var hero in new[] { "TheDragons", "Hero8", "tHeDrAgOnS" })
        {
            var rows = repository.ListRuns(new(), hero).Rows;
            Check(
                rows.Select(r => r.RunId).OrderBy(id => id).SequenceEqual(["canonical", "legacy"])
                    && rows.Single(r => r.RunId == "canonical").Hero == "TheDragons"
                    && rows.Single(r => r.RunId == "legacy").Hero == "Hero8",
                "Either hero alias must select both stored forms without rewriting their values."
            );
        }
        Check(
            repository.ListRuns(new(), "vanessa").Rows.Single().RunId == "other"
                && repository.ListRuns(new(), "Mak").Rows.Count == 0,
            "Hero filtering must ignore case and exclude other heroes."
        );
        foreach (var dayMin10 in new[] { false, true })
        {
            var days = dayMin10 ? new[] { "10", "12" } : ["null", "9", "10", "12"];
            foreach (
                var (filter, outcomes) in new[]
                {
                    (
                        GhostBattleFilter.All,
                        new[] { "win", "loss", "winner-win", "winner-loss", "unknown" }
                    ),
                    (GhostBattleFilter.IWon, new[] { "win", "winner-win" }),
                    (GhostBattleFilter.ILost, new[] { "loss", "winner-loss" }),
                }
            )
            {
                var rows = repository.ListGhostBattles("account", filter, dayMin10, new()).Rows;
                var expected = days.SelectMany(day =>
                    outcomes.Select(outcome => $"{day}-{outcome}")
                );
                Check(
                    rows.Select(b => b.BattleId)
                        .OrderBy(id => id)
                        .SequenceEqual(expected.OrderBy(id => id)),
                    "Ghost filtering must combine the day cutoff with local outcomes and winner precedence."
                );
                if (filter != GhostBattleFilter.All)
                    Check(
                        rows.All(b =>
                            filter == GhostBattleFilter.IWon
                                ? HistoryPanelFormatter.IsBattleWin(b)
                                : HistoryPanelFormatter.IsBattleLoss(b)
                        ),
                        "Ghost outcome filtering and display must agree in local perspective."
                    );
            }
        }
    }

    private static (double P50, double P95) Measure(Action action, int repetitions)
    {
        action();
        var times = new double[repetitions];
        for (var i = 0; i < repetitions; i++)
        {
            var watch = Stopwatch.StartNew();
            action();
            times[i] = watch.Elapsed.TotalMilliseconds;
        }
        Array.Sort(times);
        return (times[repetitions / 2], times[(int)(repetitions * .95)]);
    }

    private static string Plan(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(reader.GetString(3));
        return string.Join("; ", rows);
    }

    private static void Execute(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Check(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }
}
