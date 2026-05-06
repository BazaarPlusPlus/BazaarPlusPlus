#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles;

public sealed class PvpBattleOutcome
{
    public string? Result { get; set; }

    public string? WinnerCombatantId { get; set; }

    public string? LoserCombatantId { get; set; }
}
