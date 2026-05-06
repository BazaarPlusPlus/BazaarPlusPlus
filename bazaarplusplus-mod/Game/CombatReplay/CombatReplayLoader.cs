#nullable enable
using System;
using BazaarGameShared;
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Game.PvpBattles;
using MessagePack;
using TheBazaar;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayLoader
{
    public CombatSequenceMessages Load(PvpReplayPayload payload)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        return new CombatSequenceMessages(
            DeserializeGameSim(payload.SpawnMessageBytes),
            DeserializeGameSim(payload.DespawnMessageBytes),
            DeserializeCombatSim(payload.CombatMessageBytes)
        );
    }

    private static NetMessageGameSim DeserializeGameSim(byte[] payload)
    {
        return MessagePackSerializer.Deserialize<NetMessageGameSim>(
            payload,
            MessagePackConfig.Options
        );
    }

    private static NetMessageCombatSim DeserializeCombatSim(byte[] payload)
    {
        return MessagePackSerializer.Deserialize<NetMessageCombatSim>(
            payload,
            MessagePackConfig.Options
        );
    }
}
