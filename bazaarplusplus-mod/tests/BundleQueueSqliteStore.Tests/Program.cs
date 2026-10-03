#nullable enable
using System.Text.RegularExpressions;
using BazaarPlusPlus.Storage.BundleQueue;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.Sqlite;
using Microsoft.Data.Sqlite;

TestSqliteUtcInstant();
TestEligibilityAllocationPublishAndDueOrdering();
TestOutcomeResealAndCleanupQueries();
TestFinalOutboxEndsEligibility();
TestSealFailureBookkeeping();
TestSealEligibilityFixture();
TestSealJobDeadlineFormat();

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

            var first = store.EnsureAllocation("ranked", "bundle-a", 1000);
            var second = store.EnsureAllocation("ranked", "bundle-b", 2000);
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
            var allocation = store.EnsureAllocation("run-a", "bundle-a", 1000);
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
            store.FailOutboxAndScheduleReseal("bundle-a", "run-a", "pending_file_invalid", Now());
            Assert(
                Scalar(connection, "SELECT status FROM bundle_outbox WHERE bundle_id='bundle-a';")
                    == "uploaded"
                    && store.ReadJob("run-a") == null,
                "A stale pending-file snapshot must not overwrite an uploaded outcome or reseal it."
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
        }
    );
}

static void TestFinalOutboxEndsEligibility()
{
    WithStore(
        (store, connection) =>
        {
            InsertRun(
                connection,
                "rejected",
                "ranked",
                "Online",
                completed: true,
                screenshot: false
            );
            InsertRun(
                connection,
                "expired",
                "ranked",
                "Online",
                completed: true,
                screenshot: false
            );
            store.EnsureEligibleJobs(TimeSpan.Zero);
            foreach (var runId in new[] { "rejected", "expired" })
            {
                var allocation = store.EnsureAllocation(runId, "bundle-" + runId, 1000);
                store.PublishOutbox(
                    allocation,
                    Publish("bundle-" + runId, runId, runId + ".bundle", 10),
                    Now().AddDays(-20)
                );
            }

            store.RecordOutcome(
                "bundle-rejected",
                new BundleUploadOutcomeRecord(false, "bundle_rejected", null, "request-r", null),
                Now()
            );
            store.ExpirePending(Now().AddDays(-14), Now());
            store.EnsureEligibleJobs(TimeSpan.Zero);
            store.EnsureEligibleJobs(TimeSpan.Zero);

            Assert(
                store.ReadJob("rejected") == null && store.ReadJob("expired") == null,
                "A server rejection or retention expiry is final and must not start another seal."
            );
            Assert(
                Scalar(connection, "SELECT COUNT(*) || '' FROM bundle_outbox;") == "2",
                "Final outcomes must not accumulate replacement outbox rows."
            );
        }
    );
}

static void TestSealFailureBookkeeping()
{
    WithStore(
        (store, connection) =>
        {
            InsertRun(connection, "retry", "ranked", "Online", completed: true, screenshot: false);
            store.EnsureEligibleJobs(TimeSpan.Zero);
            store.EnsureAllocation("retry", "bundle-retry", 1000);
            var allocated = store.ReadJob("retry")!;
            Assert(
                allocated.State == BundleSealJobState.Sealing
                    && allocated.Attempts == 0
                    && allocated.LastAttemptAtUtc == null
                    && allocated.LastErrorCode == null,
                "Allocation marks the job sealing without counting a failed attempt."
            );

            store.RecordSealFailure(
                "retry",
                BundleSealJobState.Waiting,
                "seal_publish_failed",
                "System.IO.IOException: locked",
                3,
                Now()
            );
            var waiting = store.ReadJob("retry")!;
            Assert(
                waiting.State == BundleSealJobState.Waiting
                    && waiting.Attempts == 3
                    && waiting.LastAttemptAtUtc == Now()
                    && waiting.LastErrorCode == "seal_publish_failed",
                "A recorded failure writes state, attempts, time, and code together."
            );
            Assert(
                Scalar(
                    connection,
                    "SELECT last_error_detail FROM bundle_seal_jobs WHERE run_id='retry';"
                ) == "System.IO.IOException: locked",
                "The bounded diagnostic persists."
            );
            Assert(
                store.ListWaitingRunIds().SequenceEqual(["retry"]),
                "A retrying job stays waiting."
            );

            store.RecordSealFailure(
                "retry",
                BundleSealJobState.TerminalFailure,
                "bundle_build_failed",
                null,
                1,
                Now().AddSeconds(5)
            );
            Assert(
                store.ReadJob("retry")!.State == BundleSealJobState.TerminalFailure
                    && store.ListWaitingRunIds().Count == 0,
                "A terminal failure leaves the waiting set."
            );

            var rejected = false;
            try
            {
                store.RecordSealFailure(
                    "retry",
                    BundleSealJobState.Sealing,
                    "seal_attempt_failed",
                    null,
                    1,
                    Now()
                );
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }
            Assert(rejected, "A failure never records the in-flight sealing state.");
        }
    );
}

// The fixture is the cross-project contract for seal eligibility; installer History cleanup
// evaluates the same cases against its protection predicate.
static void TestSealEligibilityFixture()
{
    var cases = BundleSealEligibilityCase.Load();
    foreach (var eligibilityCase in cases)
        Assert(
            !eligibilityCase.EligibleForNewJob || eligibilityCase.ProtectedFromCleanup,
            $"{eligibilityCase.Name}: a Run eligible for a new seal job must be protected from cleanup."
        );

    WithStore(
        (store, connection) =>
        {
            foreach (var eligibilityCase in cases)
                eligibilityCase.Seed(connection);
            var existingJobs = cases
                .Where(eligibilityCase => store.ReadJob(eligibilityCase.Name) != null)
                .Select(eligibilityCase => eligibilityCase.Name)
                .ToHashSet(StringComparer.Ordinal);

            store.EnsureEligibleJobs(TimeSpan.FromMinutes(2));

            foreach (var eligibilityCase in cases)
            {
                var created =
                    !existingJobs.Contains(eligibilityCase.Name)
                    && store.ReadJob(eligibilityCase.Name) != null;
                Assert(
                    created == eligibilityCase.EligibleForNewJob,
                    $"{eligibilityCase.Name}: expected eligibleForNewJob={eligibilityCase.EligibleForNewJob}, EnsureEligibleJobs created={created}."
                );
            }
        }
    );
}

// bundle_seal_jobs.input_deadline_at_utc has one format, SQLite datetime() output
// (`yyyy-MM-dd HH:mm:ss`, UTC, whole seconds), because ListWaitingRunIds and
// idx_bundle_seal_jobs_state order it as text. Failure modes, each with an assertion below:
//  1. Same day, a datetime() deadline at 23:00 and a reseal deadline at 01:00: the reseal must
//     come first. A `"o"` value diverges at character 11 ('T' > ' ') and sorts after it.
//  2. FailOutboxAndScheduleReseal stores the datetime() shape, and a `now` with a non-zero offset
//     is stored as UTC.
//  3. SQLite datetime() really normalizes a `"o"` string (seven fractional digits, an offset).
//  4. The open-time repair rewrites only ISO-shaped rows; canonical rows stay byte-identical.
//  5. The repair is idempotent: a second open rewrites zero rows.
//  6. An ISO-shaped value datetime() cannot parse stays as it is and does not roll back the
//     initialization transaction through the NOT NULL constraint.
//  7. ReadJob(...).InputDeadlineAtUtc names the same instant before and after the repair, up to
//     truncation to the second (SQLite rounds to milliseconds first, so |delta| < 1 s).
//  8. The repair leaves PRAGMA user_version at RunLogSchema.LocalDatabaseSchemaVersion.
static void TestSealJobDeadlineFormat()
{
    var canonical = new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$");
    var resealUtc = new DateTimeOffset(2026, 8, 3, 1, 0, 0, TimeSpan.Zero).AddTicks(4_567_891);
    var resealOffset = new DateTimeOffset(2026, 8, 3, 9, 30, 0, TimeSpan.FromHours(8)).AddTicks(
        1_234_567
    );

    // 3: verify SQLite against the exact strings .NET writes, not an assumption about them.
    using (var memory = new SqliteConnection("Data Source=:memory:"))
    {
        memory.Open();
        Assert(
            Scalar(memory, $"SELECT datetime('{resealUtc:o}');") == "2026-08-03 01:00:00",
            $"datetime() must normalize '{resealUtc:o}'."
        );
        Assert(
            Scalar(memory, $"SELECT datetime('{resealOffset:o}');") == "2026-08-03 01:30:00",
            $"datetime() must convert '{resealOffset:o}' to UTC."
        );
    }

    WithStore(
        (store, connection) =>
        {
            // 1 and 2: a datetime() job late in the day and reseal jobs early in the same day.
            InsertRun(connection, "late", "ranked", "Online", completed: true, screenshot: false);
            Execute(
                connection,
                "UPDATE runs SET ended_at_utc = '2026-08-03T22:58:00Z' WHERE run_id = 'late';"
            );
            foreach (var runId in new[] { "reseal-utc", "reseal-offset" })
            {
                InsertRun(
                    connection,
                    runId,
                    "ranked",
                    "Online",
                    completed: true,
                    screenshot: false
                );
                InsertOutbox(
                    connection,
                    "bundle-" + runId,
                    runId,
                    runId + ".bundle",
                    "pending",
                    Now().AddDays(-1),
                    null
                );
            }
            store.EnsureEligibleJobs(TimeSpan.FromMinutes(2));
            store.FailOutboxAndScheduleReseal(
                "bundle-reseal-utc",
                "reseal-utc",
                "invalid",
                resealUtc
            );
            store.FailOutboxAndScheduleReseal(
                "bundle-reseal-offset",
                "reseal-offset",
                "invalid",
                resealOffset
            );

            var waiting = store.ListWaitingRunIds();
            Assert(
                waiting.SequenceEqual(["reseal-utc", "reseal-offset", "late"]),
                $"Waiting jobs must sort by deadline instant; got [{string.Join(", ", waiting)}]."
            );
            Assert(
                Deadline(connection, "late") == "2026-08-03 23:00:00",
                "EnsureEligibleJobs writes the datetime() shape."
            );
            Assert(
                Deadline(connection, "reseal-utc") == "2026-08-03 01:00:00",
                $"A reseal deadline is stored in the datetime() shape; got '{Deadline(connection, "reseal-utc")}'."
            );
            Assert(
                Deadline(connection, "reseal-offset") == "2026-08-03 01:30:00",
                $"A reseal deadline with an offset is stored as UTC; got '{Deadline(connection, "reseal-offset")}'."
            );
            // 7 (writer): the stored reseal deadline is `now` truncated to the second.
            Assert(
                store.ReadJob("reseal-utc")!.InputDeadlineAtUtc
                    == new DateTimeOffset(2026, 8, 3, 1, 0, 0, TimeSpan.Zero),
                "A reseal deadline keeps its instant, truncated to the second."
            );
        }
    );

    WithStore(
        (store, connection) =>
        {
            var database = connection.DataSource;
            // Counts every rewrite of the column, so "zero rows changed" is measured, not inferred.
            Execute(
                connection,
                """
                CREATE TABLE test_deadline_rewrites (run_id TEXT NOT NULL);
                CREATE TRIGGER test_count_deadline_rewrites
                AFTER UPDATE OF input_deadline_at_utc ON bundle_seal_jobs
                BEGIN
                    INSERT INTO test_deadline_rewrites (run_id) VALUES (NEW.run_id);
                END;
                """
            );
            var seeded = new (string RunId, string Deadline)[]
            {
                ("legacy-utc", "2026-08-03T04:15:30.4567891+00:00"),
                ("legacy-offset", "2026-08-03T12:15:30.9000000+08:00"),
                ("legacy-zulu", "2026-08-03T04:20:00Z"),
                ("canonical", "2026-08-03 04:10:00"),
                ("unparseable", "2026-08-03Tnot-a-time"),
            };
            foreach (var (runId, deadline) in seeded)
            {
                InsertRun(
                    connection,
                    runId,
                    "ranked",
                    "Online",
                    completed: true,
                    screenshot: false
                );
                Execute(
                    connection,
                    "INSERT INTO bundle_seal_jobs (run_id, state, screenshot_requested, screenshot_state, input_deadline_at_utc) VALUES ($runId, 'waiting', 0, 'not_requested', $deadline);",
                    ("$runId", runId),
                    ("$deadline", deadline)
                );
            }
            var canonicalHexBefore = Scalar(
                connection,
                "SELECT hex(input_deadline_at_utc) FROM bundle_seal_jobs WHERE run_id = 'canonical';"
            );
            var instantsBefore = new[] { "legacy-utc", "legacy-offset", "legacy-zulu", "canonical" }
                .Select(runId => (RunId: runId, Instant: store.ReadJob(runId)!.InputDeadlineAtUtc))
                .ToArray();

            // 6: the open must succeed despite the unparseable row.
            var reopened = new BundleQueueStore(database);

            // 4: ISO rows rewritten, the canonical row byte-identical, the unparseable row kept.
            Assert(
                Deadline(connection, "legacy-utc") == "2026-08-03 04:15:30",
                $"An ISO deadline is repaired on open; got '{Deadline(connection, "legacy-utc")}'."
            );
            Assert(
                Deadline(connection, "legacy-offset") == "2026-08-03 04:15:30",
                "An ISO deadline with an offset is repaired to UTC."
            );
            Assert(
                Deadline(connection, "legacy-zulu") == "2026-08-03 04:20:00",
                "A Z-suffixed ISO deadline is repaired."
            );
            Assert(
                Scalar(
                    connection,
                    "SELECT hex(input_deadline_at_utc) FROM bundle_seal_jobs WHERE run_id = 'canonical';"
                ) == canonicalHexBefore,
                "A canonical deadline stays byte-identical."
            );
            Assert(
                Deadline(connection, "unparseable") == "2026-08-03Tnot-a-time",
                "A value datetime() cannot parse is left alone."
            );
            var rewritten = Scalar(
                connection,
                "SELECT group_concat(run_id, ',') FROM (SELECT run_id FROM test_deadline_rewrites ORDER BY run_id);"
            );
            Assert(
                rewritten == "legacy-offset,legacy-utc,legacy-zulu",
                $"Only ISO-shaped parseable rows are rewritten; got '{rewritten}'."
            );

            // 7: the same instant, truncated to the second.
            foreach (var (runId, before) in instantsBefore)
            {
                var after = reopened.ReadJob(runId)!.InputDeadlineAtUtc;
                Assert(
                    after.Offset == TimeSpan.Zero
                        && (before - after).Duration() < TimeSpan.FromSeconds(1),
                    $"{runId}: repaired deadline {after:o} must name the instant {before:o}."
                );
                Assert(
                    canonical.IsMatch(Deadline(connection, runId)!),
                    $"{runId}: repaired deadline must have the datetime() shape."
                );
            }
            Assert(
                reopened.ReadJob("legacy-utc")!.InputDeadlineAtUtc
                    == new DateTimeOffset(2026, 8, 3, 4, 15, 30, TimeSpan.Zero),
                "A fractional second away from the boundary truncates to the whole second."
            );

            // 5: a second open rewrites nothing.
            _ = new BundleQueueStore(database);
            Assert(
                Scalar(connection, "SELECT COUNT(*) || '' FROM test_deadline_rewrites;") == "3",
                "The repair is idempotent: a second open rewrites zero rows."
            );

            // 8: the repair never bumps the schema version.
            using var version = connection.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            Assert(
                Convert.ToInt32(version.ExecuteScalar()) == RunLogSchema.LocalDatabaseSchemaVersion,
                "The deadline repair must not change user_version."
            );
        }
    );
}

static string? Deadline(SqliteConnection connection, string runId)
{
    using var command = connection.CreateCommand();
    command.CommandText =
        "SELECT input_deadline_at_utc FROM bundle_seal_jobs WHERE run_id = $runId;";
    command.Parameters.AddWithValue("$runId", runId);
    return command.ExecuteScalar() as string;
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
