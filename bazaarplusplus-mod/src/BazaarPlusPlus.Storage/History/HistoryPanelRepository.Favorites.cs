#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Storage.RunLog;

namespace BazaarPlusPlus.Game.HistoryPanel.Storage;

public sealed partial class HistoryPanelRepository
{
    // The battles columns a favorite copies under the same names; HistoryPanelRowMapper reads both.
    private const string FavoriteDisplayColumns =
        "run_id, local_player_account_id, uploader_account_id, remote_battle_id, recorded_at_ms, "
        + "day, hour, encounter_id, combat_kind, player_name, player_account_id, player_hero, "
        + "player_rank, player_rating, player_level, player_prestige, player_victories, "
        + "player_hand_item_count, player_skill_count, opponent_name, opponent_account_id, "
        + "opponent_hero, opponent_rank, opponent_rating, opponent_level, opponent_prestige, "
        + "opponent_victories, opponent_hand_item_count, opponent_skill_count, result, "
        + "winner_combatant_id, loser_combatant_id, is_final_battle";

    // The board snapshots stay a detail read (LoadFavoriteSnapshots), as battle_snapshots do.
    public HistoryCountedPage<HistoryFavoriteRecord> ListFavorites(HistoryPageRequest request) =>
        ReadCountedPage(
            HistoryPageQuery.Favorites(),
            "battle_id, source, favorited_at_ms, note, favorite_payload_state, "
                + FavoriteDisplayColumns,
            request,
            HistoryPanelRowMapper.ReadFavorite
        );

    public bool IsFavorite(string battleId)
    {
        if (!DatabaseExists || string.IsNullOrWhiteSpace(battleId))
            return false;
        using var connection = OpenConnection(ensureSchema: true);
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText =
            $"SELECT 1 FROM {RunLogSchema.FavoriteBattlesTableName} WHERE battle_id = $battleId;";
        command.Parameters.AddWithValue("$battleId", battleId);
        return command.ExecuteScalar() != null;
    }

    public HashSet<string> ListFavoriteBattleIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!DatabaseExists)
            return ids;
        using var connection = OpenConnection(ensureSchema: true);
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"SELECT battle_id FROM {RunLogSchema.FavoriteBattlesTableName};";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            ids.Add(reader.GetString(0));
        return ids;
    }

    // Copies the battle's display columns and board snapshots in one statement. Local snapshots
    // come from battle_snapshots; a Ghost battle has none there, so the caller passes the ones from
    // its payload manifest (recorder perspective, as stored for Ghost rows). Returns false when the
    // battle is already a favorite or no longer exists.
    public bool AddFavorite(
        string battleId,
        bool replayCopySaved,
        PvpBattleSnapshotDocuments? snapshots,
        DateTimeOffset favoritedAtUtc
    )
    {
        if (string.IsNullOrWhiteSpace(battleId))
            throw new ArgumentException("Battle id is required.", nameof(battleId));
        var sourceColumns = Qualified("b", FavoriteDisplayColumns);
        using var connection = OpenConnection(ensureSchema: true);
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            INSERT INTO {RunLogSchema.FavoriteBattlesTableName} (
                battle_id, source, favorited_at_ms, favorite_payload_state, {FavoriteDisplayColumns},
                player_hand_json, player_skills_json, opponent_hand_json, opponent_skills_json
            )
            SELECT b.battle_id, b.source, $favoritedAtMs, $payloadState, {sourceColumns},
                COALESCE($playerHandJson, s.player_hand_json),
                COALESCE($playerSkillsJson, s.player_skills_json),
                COALESCE($opponentHandJson, s.opponent_hand_json),
                COALESCE($opponentSkillsJson, s.opponent_skills_json)
            FROM {RunLogSchema.BattlesTableName} AS b
            LEFT JOIN {RunLogSchema.BattleSnapshotsTableName} AS s ON s.battle_id = b.battle_id
            WHERE b.battle_id = $battleId
            ON CONFLICT(battle_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$battleId", battleId);
        command.Parameters.AddWithValue("$favoritedAtMs", favoritedAtUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue(
            "$payloadState",
            ReplayAvailabilityCodec.ToFavoritePayloadState(replayCopySaved)
        );
        command.Parameters.AddWithValue("$playerHandJson", OrNull(snapshots?.PlayerHand));
        command.Parameters.AddWithValue("$playerSkillsJson", OrNull(snapshots?.PlayerSkills));
        command.Parameters.AddWithValue("$opponentHandJson", OrNull(snapshots?.OpponentHand));
        command.Parameters.AddWithValue("$opponentSkillsJson", OrNull(snapshots?.OpponentSkills));
        return command.ExecuteNonQuery() == 1;
    }

    public bool UpdateFavoriteNote(string battleId, string note, DateTimeOffset updatedAtUtc)
    {
        if (note.Length > RunLogSchema.FavoriteNoteMaxLength)
            throw new ArgumentOutOfRangeException(nameof(note), note.Length, null);
        using var connection = OpenConnection(ensureSchema: true);
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            UPDATE {RunLogSchema.FavoriteBattlesTableName}
            SET note = $note, note_updated_at_ms = $updatedAtMs
            WHERE battle_id = $battleId;
            """;
        command.Parameters.AddWithValue("$battleId", battleId);
        command.Parameters.AddWithValue("$note", note);
        command.Parameters.AddWithValue("$updatedAtMs", updatedAtUtc.ToUnixTimeMilliseconds());
        return command.ExecuteNonQuery() == 1;
    }

    public bool DeleteFavorite(string battleId)
    {
        if (!DatabaseExists)
            return false;
        using var connection = OpenConnection(ensureSchema: true);
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText =
            $"DELETE FROM {RunLogSchema.FavoriteBattlesTableName} WHERE battle_id = $battleId;";
        command.Parameters.AddWithValue("$battleId", battleId);
        return command.ExecuteNonQuery() == 1;
    }

    // Null when the favorite has no recorded boards (a Ghost favorite whose payload never loaded).
    // A later board the row left NULL comes back as an empty document, which reads as an empty
    // capture.
    public PvpBattleSnapshotDocuments? LoadFavoriteSnapshots(string battleId)
    {
        if (!DatabaseExists)
            return null;
        using var connection = OpenConnection(ensureSchema: true);
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT player_hand_json, player_skills_json, opponent_hand_json, opponent_skills_json
            FROM {RunLogSchema.FavoriteBattlesTableName}
            WHERE battle_id = $battleId;
            """;
        command.Parameters.AddWithValue("$battleId", battleId);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0))
            return null;
        return new PvpBattleSnapshotDocuments
        {
            PlayerHand = reader.GetString(0),
            PlayerSkills = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            OpponentHand = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            OpponentSkills = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
        };
    }

    private static object OrNull(string? json) => json == null ? DBNull.Value : json;

    private static string Qualified(string alias, string columns) =>
        string.Join(", ", columns.Split(',').Select(column => $"{alias}.{column.Trim()}"));
}
