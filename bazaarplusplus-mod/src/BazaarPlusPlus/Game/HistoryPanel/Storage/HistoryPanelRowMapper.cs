#nullable enable
using System;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.PvpBattles;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.HistoryPanel.Storage;

// Pure SQLite reader → typed record translation. No SQL, no UI, no formatting; just
// column reads and the projection rules tied to the schema (e.g. final_* fallback to day/hour).
internal static class HistoryPanelRowMapper
{
    public static HistoryRunRecord ReadRun(SqliteDataReader reader)
    {
        var startedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("started_at_utc")));
        var endedAt = GetNullableDateTimeOffset(reader, "ended_at_utc");
        var finalDay = GetNullableInt32(reader, "final_day") ?? GetNullableInt32(reader, "day");
        var finalHour = GetNullableInt32(reader, "final_hour") ?? GetNullableInt32(reader, "hour");
        var lastSeen =
            endedAt ?? GetNullableDateTimeOffset(reader, "last_seen_at_utc") ?? startedAt;
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
            GetNullableString(reader, "player_rank"),
            GetNullableInt32(reader, "player_rating"),
            GetNullableInt32(reader, "victories"),
            GetNullableInt32(reader, "losses"),
            rawStatus,
            reader.GetInt32(reader.GetOrdinal("battle_count"))
        );
    }

    public static HistoryBattleRecord ReadLocalBattle(
        SqliteDataReader reader,
        string battleId,
        PvpBattleSnapshots snapshots
    )
    {
        return new HistoryBattleRecord(
            battleId,
            reader.GetString(reader.GetOrdinal("run_id")),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("recorded_at_utc"))),
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
            HistoryBattlePreviewProjection.CountSnapshots(
                snapshots.PlayerHand,
                snapshots.PlayerSkills,
                snapshots.OpponentHand,
                snapshots.OpponentSkills
            ),
            snapshots,
            isFinalBattle: false,
            source: HistoryBattleSource.Local,
            replayAvailable: true,
            replayDownloaded: true
        );
    }

    public static HistoryBattleRecord ReadGhostBattle(SqliteDataReader reader)
    {
        var battleId = SafeGetNullableString(reader, "battle_id") ?? "unknown";
        return GhostBattleLocalProjector.CreateHistoryBattleRecord(
            battleId,
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("recorded_at_utc"))),
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
            isFinalBattle: GetNullableInt32(reader, "is_final_battle") == 1,
            replayAvailable: GetNullableInt32(reader, "replay_available") == 1,
            replayDownloaded: GetNullableInt32(reader, "replay_downloaded") == 1
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
        return reader.IsDBNull(ordinal) ? null : DateTimeOffset.Parse(reader.GetString(ordinal));
    }
}
