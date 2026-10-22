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
    ShutdownForced,
    QueueShutdownDrainTimeout,
    QueueWriteException,
    QueueWorkerTerminatedUnexpectedly,
}

public enum RunLoggingTransition
{
    RunEntered,
    RunEnded,
    RunInterrupted,
    StateReconciled,
    Unknown,
}
