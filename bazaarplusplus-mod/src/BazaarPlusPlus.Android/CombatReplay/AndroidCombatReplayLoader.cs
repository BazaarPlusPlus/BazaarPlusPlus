using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Game.PvpBattles;
using Il2CppBazaarGameShared.Infra.Messages;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.CombatReplay;

/// <summary>
/// A Replay Payload's three messages back as the game's combat sequence, decoded by the game's
/// own serializer (<see cref="AndroidReplayMessages.Deserialize{T}"/>), as the desktop's
/// <c>CombatReplayLoader</c> decodes them with the Mono one. Main thread only.
/// </summary>
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
