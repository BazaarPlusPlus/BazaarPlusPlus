using System.Diagnostics;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Storage.BundleQueue;
using BazaarPlusPlus.Storage.Sqlite;
using Microsoft.Data.Sqlite;

internal static class IdempotentAccountWriteTests
{
    internal static void Run()
    {
        foreach (var mode in new[] { SqliteJournalMode.Wal, SqliteJournalMode.Delete })
            CheckMode(mode);
    }

    private static void CheckMode(SqliteJournalMode mode)
    {
        var root = Path.Combine(Path.GetTempPath(), $"bpp-idempotent-account-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var database = Path.Combine(root, "runs.db");
            var options = new SqliteOpenOptions(mode);
            var queue = new BundleQueueStore(database, options);
            var battles = new PvpBattleSqliteStore(database, options);
            using var connection = new SqliteConnection($"Data Source={database}");
            connection.Open();
            Execute(
                connection,
                """
                INSERT INTO runs(run_id,started_at_ms,last_seen_at_ms,ended_at_ms,status,hero,game_mode,player_account_id)
                VALUES ('known',0,0,0,'completed','Vanessa','Ranked','first'),
                       ('new',0,0,0,'completed','Vanessa','Ranked',NULL);
                INSERT INTO battles(battle_id,source,run_id,recorded_at_ms,combat_kind,local_payload_state)
                VALUES ('attached','LOCAL','known',0,'PVPCombat','ready'),
                       ('unattached','LOCAL',NULL,0,'PVPCombat','ready');
                """
            );
            queue.EnsureEligibleJobs(TimeSpan.FromMinutes(2));
            var failures = new List<Exception>();
            // BEGIN IMMEDIATE holds the writer reservation while permitting readers in both modes.
            using (var writer = connection.BeginTransaction())
            {
                Observe(
                    "FreezePlayerAccountId",
                    () => queue.FreezePlayerAccountId("known", "second")
                );
                Observe("AttachToRun", () => battles.AttachToRun("attached", "new"));
                writer.Rollback();
            }
            if (failures.Count > 0)
                throw new AggregateException(
                    "Idempotent operations must not request the writer lock.",
                    failures
                );

            queue.FreezePlayerAccountId("new", "second");
            battles.AttachToRun("unattached", "new");
            Equal(
                "first",
                Scalar(connection, "SELECT player_account_id FROM runs WHERE run_id='known'")
            );
            Equal(
                "first",
                Scalar(
                    connection,
                    "SELECT player_account_id FROM bundle_seal_jobs WHERE run_id='known'"
                )
            );
            Equal(
                "second",
                Scalar(connection, "SELECT player_account_id FROM runs WHERE run_id='new'")
            );
            Equal(
                "second",
                Scalar(
                    connection,
                    "SELECT player_account_id FROM bundle_seal_jobs WHERE run_id='new'"
                )
            );
            Equal(
                "known",
                Scalar(connection, "SELECT run_id FROM battles WHERE battle_id='attached'")
            );
            Equal(
                "new",
                Scalar(connection, "SELECT run_id FROM battles WHERE battle_id='unattached'")
            );
            // Each side freezes independently; filling one must not overwrite the other's first value.
            Execute(
                connection,
                "UPDATE bundle_seal_jobs SET player_account_id=NULL WHERE run_id='known';"
            );
            queue.FreezePlayerAccountId("known", "third");
            Equal(
                "first",
                Scalar(connection, "SELECT player_account_id FROM runs WHERE run_id='known'")
            );
            Equal(
                "third",
                Scalar(
                    connection,
                    "SELECT player_account_id FROM bundle_seal_jobs WHERE run_id='known'"
                )
            );

            void Observe(string name, Action action)
            {
                var elapsed = Stopwatch.StartNew();
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failures.Add(new InvalidOperationException(name, ex));
                }
                Console.WriteLine(
                    $"{mode} {name}: {elapsed.ElapsedMilliseconds} ms with writer held"
                );
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }

    private static void Equal(string expected, string? actual)
    {
        if (actual != expected)
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
