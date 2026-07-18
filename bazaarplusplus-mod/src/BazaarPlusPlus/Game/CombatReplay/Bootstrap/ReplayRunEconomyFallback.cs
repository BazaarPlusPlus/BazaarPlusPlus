#nullable enable

using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.CombatReplay.Bootstrap;

internal static class ReplayRunEconomyFallback
{
    internal static void ApplyMissingRunEconomy(
        PvpBattleManifest manifest,
        string? databasePath,
        IReplayPlaybackOutcomeSink? outcome = null
    )
    {
        if (manifest == null)
            return;
        if (
            manifest.Participants.PlayerIncome.HasValue && manifest.Participants.PlayerGold.HasValue
        )
            return;
        if (string.IsNullOrWhiteSpace(manifest.RunId))
            return;
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
            return;

        try
        {
            var (income, gold) = TryReadRunEconomy(databasePath, manifest.RunId);
            manifest.Participants.PlayerIncome ??= income;
            manifest.Participants.PlayerGold ??= gold;
        }
        catch (Exception ex)
        {
            outcome?.ReportDegradation(ReplayPlaybackReasonCode.PlayerAttributesUnavailable, ex);
        }
    }

    internal static (int? Income, int? Gold) TryReadRunEconomy(string databasePath, string runId)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || string.IsNullOrWhiteSpace(runId))
            return default;

        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()
        );
        connection.Open();
        RunLogSchema.EnsureInitialized(connection);

        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT income, gold
            FROM {RunLogSchema.RunsTableName}
            WHERE run_id = $runId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$runId", runId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return default;

        return (GetNullableInt32(reader, "income"), GetNullableInt32(reader, "gold"));
    }

    private static int? GetNullableInt32(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }
}
