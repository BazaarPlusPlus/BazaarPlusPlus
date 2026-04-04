#nullable enable
using BazaarGameShared.Domain.Runs;
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Game.CombatReplay;

namespace BazaarPlusPlus.Game.PvpBattles;

internal sealed class PvpBattleSequenceMatcher
{
    public bool IsPvpCombatOpeningMessage(NetMessageGameSim message)
    {
        var state = message.Data.CurrentState?.StateName;
        return state == ERunState.PVPCombat;
    }

    public bool IsAnyCombatOpeningMessage(NetMessageGameSim message)
    {
        var state = message.Data.CurrentState?.StateName;
        return state == ERunState.Combat || state == ERunState.PVPCombat;
    }

    public CombatReplaySequenceCandidate ResetCandidate()
    {
        return new CombatReplaySequenceCandidate();
    }

    public PvpBattleSequenceWindow CreateCompletedWindow(
        CombatReplaySequenceCandidate candidate,
        NetMessageGameSim despawnMessage,
        string? runId
    )
    {
        return new PvpBattleSequenceWindow
        {
            RunId = runId ?? candidate.RunId,
            SpawnMessage = candidate.SpawnMessage,
            CombatMessage = candidate.CombatMessage,
            DespawnMessage = despawnMessage,
        };
    }
}
