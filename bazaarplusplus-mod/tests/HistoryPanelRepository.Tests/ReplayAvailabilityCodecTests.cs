#nullable enable
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

// Every Replay Availability must survive the schema's ghost_replay_state CHECK and read back
// unchanged through both Ghost read paths; a NULL state reads as Unavailable.
internal static class ReplayAvailabilityCodecTests
{
    internal static void Run(string root)
    {
        var path = Path.Combine(root, "replay-availability.sqlite3");
        var expected = new Dictionary<string, ReplayAvailability>();
        using (var db = new SqliteConnection($"Data Source={path}"))
        {
            db.Open();
            RunLogSchema.EnsureInitialized(db);
            foreach (var availability in Enum.GetValues<ReplayAvailability>())
            {
                var id = $"ghost-{availability}";
                Insert(db, id, ReplayAvailabilityCodec.ToStoredState(availability));
                expected[id] = availability;
            }
            Insert(db, "ghost-null", null);
            expected["ghost-null"] = ReplayAvailability.Unavailable;

            var rejected = false;
            try
            {
                Insert(db, "ghost-bogus", "downloaded");
            }
            catch (SqliteException)
            {
                rejected = true;
            }
            Check(rejected, "The schema must reject a state the codec does not own.");
        }

        var repository = new HistoryPanelRepository(path);
        var rows = repository
            .ListGhostBattles("account-local", GhostBattleFilter.All, false, new())
            .Rows.ToDictionary(row => row.BattleId);
        Check(rows.Count == expected.Count, "Every inserted Ghost row must be listed.");
        foreach (var (id, availability) in expected)
        {
            Check(
                rows[id].Replay == availability,
                $"{id} must list as {availability}, got {rows[id].Replay}."
            );
            Check(
                repository.TryGetGhostBundleReference(id)?.ReplayState == availability,
                $"{id} must resolve its bundle reference as {availability}."
            );
        }
    }

    private static void Insert(SqliteConnection db, string battleId, string? state)
    {
        using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO battles (
                battle_id, source, remote_battle_id, uploader_account_id, local_player_account_id,
                bundle_id, download_url, download_url_expires_at_ms, recorded_at_utc,
                ghost_replay_state, combat_kind
            ) VALUES (
                $battleId, 'GHOST', $battleId, 'uploader', 'account-local',
                'bundle', 'https://r2.example/bundle', 0, '2026-09-01T00:00:00Z',
                $state, 'PVPCombat'
            );
            """;
        command.Parameters.AddWithValue("$battleId", battleId);
        command.Parameters.AddWithValue("$state", (object?)state ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
