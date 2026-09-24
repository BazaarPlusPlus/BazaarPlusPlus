#nullable enable
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

internal static class HistoryGhostRecoveryTests
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bpp-ghost-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            CheckRecovery(root);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    private static void CheckRecovery(string root)
    {
        var path = Path.Combine(root, "recovery.sqlite3");
        using var db = new SqliteConnection($"Data Source={path}");
        db.Open();
        RunLogSchema.EnsureInitialized(db);
        using var command = db.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE n(x) AS (VALUES(0) UNION ALL SELECT x+1 FROM n WHERE x<85)
            INSERT INTO battles (battle_id,source,remote_battle_id,uploader_account_id,local_player_account_id,bundle_id,recorded_at_utc,ghost_replay_state,deleted_at_utc,combat_kind)
            SELECT printf('recover%03d',x),'GHOST',printf('remote%03d',x),'uploader','account-a','bundle','2026-01-01T00:00:00Z','local_ready','2026-02-01T00:00:00Z','PVPCombat' FROM n;
            """;
        command.ExecuteNonQuery();
        var repository = new HistoryPanelRepository(path);
        var replay = Path.Combine(root, "Replays");
        var payloadPath = GhostBattlePayloadStore.ResolveDirectory(replay);
        var store = new GhostBattlePayloadStore(payloadPath);
        store.Save(Payload("recover000", "account-a"));
        store.Save(Payload("recover085", "account-a"));
        store.Save(Payload("recover001", "wrong-account"));
        File.WriteAllText(Path.Combine(payloadPath, "recover084.ghost.mpack.gz"), "invalid");
        var oversize = Path.Combine(payloadPath, "recover083.ghost.mpack.gz");
        using (var file = File.Create(oversize))
            file.SetLength(16 * 1024 * 1024 + 1);
        var result = store.LoadDetailed("recover083");
        Check(
            result.Status == FileBackedPayloadLoadStatus.Unreadable && File.Exists(oversize),
            "Compressed size rejection must retain the original file."
        );
        var data = new HistoryPanelDataService(repository, null, () => replay);
        Check(
            data.MaintainGhosts("account-b", CancellationToken.None) == 0,
            "Maintenance must isolate account identity."
        );
        Check(
            data.MaintainGhosts("account-a", CancellationToken.None) == 2,
            "Maintenance must advance past missing/corrupt files across all 40-row pages and restore only matching payloads."
        );
        Check(
            data.MaintainGhosts("account-a", CancellationToken.None) == 0,
            "Recovery must be idempotent."
        );
        Check(
            repository.ListGhostBattles("account-a", GhostBattleFilter.All, false, new()).Rows.Count
                == 2,
            "Recovered downloads must remain visible despite age."
        );
        Check(
            File.Exists(Path.Combine(payloadPath, "recover084.ghost.mpack.gz")),
            "Recovery must not delete corrupt files."
        );
        var large = Payload("expanded", "account-a");
        large.ReplayPayload.SpawnMessageBytes = new byte[65 * 1024 * 1024];
        var bytes = GhostBattlePayloadCodec.Serialize(large);
        Check(
            !GhostBattlePayloadCodec.TryDeserialize(bytes, out _, out var reason)
                && reason == "payload_too_large",
            "Decompression must enforce its byte limit before deserialization."
        );
        Console.WriteLine(
            "Ghost recovery: 86 hidden rows, 2 valid files across three pages; corrupt, missing, wrong-account and oversized files retained."
        );
    }

    private static GhostBattlePayload Payload(string id, string account) =>
        new()
        {
            BattleId = id,
            PerspectiveVersion = 1,
            BattleManifest = new PvpBattleManifest
            {
                BattleId = id,
                Participants = new() { PlayerAccountId = "uploader", OpponentAccountId = account },
            },
            ReplayPayload = new PvpReplayPayload
            {
                BattleId = id,
                SpawnMessageBytes = [1],
                CombatMessageBytes = [2],
            },
        };

    private static void Check(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }
}
