#nullable enable
namespace BazaarPlusPlus.GameInterop;

/// <summary>
/// The public game-interop surface the BazaarAgent host uses to drive external battle video
/// recording. Signatures carry only primitives (byte[]/string/enums) — GameInterop must not
/// import Game.* — and the real decode/guard/runtime work happens in composition-root delegates.
/// Published via <see cref="BazaarAgentGameBridge.CurrentRecorder"/>. Main thread only.
/// </summary>
public interface IBazaarAgentReplayRecorder
{
    /// <summary>Decodes a GhostBattlePayload msgpack+gzip blob, runs the recording guards, and
    /// boots replay playback with video recording enabled. <paramref name="expectedBattleId"/>
    /// (from the request header/query), when present, is cross-checked against the payload.</summary>
    BppReplayControlResult TryStartRecord(
        string requestId,
        byte[] ghostBattlePayloadBytes,
        string? expectedBattleId
    );

    /// <summary>Drives the replay "continue" button (<c>ReplayState.Exit()</c>), which finalizes
    /// the recording. Rejected unless the replay has finished and awaits continue.</summary>
    BppReplayControlResult TryContinueReplay();

    /// <summary>Reads the current replay playback phase and the active battle id.</summary>
    BppReplayPhaseSnapshot GetReplayPhase();
}

public enum BppReplayPhase
{
    None,
    Starting,
    Playing,
    FinishedAwaitingContinue,
}

public readonly struct BppReplayPhaseSnapshot
{
    public BppReplayPhaseSnapshot(BppReplayPhase phase, string? battleId)
    {
        Phase = phase;
        BattleId = battleId;
    }

    public BppReplayPhase Phase { get; }
    public string? BattleId { get; }
}

public enum BppReplayControlStatus
{
    /// <summary>The command reached the game (recording started / continue triggered).</summary>
    Accepted,

    /// <summary>The payload bytes are unusable: empty, decode failure, or battleId mismatch.</summary>
    InvalidPayload,

    /// <summary>The game state refuses the command (replay/recording guards, wrong phase).</summary>
    Rejected,

    /// <summary>The combat-replay runtime is not available yet (or was torn down).</summary>
    Unavailable,
}

public readonly struct BppReplayControlResult
{
    public BppReplayControlResult(
        BppReplayControlStatus status,
        string? failureReason,
        string? battleId
    )
    {
        Status = status;
        FailureReason = failureReason;
        BattleId = battleId;
    }

    public BppReplayControlStatus Status { get; }
    public string? FailureReason { get; }
    public string? BattleId { get; }

    public static BppReplayControlResult Accepted(string? battleId) =>
        new(BppReplayControlStatus.Accepted, null, battleId);

    public static BppReplayControlResult Invalid(string reason) =>
        new(BppReplayControlStatus.InvalidPayload, reason, null);

    public static BppReplayControlResult Rejected(string reason) =>
        new(BppReplayControlStatus.Rejected, reason, null);

    public static BppReplayControlResult Unavailable(string reason) =>
        new(BppReplayControlStatus.Unavailable, reason, null);
}
