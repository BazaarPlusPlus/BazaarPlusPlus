#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus.Game.PvpBattles.Persistence;

/// <summary>
/// The desktop's PvP battle catalog: maps manifests onto the records
/// <see cref="PvpBattleSqliteStore"/> stores, each board capture as the JSON of Core's
/// <see cref="PvpBattleCardSetDocument"/> (<see cref="PvpBattleSnapshotsCodec"/>). Storage owns
/// the SQL.
/// </summary>
internal sealed class PvpBattleCatalog : IPvpBattleCatalog
{
    private readonly PvpBattleSqliteStore _store;

    public PvpBattleCatalog(
        string databasePath,
        Action<ReplayPayloadMaintenanceStorageEvent>? maintenanceDiagnostics = null
    )
    {
        _store = new PvpBattleSqliteStore(
            databasePath,
            SqliteOpenOptions.Default,
            maintenanceDiagnostics
        );
    }

    public void Save(PvpBattleManifest manifest)
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));
        _store.Save(
            new PvpBattleRecord
            {
                BattleId = manifest.BattleId,
                RunId = manifest.RunId,
                RecordedAtUtc = manifest.RecordedAtUtc,
                CombatKind = manifest.CombatKind,
                Day = manifest.Day,
                Hour = manifest.Hour,
                EncounterId = manifest.EncounterId,
                Participants = manifest.Participants,
                Outcome = manifest.Outcome,
                Snapshots = PvpBattleSnapshotsCodec.ToDocuments(manifest.Snapshots),
            }
        );
    }

    public void AttachToRun(string battleId, string runId) => _store.AttachToRun(battleId, runId);

    public PvpBattleManifest? TryLoad(string battleId) =>
        _store.TryLoad(battleId) is { } record ? ToManifest(record) : null;

    public IReadOnlyList<PvpBattleManifest> ListByRunId(string runId) =>
        _store.ListByRunId(runId).Select(ToManifest).ToList();

    public IReadOnlyList<ReplayPayloadMaintenanceRecord> ListReplayMaintenanceInventory() =>
        _store.ListReplayMaintenanceInventory();

    public IReadOnlyList<string> ScheduleReplayPayloadDeletion(
        IReadOnlyCollection<string> battleIds,
        DateTimeOffset now
    ) => _store.ScheduleReplayPayloadDeletion(battleIds, now);

    public void CompleteReplayPayloadDeletion(
        IReadOnlyCollection<string> battleIds,
        DateTimeOffset now
    ) => _store.CompleteReplayPayloadDeletion(battleIds, now);

    public void MarkReplayPayloadMissing(
        IReadOnlyCollection<string> battleIds,
        DateTimeOffset now
    ) => _store.MarkReplayPayloadMissing(battleIds, now);

    /// <summary>The facts Run Logging attaches to the run, for <see cref="PvpBattleRecorded"/>.</summary>
    public static PvpBattleRecorded Recorded(PvpBattleManifest manifest) =>
        new()
        {
            BattleId = manifest.BattleId,
            RunId = manifest.RunId,
            CombatKind = manifest.CombatKind,
            Day = manifest.Day,
            Hour = manifest.Hour,
            EncounterId = manifest.EncounterId,
            OpponentName = manifest.Participants.OpponentName,
        };

    private static PvpBattleManifest ToManifest(PvpBattleRecord record) =>
        new()
        {
            BattleId = record.BattleId,
            RunId = record.RunId,
            RecordedAtUtc = record.RecordedAtUtc,
            CombatKind = record.CombatKind,
            Day = record.Day,
            Hour = record.Hour,
            EncounterId = record.EncounterId,
            Participants = record.Participants,
            Outcome = record.Outcome,
            Snapshots = record.Snapshots is { } snapshots
                ? PvpBattleSnapshotsCodec.FromDocuments(snapshots)
                : new PvpBattleSnapshots(),
        };
}
