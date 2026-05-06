#nullable enable
using System;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryBattleRecord
{
    public HistoryBattleRecord(
        string battleId,
        string runId,
        DateTimeOffset recordedAtUtc,
        int? day,
        int? hour,
        string? encounterId,
        string? playerHero,
        string? playerRank,
        int? playerRating,
        int? playerLevel,
        string? opponentName,
        string? opponentHero,
        string? opponentRank,
        int? opponentRating,
        int? opponentLevel,
        string? opponentAccountId,
        string? combatKind,
        string? result,
        string? winnerCombatantId,
        string? loserCombatantId,
        string snapshotSummary,
        HistoryBattlePreviewData previewData,
        bool isBundleFinalBattle,
        HistoryBattleSource source,
        bool replayAvailable,
        bool replayDownloaded
    )
    {
        BattleId = battleId;
        RunId = runId;
        RecordedAtUtc = recordedAtUtc;
        Day = day;
        Hour = hour;
        EncounterId = encounterId;
        PlayerHero = playerHero;
        PlayerRank = playerRank;
        PlayerRating = playerRating;
        PlayerLevel = playerLevel;
        OpponentName = opponentName;
        OpponentHero = opponentHero;
        OpponentRank = opponentRank;
        OpponentRating = opponentRating;
        OpponentLevel = opponentLevel;
        OpponentAccountId = opponentAccountId;
        CombatKind = combatKind;
        Result = result;
        WinnerCombatantId = winnerCombatantId;
        LoserCombatantId = loserCombatantId;
        SnapshotSummary = snapshotSummary;
        PreviewData = previewData;
        IsBundleFinalBattle = isBundleFinalBattle;
        Source = source;
        ReplayAvailable = replayAvailable;
        ReplayDownloaded = replayDownloaded;
    }

    public string BattleId { get; }

    public string RunId { get; }

    public DateTimeOffset RecordedAtUtc { get; }

    public int? Day { get; }

    public int? Hour { get; }

    public string? EncounterId { get; }

    public string? PlayerHero { get; }

    public string? PlayerRank { get; }

    public int? PlayerRating { get; }

    public int? PlayerLevel { get; }

    public string? OpponentName { get; }

    public string? OpponentHero { get; }

    public string? OpponentRank { get; }

    public int? OpponentRating { get; }

    public int? OpponentLevel { get; }

    public string? OpponentAccountId { get; }

    public string? CombatKind { get; }

    public string? Result { get; }

    public string? WinnerCombatantId { get; }

    public string? LoserCombatantId { get; }

    public string SnapshotSummary { get; }

    public HistoryBattlePreviewData PreviewData { get; }

    public bool IsBundleFinalBattle { get; }

    public HistoryBattleSource Source { get; }

    public bool ReplayAvailable { get; }

    public bool ReplayDownloaded { get; }
}

internal enum HistoryBattleSource
{
    Local,
    Ghost,
}
