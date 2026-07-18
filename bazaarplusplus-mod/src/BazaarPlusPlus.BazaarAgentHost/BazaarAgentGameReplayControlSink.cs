#nullable enable
using BazaarPlusPlus.BazaarAgent;
using BazaarPlusPlus.GameInterop;

namespace BazaarPlusPlus.BazaarAgentHost;

/// <summary>
/// Thin main-thread adapter from the pure-core replay control port onto the BazaarPlusPlus
/// replay recorder facade. Reads <see cref="BazaarAgentGameBridge.CurrentRecorder"/> lazily per
/// call so facade teardown maps to 503 instead of a stale reference. Contains no game logic and
/// must never exit ReplayState itself — that is the facade's continue path, and only ever in
/// response to an explicit continue command.
/// </summary>
internal sealed class BazaarAgentGameReplayControlSink : IBazaarAgentReplayControlSink
{
    public BazaarAgentReplayControlOutcome Start(
        string requestId,
        byte[] ghostBattlePayloadBytes,
        string? battleId
    )
    {
        var recorder = BazaarAgentGameBridge.CurrentRecorder;
        if (recorder is null)
            return Unavailable();

        return Map(recorder.TryStartRecord(requestId, ghostBattlePayloadBytes, battleId));
    }

    public BazaarAgentReplayControlOutcome Continue()
    {
        var recorder = BazaarAgentGameBridge.CurrentRecorder;
        if (recorder is null)
            return Unavailable();

        return Map(recorder.TryContinueReplay());
    }

    private static BazaarAgentReplayControlOutcome Unavailable() =>
        new(
            BazaarAgentReplayControlStatus.Unavailable,
            "Replay recorder facade is unavailable.",
            null
        );

    private static BazaarAgentReplayControlOutcome Map(BppReplayControlResult result)
    {
        var status = result.Status switch
        {
            BppReplayControlStatus.Accepted => BazaarAgentReplayControlStatus.Accepted,
            BppReplayControlStatus.InvalidPayload => BazaarAgentReplayControlStatus.InvalidPayload,
            BppReplayControlStatus.Rejected => BazaarAgentReplayControlStatus.Rejected,
            _ => BazaarAgentReplayControlStatus.Unavailable,
        };
        return new BazaarAgentReplayControlOutcome(status, result.FailureReason, result.BattleId);
    }
}
