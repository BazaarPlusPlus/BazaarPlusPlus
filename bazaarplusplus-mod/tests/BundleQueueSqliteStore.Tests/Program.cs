#nullable enable
using BazaarPlusPlus.Storage.BundleQueue;
using Microsoft.Data.Sqlite;

TestEligibilityAllocationPublishAndDueOrdering();
TestOutcomeResealAndCleanupQueries();
TestFinalOutboxEndsEligibility();
TestSealFailureBookkeeping();
TestSealEligibilityFixture();
TestSealJobDeadlinesAreInstants();

Console.WriteLine("Bundle queue SQLite store checks passed.");

static void TestEligibilityAllocationPublishAndDueOrdering()
{
    WithStore(
        (store, connection) =>
        {
            InsertRun(connection, "ranked", "ranked", "Online", completed: true, screenshot: true);
            InsertRun(connection, "ptr", "ranked", "Ptr", completed: true, screenshot: false);
            InsertRun(connection, "casual", "casual", "Online", completed: true, screenshot: false);
            store.EnsureEligibleJobs(TimeSpan.FromMinutes(2));

            var ids = store.ListWaitingJobs().Select(job => job.RunId).ToList();
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
            var due = store.ListDue(Now(), 3).Single();
            Assert(
                due.BundleId == "bundle-a" && due.Attempts == 0,
                "Pending outbox should be due, with no attempts yet."
            );

            store.RecordTransient("bundle-a", "busy", null, Now(), Now().AddMinutes(3));
            Assert(
                store.ListDue(Now().AddMinutes(2), 3).Count == 0,
                "Transient delay must postpone due work."
            );
            Assert(
                store.ListDue(Now().AddMinutes(3), 3).Single().Attempts == 1,
                "Due time boundary is inclusive, and the row carries its recorded attempts."
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
            Assert(
                !store.FailOutboxAndScheduleReseal(
                    "bundle-a",
                    "run-a",
                    "pending_file_invalid",
                    Now()
                ),
                "A stale validation must not report a recreated job."
            );
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
            Assert(
                store.FailOutboxAndScheduleReseal("bundle-b", "run-b", "invalid", Now()),
                "A recreated job is reported only after committing it."
            );
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
                store.ListWaitingJobs().Select(job => job.RunId).SequenceEqual(["retry"]),
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
                    && store.ListWaitingJobs().Count == 0,
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
            // History's delete refusal reads the same column (BundleQueueStore.ProtectedRunCondition).
            foreach (var eligibilityCase in cases)
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    $"SELECT EXISTS (SELECT 1 FROM runs AS r WHERE r.run_id = $runId AND {BundleQueueStore.ProtectedRunCondition("r")});";
                command.Parameters.AddWithValue("$runId", eligibilityCase.Name);
                var isProtected = Convert.ToInt64(command.ExecuteScalar()) != 0;
                Assert(
                    isProtected == eligibilityCase.ProtectedFromCleanup,
                    $"{eligibilityCase.Name}: expected protectedFromCleanup={eligibilityCase.ProtectedFromCleanup}, ProtectedRunCondition={isProtected}."
                );
            }
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

// bundle_seal_jobs.input_deadline_at_ms is an instant in Unix milliseconds, so ListWaitingJobs
// and idx_bundle_seal_jobs_state order deadlines by instant whatever offset `now` carried.
static void TestSealJobDeadlinesAreInstants()
{
    var resealUtc = new DateTimeOffset(2026, 8, 3, 1, 0, 0, TimeSpan.Zero).AddTicks(4_567_891);
    var resealOffset = new DateTimeOffset(2026, 8, 3, 9, 30, 0, TimeSpan.FromHours(8));

    WithStore(
        (store, connection) =>
        {
            InsertRun(connection, "late", "ranked", "Online", completed: true, screenshot: false);
            Execute(
                connection,
                "UPDATE runs SET ended_at_ms = $endedAt WHERE run_id = 'late';",
                ("$endedAt", DateTimeOffset.Parse("2026-08-03T22:58:00Z").ToUnixTimeMilliseconds())
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

            var waiting = store.ListWaitingJobs().Select(job => job.RunId).ToList();
            Assert(
                waiting.SequenceEqual(["reseal-utc", "reseal-offset", "late"]),
                $"Waiting jobs must sort by deadline instant; got [{string.Join(", ", waiting)}]."
            );
            Assert(
                store.ReadJob("late")!.InputDeadlineAtUtc
                    == DateTimeOffset.Parse("2026-08-03T23:00:00Z"),
                "EnsureEligibleJobs adds the convergence window to the end time."
            );
            Assert(
                store.ReadJob("reseal-utc")!.InputDeadlineAtUtc
                    == DateTimeOffset.Parse("2026-08-03T01:00:00.456Z"),
                "A reseal deadline keeps its instant to the millisecond."
            );
            Assert(
                store.ReadJob("reseal-offset")!.InputDeadlineAtUtc == resealOffset,
                "A reseal deadline with an offset names the same instant."
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
        "INSERT INTO runs (run_id, started_at_ms, last_seen_at_ms, ended_at_ms, status, hero, game_mode, build_channel, bundle_screenshot_requested) VALUES ($runId, $now, $now, $now, $status, 'Vanessa', $mode, $channel, $screenshot);",
        ("$runId", runId),
        ("$now", DateTimeOffset.Parse("2026-08-03T05:00:00Z").ToUnixTimeMilliseconds()),
        ("$status", completed ? "completed" : "active"),
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
        "INSERT INTO bundle_outbox (bundle_id, run_id, file_name, content_sha256_hex, content_digest, total_bytes, has_screenshot, sealed_at_ms, status, next_attempt_at_ms, failed_at_ms) VALUES ($bundleId, $runId, $fileName, 'sha', 'digest', 10, 0, $sealedAt, $status, $sealedAt, $failedAt);",
        ("$bundleId", bundleId),
        ("$runId", runId),
        ("$fileName", fileName),
        ("$sealedAt", sealedAt.ToUnixTimeMilliseconds()),
        ("$status", status),
        ("$failedAt", failedAt?.ToUnixTimeMilliseconds())
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
