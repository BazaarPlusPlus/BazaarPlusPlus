#nullable enable
using BazaarPlusPlus.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Storage.RunLog;

/// <summary>
/// The reads Core's Bundle composer seals a Run from: the ended run's facts and its events, as
/// stored. Mapping them onto the Run segment stays with the composer; the SQL stays here.
/// </summary>
public sealed class RunLogBundleSource : SqliteStoreBase
{
    public RunLogBundleSource(string databasePath, SqliteOpenOptions options)
        : base(databasePath, options ?? throw new ArgumentNullException(nameof(options))) { }

    /// <summary>The run's row once it is no longer active, or null when there is none.</summary>
    public RunLogEndedRun? TryReadEndedRun(string runId)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT hero, game_mode, started_at_ms, ended_at_ms, status,
                   COALESCE(final_day, day) AS day, COALESCE(final_hour, hour) AS hour,
                   victories, losses, player_rank, player_rating, final_player_rank,
                   final_player_rating, final_player_rating_delta, max_health, prestige, level,
                   income, gold, build_channel, mod_version
            FROM {RunLogSchema.RunsTableName}
            WHERE run_id = $runId AND status <> 'active'
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        return new RunLogEndedRun
        {
            Hero = reader.GetString(reader.GetOrdinal("hero")),
            GameMode = reader.GetString(reader.GetOrdinal("game_mode")),
            StartedAtMs = reader.GetInt64(reader.GetOrdinal("started_at_ms")),
            EndedAtMs = GetNullableInt64(reader, "ended_at_ms"),
            Status = reader.GetString(reader.GetOrdinal("status")),
            Day = GetNullableInt32(reader, "day"),
            Hour = GetNullableInt32(reader, "hour"),
            Victories = GetNullableInt32(reader, "victories"),
            Losses = GetNullableInt32(reader, "losses"),
            PlayerRank = GetNullableString(reader, "player_rank"),
            PlayerRating = GetNullableInt32(reader, "player_rating"),
            FinalPlayerRank = GetNullableString(reader, "final_player_rank"),
            FinalPlayerRating = GetNullableInt32(reader, "final_player_rating"),
            FinalPlayerRatingDelta = GetNullableInt32(reader, "final_player_rating_delta"),
            MaxHealth = GetNullableInt32(reader, "max_health"),
            Prestige = GetNullableInt32(reader, "prestige"),
            Level = GetNullableInt32(reader, "level"),
            Income = GetNullableInt32(reader, "income"),
            Gold = GetNullableInt32(reader, "gold"),
            BuildChannel = GetNullableString(reader, "build_channel"),
            ModVersion = GetNullableString(reader, "mod_version"),
        };
    }

    /// <summary>The run's events in <c>seq</c> order, each with its stored payload JSON.</summary>
    public IReadOnlyList<RunLogStoredEvent> ReadEvents(string runId)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT seq, ts_at_ms, kind, payload_json
            FROM {RunLogSchema.RunEventsTableName}
            WHERE run_id = $runId
            ORDER BY seq ASC;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        using var reader = command.ExecuteReader();
        var events = new List<RunLogStoredEvent>();
        while (reader.Read())
            events.Add(Event(reader));
        return events;
    }

    private static RunLogStoredEvent Event(SqliteDataReader reader) =>
        new()
        {
            Seq = reader.GetInt64(0),
            TimestampMs = reader.GetInt64(1),
            Kind = reader.GetString(2),
            PayloadJson = reader.GetString(3),
        };
}

/// <summary>One ended run's row, with the final day and hour in place of the last seen ones.</summary>
public sealed class RunLogEndedRun
{
    public string Hero { get; set; } = string.Empty;

    public string GameMode { get; set; } = string.Empty;

    public long StartedAtMs { get; set; }

    public long? EndedAtMs { get; set; }

    public string Status { get; set; } = string.Empty;

    public int? Day { get; set; }

    public int? Hour { get; set; }

    public int? Victories { get; set; }

    public int? Losses { get; set; }

    public string? PlayerRank { get; set; }

    public int? PlayerRating { get; set; }

    public string? FinalPlayerRank { get; set; }

    public int? FinalPlayerRating { get; set; }

    public int? FinalPlayerRatingDelta { get; set; }

    public int? MaxHealth { get; set; }

    public int? Prestige { get; set; }

    public int? Level { get; set; }

    public int? Income { get; set; }

    public int? Gold { get; set; }

    public string? BuildChannel { get; set; }

    public string? ModVersion { get; set; }
}

/// <summary>One <c>run_events</c> row; <see cref="PayloadJson"/> is the stored JSON, verbatim.</summary>
public sealed class RunLogStoredEvent
{
    public long Seq { get; set; }

    public long TimestampMs { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;
}
