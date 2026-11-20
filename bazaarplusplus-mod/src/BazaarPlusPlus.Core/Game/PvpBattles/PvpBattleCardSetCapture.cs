#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles;

public sealed class PvpBattleCardSetCapture
{
    public IList<PvpBattleCardSnapshot> Items { get; set; } = new List<PvpBattleCardSnapshot>();

    public PvpBattleCaptureStatus Status { get; set; }

    public PvpBattleCaptureSource Source { get; set; }
}
