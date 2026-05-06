#nullable enable
using System;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class GhostBattleLocalProjector
{
    public static HistoryBattleRecord CreateHistoryBattleRecord(
        string battleId,
        DateTimeOffset recordedAtUtc,
        int? day,
        int? hour,
        string? encounterId,
        string? rawPlayerName,
        string? rawPlayerAccountId,
        string? rawPlayerHero,
        string? rawPlayerRank,
        int? rawPlayerRating,
        int? rawPlayerLevel,
        string? rawOpponentHero,
        string? rawOpponentRank,
        int? rawOpponentRating,
        int? rawOpponentLevel,
        string? combatKind,
        string? rawResult,
        string? rawWinnerCombatantId,
        string? rawLoserCombatantId,
        bool isBundleFinalBattle,
        bool replayAvailable,
        bool replayDownloaded
    )
    {
        return new HistoryBattleRecord(
            battleId,
            string.Empty,
            recordedAtUtc,
            day,
            hour,
            encounterId,
            rawOpponentHero,
            rawOpponentRank,
            rawOpponentRating,
            rawOpponentLevel,
            rawPlayerName,
            rawPlayerHero,
            rawPlayerRank,
            rawPlayerRating,
            rawPlayerLevel,
            rawPlayerAccountId,
            combatKind,
            ProjectResultToLocal(rawResult),
            ProjectCombatantIdToLocal(rawWinnerCombatantId),
            ProjectCombatantIdToLocal(rawLoserCombatantId),
            string.Empty,
            HistoryPanelRepository.BuildEmptyPreviewData(),
            isBundleFinalBattle,
            source: HistoryBattleSource.Ghost,
            replayAvailable,
            replayDownloaded
        );
    }

    internal static string? ProjectResultToLocal(string? rawResult)
    {
        if (string.IsNullOrWhiteSpace(rawResult))
            return rawResult;

        var trimmed = rawResult.Trim();
        if (
            string.Equals(trimmed, "Win", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "Won", StringComparison.OrdinalIgnoreCase)
        )
            return "Lost";

        if (
            string.Equals(trimmed, "Loss", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "Lost", StringComparison.OrdinalIgnoreCase)
        )
            return "Won";

        return trimmed;
    }

    internal static string? ProjectCombatantIdToLocal(string? rawCombatantId)
    {
        if (string.IsNullOrWhiteSpace(rawCombatantId))
            return rawCombatantId;

        return rawCombatantId.Trim() switch
        {
            "Player" => "Opponent",
            "Opponent" => "Player",
            _ => rawCombatantId,
        };
    }
}
