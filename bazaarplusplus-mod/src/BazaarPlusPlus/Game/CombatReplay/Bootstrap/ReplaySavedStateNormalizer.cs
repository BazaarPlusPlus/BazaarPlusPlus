#nullable enable

using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarPlusPlus.Game.PvpBattles;
using TheBazaar;

namespace BazaarPlusPlus.Game.CombatReplay.Bootstrap;

internal static class ReplaySavedStateNormalizer
{
    internal static void Normalize(PvpBattleManifest? manifest, CombatSequenceMessages? sequence)
    {
        NormalizeMessage(sequence?.SpawnMessage, manifest);
        NormalizeMessage(sequence?.DespawnMessage, manifest);
    }

    private static void NormalizeMessage(NetMessageGameSim? message, PvpBattleManifest? manifest)
    {
        var data = message?.Data;
        if (data == null)
            return;

        data.Run ??= new SimUpdateRun();
        data.Run.Day = ReplaySavedStateRules.ResolvePositiveUInt(data.Run.Day, manifest?.Day);
        data.Run.Hour = ReplaySavedStateRules.ResolvePositiveUInt(data.Run.Hour, manifest?.Hour);

        data.Player ??= new SimUpdatePlayer { CombatantId = ECombatantId.Player };
        NormalizeLevel(data.Player, manifest?.Participants?.PlayerLevel);

        data.Opponent ??= new SimUpdatePlayer { CombatantId = ECombatantId.Opponent };
        NormalizeLevel(data.Opponent, manifest?.Participants?.OpponentLevel);
    }

    private static void NormalizeLevel(SimUpdatePlayer player, int? manifestLevel)
    {
        player.Attributes ??= new Dictionary<EPlayerAttributeType, int>();
        player.Attributes.TryGetValue(EPlayerAttributeType.Level, out var rawLevel);
        player.Attributes[EPlayerAttributeType.Level] = ReplaySavedStateRules.ResolveLevel(
            rawLevel,
            manifestLevel
        );
    }
}
