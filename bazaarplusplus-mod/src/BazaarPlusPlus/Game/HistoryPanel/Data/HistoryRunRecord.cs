#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.Data;

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
        int? playerRatingDelta,
        int? victories,
        int? losses,
        string rawStatus
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
        PlayerRatingDelta = playerRatingDelta;
        Victories = victories;
        Losses = losses;
        RawStatus = rawStatus;
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

    // The rank and rating the Run ended on, or the ones it started on while it has no final
    // values (an active or abandoned Run).
    public string? PlayerRank { get; }

    public int? PlayerRating { get; }

    // The Run's rating change; null when either end of it is unknown.
    public int? PlayerRatingDelta { get; }

    public int? Victories { get; }

    public int? Losses { get; }

    public string RawStatus { get; }
}
