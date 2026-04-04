#nullable enable
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Core.Events;

internal sealed class PvpBattleRecorded
{
    public PvpBattleManifest Manifest { get; set; } = null!;
}
