#nullable enable
namespace BazaarPlusPlus.GameInterop;

/// <summary>
/// BazaarPlusPlus-side implementation of <see cref="IBazaarAgentReplayRecorder"/>. Pure
/// delegate holder (mirrors <see cref="BazaarAgentGameProbe"/>): the composition root supplies
/// delegates that decode the payload, run the recording guards, and reach the internal
/// CombatReplayRuntime — keeping this layer free of Game.* imports.
/// </summary>
internal sealed class BazaarAgentReplayRecorder : IBazaarAgentReplayRecorder
{
    private readonly Func<string, byte[], string?, BppReplayControlResult> _tryStartRecord;
    private readonly Func<BppReplayControlResult> _tryContinueReplay;
    private readonly Func<BppReplayPhaseSnapshot> _getReplayPhase;

    public BazaarAgentReplayRecorder(
        Func<string, byte[], string?, BppReplayControlResult> tryStartRecord,
        Func<BppReplayControlResult> tryContinueReplay,
        Func<BppReplayPhaseSnapshot> getReplayPhase
    )
    {
        _tryStartRecord = tryStartRecord ?? throw new ArgumentNullException(nameof(tryStartRecord));
        _tryContinueReplay =
            tryContinueReplay ?? throw new ArgumentNullException(nameof(tryContinueReplay));
        _getReplayPhase = getReplayPhase ?? throw new ArgumentNullException(nameof(getReplayPhase));
    }

    public BppReplayControlResult TryStartRecord(
        string requestId,
        byte[] ghostBattlePayloadBytes,
        string? expectedBattleId
    ) => _tryStartRecord(requestId, ghostBattlePayloadBytes, expectedBattleId);

    public BppReplayControlResult TryContinueReplay() => _tryContinueReplay();

    public BppReplayPhaseSnapshot GetReplayPhase() => _getReplayPhase();
}
