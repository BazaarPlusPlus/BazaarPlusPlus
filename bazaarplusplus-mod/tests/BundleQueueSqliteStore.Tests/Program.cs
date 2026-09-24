#nullable enable
using BazaarPlusPlus.Storage.BundleQueue;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.Sqlite;
using Microsoft.Data.Sqlite;

TestSqliteUtcInstant();
TestEligibilityAllocationPublishAndDueOrdering();
TestOutcomeResealAndCleanupQueries();

Console.WriteLine("Bundle queue SQLite store checks passed.");

static void TestSqliteUtcInstant()
{
    using var connection = new SqliteConnection("Data Source=:memory:");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT datetime('2026-08-03T05:00:00.0000000+00:00', '+2 minutes');";
    var raw = command.ExecuteScalar() as string;
    Assert(raw == "2026-08-03 05:02:00", "SQLite datetime output shape");
    var parsed = SqliteUtcInstant.Parse(raw!);
    Assert(parsed.Offset == TimeSpan.Zero, "offset-less SQLite datetime is UTC");
    Assert(parsed == DateTimeOffset.Parse("2026-08-03T05:02:00+00:00"), "SQLite UTC instant");
    Assert(
        SqliteUtcInstant.Parse("2026-08-03T05:02:00+08:00")
            == DateTimeOffset.Parse("2026-08-02T21:02:00+00:00"),
        "explicit offsets normalize to UTC"
    );
}

static void TestEligibilityAllocationPublishAndDueOrdering()
{
    WithStore(
        (store, connection) =>
        {
            InsertRun(connection, "ranked", "ranked", "Online", completed: true, screenshot: true);
            InsertRun(connection, "ptr", "ranked", "Ptr", completed: true, screenshot: false);
            InsertRun(connection, "casual", "casual", "Online", completed: true, screenshot: false);
            store.EnsureEligibleJobs(TimeSpan.FromMinutes(2));

            var ids = store.ListWaitingRunIds();
            Assert(
                ids.SequenceEqual(["ranked"]),
                "Only completed online ranked runs are eligible."
            );
            var job = store.ReadJob("ranked")!;
            Assert(job.ScreenshotRequested, "Screenshot request policy must reach the seal job.");
            Assert(
                job.InputDeadlineAtUtc == DateTimeOffset.Parse("2026-08-03T05:02:00Z"),
                "Eligibility should persist the exact convergence deadline."
            );

            var first = store.EnsureAllocation("ranked", "bundle-a", 1000, Now());
            var second = store.EnsureAllocation("ranked", "bundle-b", 2000, Now().AddSeconds(1));
            Assert(
                first.BundleId == "bundle-a" && first.CreatedAtMs == 1000,
                "First allocation wins."
            );
            Assert(
                second.BundleId == "bundle-a" && second.CreatedAtMs == 1000,
                "Allocation is idempotent."
            );

            store.FreezePlayerAccountId("ranked", "account-a");
            Assert(
                Scalar(connection, "SELECT player_account_id FROM runs WHERE run_id='ranked';")
                    == "account-a",
                "Run account should freeze."
            );
            Assert(
                store.ReadJob("ranked")!.PlayerAccountId == "account-a",
                "Job account should freeze atomically."
            );

            var outbox = Publish("bundle-a", "ranked", "a.bundle", sealedBytes: 10);
            Assert(
                !store.PublishOutbox(new BundleAllocationRecord("wrong", 1000), outbox, Now()),
                "Allocation mismatch must reject publish."
            );
            Assert(store.ReadJob("ranked") != null, "Rejected publish must retain its seal job.");
            Assert(
                store.PublishOutbox(first, outbox, Now()),
                "Matching allocation should publish."
            );
            Assert(
                store.ReadJob("ranked") == null,
                "Publish should delete the job in the same transaction."
            );
            Assert(
                store.ContainsOutbox("bundle-a"),
                "Published allocation should exist in outbox."
            );
            Assert(
                store.ListDue(Now(), 3).Single().BundleId == "bundle-a",
                "Pending outbox should be due."
            );

            store.RecordTransient("bundle-a", "busy", null, Now(), Now().AddMinutes(3));
            Assert(
                store.ListDue(Now().AddMinutes(2), 3).Count == 0,
                "Transient delay must postpone due work."
            );
            Assert(
                store.ListDue(Now().AddMinutes(3), 3).Count == 1,
                "Due time boundary is inclusive."
            );
        }
    );
}

static void TestOutcomeResealAndCleanupQueries()
{
    WithStore(
        (store, connection) =>
        {
            InsertRun(connection, "run-a", "ranked", "Online", completed: true, screenshot: false);
            store.EnsureEligibleJobs(TimeSpan.Zero);
            var allocation = store.EnsureAllocation("run-a", "bundle-a", 1000, Now());
            store.PublishOutbox(
                allocation,
                Publish("bundle-a", "run-a", "a.bundle", 10),
                Now().AddDays(-20)
            );

            store.RecordOutcome(
                "bundle-a",
                new BundleUploadOutcomeRecord(true, "ok", null, "request-a", "stored"),
                Now()
            );
            Assert(
                Scalar(connection, "SELECT status FROM bundle_outbox WHERE bundle_id='bundle-a';")
                    == "uploaded",
                "Uploaded outcome should persist before file cleanup."
            );
            Assert(
                store.ListRetentionFileNames(Now().AddDays(-7)).Contains("a.bundle"),
                "Uploaded files are immediate retention candidates."
            );

            InsertOutbox(
                connection,
                "bundle-b",
                "run-b",
                "b.bundle",
                "pending",
                Now().AddDays(-20),
                null
            );
            InsertRun(connection, "run-b", "ranked", "Online", completed: true, screenshot: true);
            store.FailOutboxAndScheduleReseal("bundle-b", "run-b", "invalid", Now());
            Assert(
                Scalar(connection, "SELECT status FROM bundle_outbox WHERE bundle_id='bundle-b';")
                    == "permanent_failure",
                "Invalid outbox must fail."
            );
            Assert(
                store.ReadJob("run-b")?.State == BundleSealJobState.Waiting,
                "Invalid outbox must schedule reseal atomically."
            );

            InsertOutbox(
                connection,
                "bundle-c",
                "run-c",
                "c.bundle",
                "pending",
                Now().AddDays(-15),
                null
            );
            store.ExpirePending(Now().AddDays(-14), Now());
            Assert(
                Scalar(connection, "SELECT status FROM bundle_outbox WHERE bundle_id='bundle-c';")
                    == "permanent_failure",
                "Fourteen-day pending rows should expire."
            );
            Assert(
                store.ListReclaimFileNames(Now().AddDays(-14)).Contains("c.bundle"),
                "Expired files should be reclaim candidates."
            );

            InsertOutbox(
                connection,
                "bundle-d",
                "run-d",
                "d.bundle",
                "permanent_failure",
                Now().AddDays(-8),
                Now().AddDays(-8)
            );
            Assert(
                store.ListRetentionFileNames(Now().AddDays(-7)).Contains("d.bundle"),
                "Seven-day permanent files should be retention candidates."
            );
            Assert(
                RunLogSchema.LocalDatabaseSchemaVersion == 2,
                "Artifact lifecycle migration should own persistence schema version two."
            );
        }
    );
}

static void WithStore(Action<BundleQueueStore, SqliteConnection> test)
{
    var root = Path.Combine(
        Path.GetTempPath(),
        "bpp-bundle-queue-tests",
        Guid.NewGuid().ToString("N")
    );
    Directory.CreateDirectory(root);
    try
    {
        var database = Path.Combine(root, "queue.db");
        var store = new BundleQueueStore(database);
        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();
        test(store, connection);
    }
    finally
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(root, recursive: true);
    }
}

static void InsertRun(
    SqliteConnection connection,
    string runId,
    string mode,
    string channel,
    bool completed,
    bool screenshot
)
{
    Execute(
        connection,
        "INSERT INTO runs (run_id, started_at_utc, last_seen_at_utc, ended_at_utc, status, completed, hero, game_mode, build_channel, bundle_screenshot_requested) VALUES ($runId, $now, $now, $now, $status, $completed, 'Vanessa', $mode, $channel, $screenshot);",
        ("$runId", runId),
        ("$now", "2026-08-03T05:00:00Z"),
        ("$status", completed ? "completed" : "active"),
        ("$completed", completed ? 1 : 0),
        ("$mode", mode),
        ("$channel", channel),
        ("$screenshot", screenshot ? 1 : 0)
    );
}

static void InsertOutbox(
    SqliteConnection connection,
    string bundleId,
    string runId,
    string fileName,
    string status,
    DateTimeOffset sealedAt,
    DateTimeOffset? failedAt
)
{
    Execute(
        connection,
        "INSERT INTO bundle_outbox (bundle_id, run_id, file_name, content_sha256_hex, content_digest, total_bytes, has_screenshot, sealed_at_utc, status, next_attempt_at_utc, failed_at_utc) VALUES ($bundleId, $runId, $fileName, 'sha', 'digest', 10, 0, $sealedAt, $status, $sealedAt, $failedAt);",
        ("$bundleId", bundleId),
        ("$runId", runId),
        ("$fileName", fileName),
        ("$sealedAt", sealedAt.ToString("o")),
        ("$status", status),
        ("$failedAt", failedAt?.ToString("o"))
    );
}

static BundleOutboxPublishRecord Publish(
    string bundleId,
    string runId,
    string fileName,
    long sealedBytes
) => new(bundleId, runId, fileName, "sha", "digest", sealedBytes, false);
static DateTimeOffset Now() => DateTimeOffset.Parse("2026-08-03T06:00:00Z");

static void Execute(
    SqliteConnection connection,
    string sql,
    params (string Name, object? Value)[] parameters
)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    foreach (var parameter in parameters)
        command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
    command.ExecuteNonQuery();
}

static string? Scalar(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    return command.ExecuteScalar() as string;
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
