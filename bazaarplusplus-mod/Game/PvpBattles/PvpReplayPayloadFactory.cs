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
            SpawnMessageBase64 = SerializeMessage(
                window.SpawnMessage
                    ?? throw new InvalidOperationException("Spawn message is required.")
            ),
            CombatMessageBase64 = SerializeMessage(
                window.CombatMessage
                    ?? throw new InvalidOperationException("Combat message is required.")
            ),
            DespawnMessageBase64 = SerializeMessage(
                window.DespawnMessage
                    ?? throw new InvalidOperationException("Despawn message is required.")
            ),
        };
    }

    private static string SerializeMessage<TMessage>(TMessage message)
        where TMessage : INetMessage
    {
        var bytes = MessagePackSerializer.Serialize(message, MessagePackConfig.Options);
        return Convert.ToBase64String(bytes);
    }
}
