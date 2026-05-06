#nullable enable
using System;
using BazaarGameShared;
using BazaarGameShared.Infra.Messages;
using MessagePack;

namespace BazaarPlusPlus.Game.PvpBattles;

internal sealed class PvpReplayPayloadFactory
{
    public PvpReplayPayload Create(string battleId, PvpBattleSequenceWindow window)
    {
        return new PvpReplayPayload
        {
            BattleId = battleId,
            Version = 1,
            SpawnMessageBytes = SerializeMessage(
                window.SpawnMessage
                    ?? throw new InvalidOperationException("Spawn message is required.")
            ),
            CombatMessageBytes = SerializeMessage(
                window.CombatMessage
                    ?? throw new InvalidOperationException("Combat message is required.")
            ),
            DespawnMessageBytes = SerializeMessage(
                window.DespawnMessage
                    ?? throw new InvalidOperationException("Despawn message is required.")
            ),
        };
    }

    private static byte[] SerializeMessage<TMessage>(TMessage message)
        where TMessage : INetMessage
    {
        return MessagePackSerializer.Serialize(message, MessagePackConfig.Options);
    }
}
