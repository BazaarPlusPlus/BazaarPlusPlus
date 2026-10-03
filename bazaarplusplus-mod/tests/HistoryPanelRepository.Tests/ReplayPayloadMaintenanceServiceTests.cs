#nullable enable
using System.Diagnostics;
using System.Runtime.Versioning;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

internal static class ReplayPayloadMaintenanceServiceTests
{
    internal static void Run()
    {
        TenThousandPayloadsUseBulkStorageAndLinearWork();
        TenThousandPayloadsUseBoundedRealSqliteCommands();
        MissingPayloadsDoNotConsumeNewestRetentionSlots();
        FailedFileDeleteRemainsPendingUntilAReentrantRetry();
        MaintenanceFlightsAreSingleAndCancelable();
    }

    private static void MissingPayloadsDoNotConsumeNewestRetentionSlots()
    {
        var now = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);
        var inventory = Enumerable
            .Range(0, 201)
            .Select(index => new ReplayPayloadMaintenanceRecord(
                $"retention-{index:D3}",
                now.AddDays(-31).AddMinutes(-index),
                HasLocalPayload: true,
                ReplayPayloadState.Ready
            ))
            .ToList();
        var catalog = new RecordingMaintenanceCatalog(inventory);
        using var files = new PayloadDirectory(inventory.Skip(1).Select(record => record.BattleId));
        using var operationGate = new ReplayPayloadOperationGate();

        var result = new ReplayPayloadMaintenanceService(
            catalog,
            files.Store,
            operationGate,
            () => Array.Empty<string>()
        ).Run(now, CancellationToken.None);

        Equal(1, result.MissingPayloadCount, "missing payload count");
        Equal(200, result.EvaluatedPayloadCount, "on-disk retention population");
        Equal(0, result.ScheduledDeleteCount, "missing row does not evict the oldest on-disk row");
        Equal(200, files.Count(), "every on-disk payload survives");
        Equal(
            ReplayPayloadState.Missing,
            catalog.Inventory.Single(record => record.BattleId == "retention-000").PayloadState,
            "missing lifecycle state"
        );
    }

    private static void TenThousandPayloadsUseBoundedRealSqliteCommands()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bpp-replay-sqlite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "run.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                RunLogSchema.EnsureInitialized(connection);
                using var command = connection.CreateCommand();
                command.CommandText = """
                    WITH RECURSIVE sequence(value) AS (
                        VALUES(0)
                        UNION ALL
                        SELECT value + 1 FROM sequence WHERE value < 9999
                    )
                    INSERT INTO battles (
                        battle_id, source, recorded_at_utc, combat_kind,
                        has_local_payload, local_payload_state
                    )
                    SELECT printf('sqlite-%05d', value), 'LOCAL',
                           '2025-01-01T00:00:00Z', 'PVPCombat', 1, 'ready'
                    FROM sequence;
                    """;
                command.ExecuteNonQuery();
            }

            var diagnostics = new List<ReplayPayloadMaintenanceStorageEvent>();
            var catalog = new PvpBattleSqliteStore(databasePath, diagnostics.Add);
            using var files = new PayloadDirectory(
                Enumerable.Range(0, 10_000).Select(index => $"sqlite-{index:D5}")
            );
            using var operationGate = new ReplayPayloadOperationGate();
            var result = new ReplayPayloadMaintenanceService(
                catalog,
                files.Store,
                operationGate,
                () => Array.Empty<string>()
            ).Run(new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

            Equal(9_800, result.ScheduledDeleteCount, "real SQLite scheduled payload count");
            Equal(200, files.Count(), "real SQLite retained payload files");
            Equal(
                3,
                diagnostics.Count(item =>
                    item == ReplayPayloadMaintenanceStorageEvent.ConnectionOpened
                ),
                "real SQLite maintenance connection count"
            );
            Equal(
                4,
                diagnostics.Count(item =>
                    item == ReplayPayloadMaintenanceStorageEvent.CommandExecuted
                ),
                "real SQLite maintenance command count"
            );
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void MaintenanceFlightsAreSingleAndCancelable()
    {
        using var entered = new ManualResetEventSlim();
        using var flight = new ReplayMaintenanceFlight();
        var runCount = 0;
        Task Work(CancellationToken cancellationToken) =>
            Task.Run(
                () =>
                {
                    Interlocked.Increment(ref runCount);
                    entered.Set();
                    cancellationToken.WaitHandle.WaitOne();
                    cancellationToken.ThrowIfCancellationRequested();
                },
                cancellationToken
            );

        var first = flight.Start(Work);
        var second = flight.Start(Work);
        Assert(
            ReferenceEquals(first, second),
            "Concurrent maintenance requests must share a flight."
        );
        Assert(entered.Wait(TimeSpan.FromSeconds(5)), "Maintenance flight did not start.");
        Equal(1, runCount, "single flight invocation count");
        flight.Dispose();
        try
        {
            first.GetAwaiter().GetResult();
            throw new InvalidOperationException("Canceled maintenance unexpectedly completed.");
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }
    }

    private static void TenThousandPayloadsUseBulkStorageAndLinearWork()
    {
        var now = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);
        var inventory = Enumerable
            .Range(0, 10_000)
            .Select(index => new ReplayPayloadMaintenanceRecord(
                $"battle-{index:D5}",
                now.AddDays(-31).AddMinutes(-index),
                HasLocalPayload: true,
                ReplayPayloadState.Ready
            ))
            .ToList();
        var catalog = new RecordingMaintenanceCatalog(inventory);
        using var files = new PayloadDirectory(inventory.Select(record => record.BattleId));
        using var operationGate = new ReplayPayloadOperationGate();
        var service = new ReplayPayloadMaintenanceService(
            catalog,
            files.Store,
            operationGate,
            () => Array.Empty<string>()
        );

        var stopwatch = Stopwatch.StartNew();
        var result = service.Run(now, CancellationToken.None);
        stopwatch.Stop();

        Equal(10_000, result.EvaluatedPayloadCount, "evaluated payload count");
        Equal(9_800, result.ScheduledDeleteCount, "retention candidate count");
        Equal(9_800, result.DeletedPayloadCount, "deleted payload count");
        Equal(200, files.Count(), "retained payload files");
        Assert(files.Contains("battle-00000"), "The newest payload file must survive.");
        Assert(!files.Contains("battle-09999"), "The oldest payload file must be deleted.");
        Equal(1, catalog.InventoryCallCount, "inventory query count");
        Equal(1, catalog.ScheduleCallCount, "schedule query count");
        Equal(1, catalog.CompleteCallCount, "completion query count");
        Assert(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"10,000 synthetic payload maintenance took {stopwatch.Elapsed}."
        );
        Assert(
            result.WorkUnits <= 30_000,
            $"Expected linear work, got {result.WorkUnits} work units."
        );
    }

    private static void FailedFileDeleteRemainsPendingUntilAReentrantRetry()
    {
        var now = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);
        var inventory = Enumerable
            .Range(0, 201)
            .Select(index => new ReplayPayloadMaintenanceRecord(
                $"retry-{index:D3}",
                now.AddDays(-31).AddMinutes(-index),
                HasLocalPayload: true,
                ReplayPayloadState.Ready
            ))
            .ToList();
        var catalog = new RecordingMaintenanceCatalog(inventory);
        using var files = new PayloadDirectory(inventory.Select(record => record.BattleId));
        using var operationGate = new ReplayPayloadOperationGate();
        var service = new ReplayPayloadMaintenanceService(
            catalog,
            files.Store,
            operationGate,
            () => Array.Empty<string>()
        );

        ReplayPayloadMaintenanceResult first;
        using (files.DenyDelete("retry-200"))
            first = service.Run(now, CancellationToken.None);
        Equal(1, first.FailedDeleteCount, "first failed delete count");
        Assert(files.Contains("retry-200"), "A failed delete must leave the payload file.");
        Equal(
            ReplayPayloadState.DeletePending,
            catalog.Inventory.Single(record => record.BattleId == "retry-200").PayloadState,
            "durable retry state"
        );

        var second = service.Run(now.AddMinutes(1), CancellationToken.None);
        Equal(1, second.DeletedPayloadCount, "retry deleted count");
        Equal(
            ReplayPayloadState.Evicted,
            catalog.Inventory.Single(record => record.BattleId == "retry-200").PayloadState,
            "retry terminal state"
        );
        Assert(!files.Contains("retry-200"), "The retried delete must remove the payload file.");
    }

    private sealed class RecordingMaintenanceCatalog(
        IReadOnlyList<ReplayPayloadMaintenanceRecord> inventory
    ) : IReplayPayloadMaintenanceCatalog
    {
        private readonly List<ReplayPayloadMaintenanceRecord> _inventory = inventory.ToList();

        internal IReadOnlyList<ReplayPayloadMaintenanceRecord> Inventory => _inventory;
        internal int InventoryCallCount { get; private set; }
        internal int ScheduleCallCount { get; private set; }
        internal int CompleteCallCount { get; private set; }

        public IReadOnlyList<ReplayPayloadMaintenanceRecord> ListReplayMaintenanceInventory()
        {
            InventoryCallCount++;
            return _inventory.ToList();
        }

        public IReadOnlyList<string> ScheduleReplayPayloadDeletion(
            IReadOnlyCollection<string> battleIds,
            DateTimeOffset now
        )
        {
            ScheduleCallCount++;
            var set = new HashSet<string>(battleIds, StringComparer.Ordinal);
            var scheduled = _inventory
                .Where(record =>
                    set.Contains(record.BattleId) && record.PayloadState == ReplayPayloadState.Ready
                )
                .Select(record => record.BattleId)
                .ToList();
            Replace(
                scheduled,
                record =>
                    record with
                    {
                        HasLocalPayload = false,
                        PayloadState = ReplayPayloadState.DeletePending,
                    }
            );
            return scheduled;
        }

        public void CompleteReplayPayloadDeletion(
            IReadOnlyCollection<string> battleIds,
            DateTimeOffset now
        )
        {
            CompleteCallCount++;
            Replace(battleIds, record => record with { PayloadState = ReplayPayloadState.Evicted });
        }

        public void MarkReplayPayloadMissing(
            IReadOnlyCollection<string> battleIds,
            DateTimeOffset now
        )
        {
            Replace(
                battleIds,
                record =>
                    record with
                    {
                        HasLocalPayload = false,
                        PayloadState = ReplayPayloadState.Missing,
                    }
            );
        }

        private void Replace(
            IEnumerable<string> battleIds,
            Func<ReplayPayloadMaintenanceRecord, ReplayPayloadMaintenanceRecord> update
        )
        {
            var set = new HashSet<string>(battleIds, StringComparer.Ordinal);
            for (var index = 0; index < _inventory.Count; index++)
            {
                if (set.Contains(_inventory[index].BattleId))
                    _inventory[index] = update(_inventory[index]);
            }
        }
    }

    // A real CombatReplayPayloadStore over a temporary directory. Payload files are empty:
    // maintenance only lists and deletes by name. DenyDelete makes File.Delete throw the way
    // the platform does: a read-only directory on Unix, a read-only file on Windows.
    private sealed class PayloadDirectory : IDisposable
    {
        private const string FileSuffix = ".payload.mpack.gz";
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"bpp-replay-payloads-{Guid.NewGuid():N}"
        );

        internal PayloadDirectory(IEnumerable<string> battleIds)
        {
            Directory.CreateDirectory(_root);
            foreach (var battleId in battleIds)
                File.WriteAllBytes(PathFor(battleId), []);
            Store = new CombatReplayPayloadStore(_root);
        }

        internal CombatReplayPayloadStore Store { get; }

        internal bool Contains(string battleId) => File.Exists(PathFor(battleId));

        internal int Count() => Directory.GetFiles(_root, $"*{FileSuffix}").Length;

        internal IDisposable DenyDelete(string battleId)
        {
            if (OperatingSystem.IsWindows())
            {
                var path = PathFor(battleId);
                File.SetAttributes(path, FileAttributes.ReadOnly);
                return new Restore(() => File.SetAttributes(path, FileAttributes.Normal));
            }

            return DenyDirectoryDeletes(_root);
        }

        [UnsupportedOSPlatform("windows")]
        private static IDisposable DenyDirectoryDeletes(string root)
        {
            var mode = File.GetUnixFileMode(root);
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            return new Restore(() => File.SetUnixFileMode(root, mode));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private string PathFor(string battleId) => Path.Combine(_root, battleId + FileSuffix);

        private sealed class Restore(Action restore) : IDisposable
        {
            public void Dispose() => restore();
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                $"Expected {label} to be '{expected}', got '{actual}'."
            );
    }
}
