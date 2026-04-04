#pragma warning disable CS0436
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Players;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.RunLogging.Models;
using TheBazaar;

namespace BazaarPlusPlus.Game.RunLogging;

internal static class RunLoggingGameDataReader
{
    public static bool TryCreateRunLogCreateRequest(out RunLogCreateRequest request)
    {
        request = null!;
        if (Data.Run?.Player == null)
            return false;

        var serverRunId = BppRuntimeHost.RunContext.CurrentServerRunId;
        if (string.IsNullOrWhiteSpace(serverRunId))
            return false;

        request = new RunLogCreateRequest
        {
            SchemaVersion = Persistence.Sqlite.RunLogSqliteSchema.RowSchemaVersion,
            RunId = serverRunId,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Hero = Data.Run.Player.Hero.ToString(),
            GameMode = Data.SelectedPlayMode.ToString(),
            PlayerRank = GetCurrentPlayerRank(),
            PlayerRating = GetCurrentPlayerRating(),
            Day = (int?)Data.Run.Day,
            Hour = GetCurrentRunHour(),
        };
        return true;
    }

    public static bool TryGetPlayerRankSnapshot(out string? rank, out int? rating)
    {
        try
        {
            return BppClientCacheBridge.TryGetPlayerRankSnapshot(out rank, out rating, out _);
        }
        catch
        {
            rank = null;
            rating = null;
            return false;
        }
    }

    public static bool TryBuildRunLogPlayerStats(out RunLogPlayerStatsSnapshot stats)
    {
        stats = null!;
        if (Data.Run?.Player == null)
            return false;

        stats = new RunLogPlayerStatsSnapshot
        {
            MaxHealth = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.HealthMax),
            Prestige = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Prestige),
            Level = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Level),
            Income = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Income),
            Gold = Data.Run.Player.GetAttributeValue(EPlayerAttributeType.Gold),
        };
        return true;
    }

    public static RunLogCompletion BuildRunLogCompletion(string reason)
    {
        var status =
            BppRuntimeHost.RunContext.LastRunExitKind == RunExitKind.Interrupted
                ? "abandoned"
                : "completed";
        TryBuildRunLogPlayerStats(out var stats);
        TryGetPlayerRankSnapshot(out var finalPlayerRank, out var finalPlayerRating);
        return new RunLogCompletion
        {
            SchemaVersion = Persistence.Sqlite.RunLogSqliteSchema.RowSchemaVersion,
            Status = status,
            EndedAtUtc = DateTimeOffset.UtcNow,
            FinalDay = Data.Run == null ? null : (int?)Data.Run.Day,
            FinalHour = GetCurrentRunHour(),
            MaxHealth = stats?.MaxHealth,
            Prestige = stats?.Prestige,
            Level = stats?.Level,
            Income = stats?.Income,
            Gold = stats?.Gold,
            Victories = Data.Run == null ? null : unchecked((int)Data.Run.Victories),
            Losses = Data.Run == null ? null : unchecked((int)Data.Run.Losses),
            FinalPlayerRank = finalPlayerRank,
            FinalPlayerRating = finalPlayerRating,
            Reason = reason,
        };
    }

    public static RunLogAbandonment BuildRunLogAbandonment(string reason)
    {
        return new RunLogAbandonment
        {
            SchemaVersion = Persistence.Sqlite.RunLogSqliteSchema.RowSchemaVersion,
            Status = "abandoned",
            EndedAtUtc = DateTimeOffset.UtcNow,
            FinalDay = Data.Run == null ? null : (int?)Data.Run.Day,
            FinalHour = GetCurrentRunHour(),
            Reason = reason,
        };
    }

    private static int? GetCurrentRunHour()
    {
        return Data.Run == null ? null : (int?)Data.Run.Hour;
    }

    private static string? GetCurrentPlayerRank()
    {
        return TryGetPlayerRankSnapshot(out var rank, out _) ? rank : null;
    }

    private static int? GetCurrentPlayerRating()
    {
        return TryGetPlayerRankSnapshot(out _, out var rating) ? rating : null;
    }
}
