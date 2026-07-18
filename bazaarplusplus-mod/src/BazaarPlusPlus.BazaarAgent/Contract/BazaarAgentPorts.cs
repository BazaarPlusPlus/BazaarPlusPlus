#nullable enable
namespace BazaarPlusPlus.BazaarAgent;

public static class BazaarAgentRuntimeDefaults
{
    public const int HttpListenerPort = 47900;
    public const int ActionTimeoutMilliseconds = 3000;
    public static readonly TimeSpan ActionMinDelay = TimeSpan.FromSeconds(1);

    // Replay control commands are accepted on the main thread inside one controller tick, but the
    // accept work (gzip+msgpack decode of a full GhostBattlePayload + CombatReplayLoader.Load) is
    // heavier than an action dispatch, and the main thread may be mid scene-load when the request
    // arrives. Wider than ActionTimeoutMilliseconds on purpose.
    public const int ReplayControlTimeoutMilliseconds = 10000;

    // Cap for POST /v1/replay/record bodies (raw GhostBattlePayload msgpack+gzip blob).
    // Provisional until a production p99 payload size is measured (design §11); typical gzipped
    // replay payloads are well under this. Oversized payloads should move to streaming-to-disk
    // rather than raising this further.
    public const int MaxRecordBodyBytes = 32 * 1024 * 1024;
}

public interface IBazaarAgentOptions
{
    string DecisionLogRoot { get; }
}

public interface IBazaarAgentContextReader
{
    BazaarAgentContext Build(double actionCooldownRemainingSeconds);
}

public interface IBazaarAgentActionDispatcher
{
    BazaarAgentDispatchResult Execute(
        BazaarAgentAction action,
        BazaarAgentContextSnapshot snapshot
    );
}

public enum BazaarAgentDispatchDiagnostic
{
    None,
    DispatcherException,
}

public readonly record struct BazaarAgentDispatchResult(
    bool Executed,
    string? Error,
    BazaarAgentDispatchDiagnostic Diagnostic = BazaarAgentDispatchDiagnostic.None,
    Exception? DiagnosticException = null
);

public enum BazaarAgentReplayControlKind
{
    Start,
    Continue,
}

/// <summary>One replay control command as carried through the HTTP→main-thread queue.</summary>
public readonly record struct BazaarAgentReplayCommand(
    BazaarAgentReplayControlKind Kind,
    byte[]? Payload,
    string? BattleId
);

public enum BazaarAgentReplayControlStatus
{
    /// <summary>Command reached the game: recording started / continue triggered.</summary>
    Accepted,

    /// <summary>The payload bytes are unusable (empty / decode failure / battleId mismatch). HTTP 400.</summary>
    InvalidPayload,

    /// <summary>The game is in a state that refuses the command (guards, wrong phase). HTTP 409.</summary>
    Rejected,

    /// <summary>The replay facade or combat-replay runtime is not available. HTTP 503.</summary>
    Unavailable,
}

public readonly record struct BazaarAgentReplayControlOutcome(
    BazaarAgentReplayControlStatus Status,
    string? FailureReason,
    string? BattleId
);

/// <summary>
/// Main-thread executor for replay control commands. Implemented by the host as a thin adapter
/// over the BazaarPlusPlus game-interop replay recorder facade. <c>Start</c> decodes and boots a
/// recorded replay; <c>Continue</c> drives the replay "continue" button (the only path allowed to
/// exit ReplayState — the host must never auto-exit it from a tick).
/// </summary>
public interface IBazaarAgentReplayControlSink
{
    BazaarAgentReplayControlOutcome Start(
        string requestId,
        byte[] ghostBattlePayloadBytes,
        string? battleId
    );

    BazaarAgentReplayControlOutcome Continue();
}

public interface IBazaarAgentLogger
{
    void Emit(BazaarAgentLogEvent logEvent);
}

public interface IBazaarAgentClock
{
    double NowSeconds { get; }

    string UtcNowIsoString();
}
