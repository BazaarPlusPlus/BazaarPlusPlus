#nullable enable

namespace BazaarPlusPlus.Game.RunLogging;

public enum RunLoggingReasonCode
{
    TeardownFinalizationException,
    RunActivationException,
    RunTransitionException,
    BattleCaptureException,
    InRunMismatch,
    ManifestRunUnavailable,
    DeferredRunMismatch,
    ReplayDrainHandlingException,
    ReplayDrainTimeout,
    RankRefreshHandlingException,
    RankRefreshSubscribeException,
    RankRefreshTimeout,
    ShutdownForced,
    NextRunActivated,
    QueueShutdownDrainTimeout,
    QueueWriteException,
    QueueWorkerTerminatedUnexpectedly,
    MessageStreamException,
}

public enum RunLoggingTransition
{
    RunEntered,
    RunEnded,
    RunInterrupted,
    StateReconciled,
    Unknown,
}
