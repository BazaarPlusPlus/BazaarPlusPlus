#nullable enable

namespace BazaarPlusPlus.Game.RunLogging;

internal enum RunLoggingReasonCode
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

internal enum RunLoggingTransition
{
    RunEntered,
    RunEnded,
    RunInterrupted,
    StateReconciled,
    Unknown,
}
