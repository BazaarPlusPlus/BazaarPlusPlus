#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles;

public sealed class PvpBattleSnapshots
{
    public PvpBattleCardSetCapture PlayerHand { get; set; } = new();

    public PvpBattleCardSetCapture PlayerSkills { get; set; } = new();

    public PvpBattleCardSetCapture OpponentHand { get; set; } = new();

    public PvpBattleCardSetCapture OpponentSkills { get; set; } = new();
}
