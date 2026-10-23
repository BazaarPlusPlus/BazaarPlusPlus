#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.HistoryPanel.Storage;

/// <summary>
/// The <c>runs</c> columns a History Run row reads, which <see cref="HistoryPanelRowMapper.ReadRun"/>
/// maps. Both hosts' History lists select them, so a Run reads the same on desktop and Android.
/// </summary>
public static class HistoryRunColumns
{
    // Only a Run the mod could resume reads as active (the current Run): an active History-Only
    // Run, which the V5 import should already have closed, reads as abandoned.
    private static readonly string RunStatusColumn =
        $"CASE WHEN status = 'active' AND NOT {RunLogStore.ResumableRunCondition(RunLogSchema.RunsTableName)} "
        + "THEN 'abandoned' ELSE status END AS run_status";

    public static readonly string Select =
        "run_id, hero, game_mode, started_at_ms, last_seen_at_ms, player_rank, player_rating, "
        + RunStatusColumn
        + ", day, hour, final_day, final_hour, max_health AS final_max_health, "
        + "prestige AS final_prestige, level AS final_level, income AS final_income, gold AS final_gold, "
        + "victories, losses, ended_at_ms, "
        + "final_player_rank, final_player_rating, final_player_rating_delta";
}

// Pure SQLite reader → typed record translation. No SQL, no UI, no formatting; just
// column reads and the projection rules tied to the schema (e.g. final_* fallback to day/hour).
public static class HistoryPanelRowMapper
{
    public static HistoryRunRecord ReadRun(SqliteDataReader reader)
    {
        var startedAt = DateTimeOffset.FromUnixTimeMilliseconds(
            reader.GetInt64(reader.GetOrdinal("started_at_ms"))
        );
        var endedAt = GetNullableDateTimeOffset(reader, "ended_at_ms");
        var finalDay = GetNullableInt32(reader, "final_day") ?? GetNullableInt32(reader, "day");
        var finalHour = GetNullableInt32(reader, "final_hour") ?? GetNullableInt32(reader, "hour");
        var lastSeen = endedAt ?? GetNullableDateTimeOffset(reader, "last_seen_at_ms") ?? startedAt;
        var rawStatus = reader.GetString(reader.GetOrdinal("run_status"));

        return new HistoryRunRecord(
            reader.GetString(reader.GetOrdinal("run_id")),
            reader.GetString(reader.GetOrdinal("hero")),
            reader.GetString(reader.GetOrdinal("game_mode")),
            startedAt,
            endedAt,
            lastSeen,
            finalDay,
            finalHour,
            GetNullableInt32(reader, "final_max_health"),
            GetNullableInt32(reader, "final_prestige"),
            GetNullableInt32(reader, "final_level"),
            GetNullableInt32(reader, "final_income"),
            GetNullableInt32(reader, "final_gold"),
            GetNullableString(reader, "final_player_rank")
                ?? GetNullableString(reader, "player_rank"),
            GetNullableInt32(reader, "final_player_rating")
                ?? GetNullableInt32(reader, "player_rating"),
            GetNullableInt32(reader, "final_player_rating_delta"),
            GetNullableInt32(reader, "victories"),
            GetNullableInt32(reader, "losses"),
            rawStatus
        );
    }

    public static HistoryBattleRecord ReadLocalBattle(SqliteDataReader reader, string battleId) =>
        ReadLocalBattle(
            reader,
            battleId,
            ReplayAvailabilityCodec.FromLocalPayload(
                GetNullableString(reader, "local_payload_state")
            )
        );

    public static HistoryBattleRecord ReadGhostBattle(SqliteDataReader reader) =>
        ReadGhostBattle(
            reader,
            ReplayAvailabilityCodec.Parse(GetNullableString(reader, "ghost_replay_state"))
        );

    // A favorite row keeps the battles display columns under their names, so it reads through the
    // same projection as its origin; only replay availability comes from the favorite's own copy.
    public static HistoryFavoriteRecord ReadFavorite(SqliteDataReader reader)
    {
        var battleId = reader.GetString(reader.GetOrdinal("battle_id"));
        var replay = ReplayAvailabilityCodec.FromFavoritePayload(
            reader.GetString(reader.GetOrdinal("favorite_payload_state"))
        );
        var battle =
            reader.GetString(reader.GetOrdinal("source")) == "GHOST"
                ? ReadGhostBattle(reader, replay)
                : ReadLocalBattle(reader, battleId, replay);
        return new HistoryFavoriteRecord(
            battle,
            DateTimeOffset.FromUnixTimeMilliseconds(
                reader.GetInt64(reader.GetOrdinal("favorited_at_ms"))
            ),
            reader.GetString(reader.GetOrdinal("note"))
        );
    }

    private static HistoryBattleRecord ReadLocalBattle(
        SqliteDataReader reader,
        string battleId,
        ReplayAvailability replay
    )
    {
        return new HistoryBattleRecord(
            battleId,
            reader.GetString(reader.GetOrdinal("run_id")),
            DateTimeOffset.FromUnixTimeMilliseconds(
                reader.GetInt64(reader.GetOrdinal("recorded_at_ms"))
            ),
            GetNullableInt32(reader, "day"),
            GetNullableInt32(reader, "hour"),
            GetNullableString(reader, "encounter_id"),
            GetNullableString(reader, "player_hero"),
            GetNullableString(reader, "player_rank"),
            GetNullableInt32(reader, "player_rating"),
            GetNullableInt32(reader, "player_level"),
            GetNullableInt32(reader, "player_prestige"),
            GetNullableInt32(reader, "player_victories"),
            GetNullableString(reader, "opponent_name"),
            GetNullableString(reader, "opponent_hero"),
            GetNullableString(reader, "opponent_rank"),
            GetNullableInt32(reader, "opponent_rating"),
            GetNullableInt32(reader, "opponent_level"),
            GetNullableInt32(reader, "opponent_prestige"),
            GetNullableInt32(reader, "opponent_victories"),
            GetNullableString(reader, "opponent_account_id"),
            GetNullableString(reader, "combat_kind"),
            GetNullableString(reader, "result"),
            GetNullableString(reader, "winner_combatant_id"),
            GetNullableString(reader, "loser_combatant_id"),
            default,
            isFinalBattle: false,
            source: HistoryBattleSource.Local,
            replay
        );
    }

    private static HistoryBattleRecord ReadGhostBattle(
        SqliteDataReader reader,
        ReplayAvailability replay
    )
    {
        var battleId = SafeGetNullableString(reader, "battle_id") ?? "unknown";
        return GhostBattleLocalProjector.CreateHistoryBattleRecord(
            battleId,
            DateTimeOffset.FromUnixTimeMilliseconds(
                reader.GetInt64(reader.GetOrdinal("recorded_at_ms"))
            ),
            GetNullableInt32(reader, "day"),
            GetNullableInt32(reader, "hour"),
            GetNullableString(reader, "encounter_id"),
            GetNullableString(reader, "player_name"),
            GetNullableString(reader, "player_account_id"),
            GetNullableString(reader, "player_hero"),
            GetNullableString(reader, "player_rank"),
            GetNullableInt32(reader, "player_rating"),
            GetNullableInt32(reader, "player_level"),
            GetNullableInt32(reader, "player_prestige"),
            GetNullableInt32(reader, "player_victories"),
            GetNullableString(reader, "opponent_hero"),
            GetNullableString(reader, "opponent_rank"),
            GetNullableInt32(reader, "opponent_rating"),
            GetNullableInt32(reader, "opponent_level"),
            GetNullableInt32(reader, "opponent_prestige"),
            GetNullableInt32(reader, "opponent_victories"),
            GetNullableString(reader, "combat_kind"),
            GetNullableString(reader, "result"),
            GetNullableString(reader, "winner_combatant_id"),
            GetNullableString(reader, "loser_combatant_id"),
            ReadGhostSnapshotCounts(reader),
            isFinalBattle: GetNullableInt32(reader, "is_final_battle") == 1,
            replay
        );
    }

    private static HistoryBattleSnapshotCounts ReadGhostSnapshotCounts(SqliteDataReader reader)
    {
        var playerHand = GetNullableInt32(reader, "player_hand_item_count");
        var playerSkills = GetNullableInt32(reader, "player_skill_count");
        var opponentHand = GetNullableInt32(reader, "opponent_hand_item_count");
        var opponentSkills = GetNullableInt32(reader, "opponent_skill_count");
        if (
            !playerHand.HasValue
            || !playerSkills.HasValue
            || !opponentHand.HasValue
            || !opponentSkills.HasValue
        )
            return HistoryBattleSnapshotCounts.Unknown;

        return new HistoryBattleSnapshotCounts(
            playerHand.Value,
            playerSkills.Value,
            opponentHand.Value,
            opponentSkills.Value
        );
    }

    public static string? GetNullableString(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static string? SafeGetNullableString(SqliteDataReader reader, string columnName)
    {
        try
        {
            return GetNullableString(reader, columnName);
        }
        catch
        {
            return null;
        }
    }

    public static int? GetNullableInt32(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    public static DateTimeOffset? GetNullableDateTimeOffset(
        SqliteDataReader reader,
        string columnName
    )
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(ordinal));
    }
}
