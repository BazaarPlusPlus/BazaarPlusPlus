#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles;

internal sealed class PvpBattleParticipants
{
    public string? PlayerName { get; set; }

    public string? PlayerAccountId { get; set; }

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
}
