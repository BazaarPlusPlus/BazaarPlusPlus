#nullable enable
namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class PvpBattlePendingScreenshotContext
{
    public string BattleId { get; set; } = string.Empty;

    public string? RunId { get; set; }

    public bool Captured { get; set; }
}
