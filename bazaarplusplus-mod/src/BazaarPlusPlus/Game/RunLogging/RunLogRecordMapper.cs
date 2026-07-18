#nullable enable
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Storage.RunLog;

namespace BazaarPlusPlus.Game.RunLogging;

internal static class RunLogRecordMapper
{
    public static bool TryCreateRunLogCreateRequest(
        RunBasicsSnapshot? basics,
        RankSnapshot? rank,
        string? serverRunId,
        string? buildChannel,
        out RunLogCreateRequest request
    )
    {
        request = null!;
        if (basics == null || basics.Hero == null || string.IsNullOrWhiteSpace(serverRunId))
            return false;

        request = new RunLogCreateRequest
        {
            SchemaVersion = RunLogSchema.RowSchemaVersion,
            RunId = serverRunId,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Hero = basics.Hero,
            GameMode = basics.GameMode!,
            PlayerRank = rank?.Rank,
            PlayerRating = rank?.Rating,
            Day = basics.Day,
            Hour = basics.Hour,
            BuildChannel = buildChannel,
        };
        return true;
    }

    public static RunLogCompletion BuildRunLogCompletion(
        string reason,
        RunExitKind lastExitKind,
        RunBasicsSnapshot? basics,
        PlayerStatsSnapshot? stats,
        RankSnapshot? rank
    )
    {
        return new RunLogCompletion
        {
            SchemaVersion = RunLogSchema.RowSchemaVersion,
            Status = lastExitKind == RunExitKind.Interrupted ? "abandoned" : "completed",
            EndedAtUtc = DateTimeOffset.UtcNow,
            FinalDay = basics?.Day,
            FinalHour = basics?.Hour,
            MaxHealth = stats?.MaxHealth,
            Prestige = stats?.Prestige,
            Level = stats?.Level,
            Income = stats?.Income,
            Gold = stats?.Gold,
            Victories = basics?.Victories,
            Losses = basics?.Losses,
            FinalPlayerRank = rank?.Rank,
            FinalPlayerRating = rank?.Rating,
            Reason = reason,
        };
    }

    public static RunLogAbandonment BuildRunLogAbandonment(string reason, RunBasicsSnapshot? basics)
    {
        return new RunLogAbandonment
        {
            SchemaVersion = RunLogSchema.RowSchemaVersion,
            Status = "abandoned",
            EndedAtUtc = DateTimeOffset.UtcNow,
            FinalDay = basics?.Day,
            FinalHour = basics?.Hour,
            Reason = reason,
        };
    }
}
