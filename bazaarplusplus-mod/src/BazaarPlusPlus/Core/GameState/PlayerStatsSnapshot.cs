#nullable enable
namespace BazaarPlusPlus.Core.GameState;

public sealed class PlayerStatsSnapshot
{
    public int? MaxHealth { get; set; }

    public int? Prestige { get; set; }

    public int? Level { get; set; }

    public int? Income { get; set; }

    public int? Gold { get; set; }
}
