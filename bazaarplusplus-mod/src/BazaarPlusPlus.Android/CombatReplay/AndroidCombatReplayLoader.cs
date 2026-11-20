using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Game.PvpBattles;
using Il2CppBazaarGameShared.Infra.Messages;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.CombatReplay;

internal static class AndroidCombatReplayLoader
{
    public static CombatSequenceMessages Load(PvpReplayPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new CombatSequenceMessages(
            AndroidReplayMessages.Deserialize<NetMessageGameSim>(
                payload.SpawnMessageBytes,
                "spawn"
            ),
            AndroidReplayMessages.Deserialize<NetMessageGameSim>(
                payload.DespawnMessageBytes,
                "despawn"
            ),
            AndroidReplayMessages.Deserialize<NetMessageCombatSim>(
                payload.CombatMessageBytes,
                "combat"
            )
        );
    }
}
