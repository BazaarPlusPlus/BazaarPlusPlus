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
            DeserializeGameSim(payload.SpawnMessageBase64),
            DeserializeGameSim(payload.DespawnMessageBase64),
            DeserializeCombatSim(payload.CombatMessageBase64)
        );
    }

    private static NetMessageGameSim DeserializeGameSim(string payload)
    {
        return MessagePackSerializer.Deserialize<NetMessageGameSim>(
            Convert.FromBase64String(payload),
            MessagePackConfig.Options
        );
    }

    private static NetMessageCombatSim DeserializeCombatSim(string payload)
    {
        return MessagePackSerializer.Deserialize<NetMessageCombatSim>(
            Convert.FromBase64String(payload),
            MessagePackConfig.Options
        );
    }
}
