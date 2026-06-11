#nullable enable
using BazaarGameShared.Infra.Messages;

namespace BazaarPlusPlus.Game.PvpBattles;

internal sealed class PvpBattleSequenceWindow
{
    public string? RunId { get; set; }

    public NetMessageGameSim? SpawnMessage { get; set; }

    public NetMessageCombatSim? CombatMessage { get; set; }

    public NetMessageGameSim? DespawnMessage { get; set; }
}
