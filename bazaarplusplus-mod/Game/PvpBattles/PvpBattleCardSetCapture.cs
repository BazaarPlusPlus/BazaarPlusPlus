#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.Game.CombatReplay;

namespace BazaarPlusPlus.Game.PvpBattles;

internal sealed class PvpBattleCardSetCapture
{
    public IList<CombatReplayCardSnapshot> Items { get; set; } =
        new List<CombatReplayCardSnapshot>();

    public PvpBattleCaptureStatus Status { get; set; }

    public PvpBattleCaptureSource Source { get; set; }
}
