#nullable enable
using System;
using BazaarGameShared.Infra.Messages;

namespace BazaarPlusPlus.Game.PvpBattles;

internal sealed class PvpBattleManifestFactory
{
    public string CreateBattleId()
    {
        return Guid.NewGuid().ToString("N");
    }

    public PvpBattleManifest Create(
        string battleId,
        PvpBattleSequenceWindow window,
        DateTimeOffset savedAtUtc,
        PvpBattleParticipants participants,
        PvpBattleOutcome outcome,
        PvpBattleSnapshots snapshots
    )
    {
        var spawnMessage =
            window.SpawnMessage
            ?? throw new InvalidOperationException("Spawn message is required.");
        var combatMessage =
            window.CombatMessage
            ?? throw new InvalidOperationException("Combat message is required.");

        return new PvpBattleManifest
        {
            BattleId = battleId,
            RunId = window.RunId,
            RecordedAtUtc = savedAtUtc,
            CombatKind = spawnMessage.Data.CurrentState?.StateName.ToString(),
            Day = unchecked((int)spawnMessage.Data.Run.Day),
            Hour = unchecked((int)spawnMessage.Data.Run.Hour),
            EncounterId = spawnMessage.Data.CurrentState?.CurrentEncounterId,
            Participants = participants,
            Outcome = outcome,
            Snapshots = snapshots,
        };
    }
}
