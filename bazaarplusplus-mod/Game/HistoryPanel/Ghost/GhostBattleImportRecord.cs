#nullable enable
using System;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

internal sealed class GhostBattleImportRecord
{
    public string BattleId { get; set; } = string.Empty;

    public DateTimeOffset RecordedAtUtc { get; set; }

    public int? Day { get; set; }

    public int? Hour { get; set; }

    public string? EncounterId { get; set; }

    public string? PlayerHero { get; set; }

    public string? PlayerRank { get; set; }

    public int? PlayerRating { get; set; }

    public int? PlayerLevel { get; set; }

    public string? OpponentName { get; set; }

    public string? OpponentHero { get; set; }

    public string? OpponentRank { get; set; }

    public int? OpponentRating { get; set; }

    public int? OpponentLevel { get; set; }

    public string? OpponentAccountId { get; set; }

    public string CombatKind { get; set; } = "PVPCombat";

    public string? Result { get; set; }

    public string? WinnerCombatantId { get; set; }

    public string? LoserCombatantId { get; set; }

    public bool ReplayAvailable { get; set; }

    public bool ReplayDownloaded { get; set; }

    public DateTimeOffset LastSyncedAtUtc { get; set; }
}
