#nullable enable
using System;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryRunRecord
{
    public HistoryRunRecord(
        string runId,
        string hero,
        string gameMode,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        DateTimeOffset lastSeenAtUtc,
        int? finalDay,
        int? finalHour,
        int? maxHealth,
        int? prestige,
        int? level,
        int? income,
        int? gold,
        string? playerRank,
        int? playerRating,
        int? victories,
        int? losses,
        string rawStatus,
        int battleCount
    )
    {
        RunId = runId;
        Hero = hero;
        GameMode = gameMode;
        StartedAtUtc = startedAtUtc;
        EndedAtUtc = endedAtUtc;
        LastSeenAtUtc = lastSeenAtUtc;
        FinalDay = finalDay;
        FinalHour = finalHour;
        MaxHealth = maxHealth;
        Prestige = prestige;
        Level = level;
        Income = income;
        Gold = gold;
        PlayerRank = playerRank;
        PlayerRating = playerRating;
        Victories = victories;
        Losses = losses;
        RawStatus = rawStatus;
        BattleCount = battleCount;
    }

    public string RunId { get; }

    public string Hero { get; }

    public string GameMode { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset? EndedAtUtc { get; }

    public DateTimeOffset LastSeenAtUtc { get; }

    public int? FinalDay { get; }

    public int? FinalHour { get; }

    public int? MaxHealth { get; }

    public int? Prestige { get; }

    public int? Level { get; }

    public int? Income { get; }

    public int? Gold { get; }

    public string? PlayerRank { get; }

    public int? PlayerRating { get; }

    public int? Victories { get; }

    public int? Losses { get; }

    public string RawStatus { get; }

    public int BattleCount { get; }
}
