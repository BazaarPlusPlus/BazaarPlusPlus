#nullable enable
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.Upload;

internal enum UploadFeedKind
{
    Bundle,
}

internal enum UploadAttemptObservationKind
{
    NoWork,
    Deferred,
    Degraded,
    Succeeded,
    NoHealthSignal,
}

internal enum UploadLogReasonCode
{
    InvalidLocalPaths,
    InitializationException,
    LiveRunActive,
    AccountUnavailable,
    BundleNotReady,
    AccountProbeException,
    AttemptException,
    RemoteUploadFailed,
    ActivationDisposeException,
    PtrBuild,
    ShutdownDrainTimeout,
    PayloadInvalid,
    PayloadUnreadable,
}

internal enum UploadCleanupPhase
{
    ActivationDispose,
}

internal readonly record struct UploadPumpCadence(
    int StartupDelaySeconds,
    int RetryIntervalSeconds
);

internal readonly record struct UploadAttemptObservation(
    UploadAttemptObservationKind Kind,
    string? RunId,
    UploadLogReasonCode? ReasonCode,
    int? PendingCount,
    Exception? Exception
)
{
    internal static UploadAttemptObservation NoWork() =>
        new(UploadAttemptObservationKind.NoWork, null, null, null, null);

    internal static UploadAttemptObservation NoHealthSignal() =>
        new(UploadAttemptObservationKind.NoHealthSignal, null, null, null, null);

    internal static UploadAttemptObservation Deferred(
        UploadLogReasonCode reasonCode,
        int? pendingCount = null
    ) => new(UploadAttemptObservationKind.Deferred, null, reasonCode, pendingCount, null);

    internal static UploadAttemptObservation Degraded(
        string? runId,
        UploadLogReasonCode reasonCode,
        Exception? exception = null
    ) => new(UploadAttemptObservationKind.Degraded, runId, reasonCode, null, exception);

    internal static UploadAttemptObservation Succeeded(string runId) =>
        new(UploadAttemptObservationKind.Succeeded, runId, null, null, null);
}

internal sealed class UploadAttemptResult
{
    private readonly UploadAttemptObservation[] _observations;
    private readonly IReadOnlyList<UploadAttemptObservation> _readOnlyObservations;

    private UploadAttemptResult(IReadOnlyList<UploadAttemptObservation> observations)
    {
        _observations = new UploadAttemptObservation[observations.Count];
        for (var index = 0; index < observations.Count; index++)
            _observations[index] = observations[index];
        _readOnlyObservations = Array.AsReadOnly(_observations);
    }

    internal IReadOnlyList<UploadAttemptObservation> Observations => _readOnlyObservations;

    internal static UploadAttemptResult NoWork() => From(UploadAttemptObservation.NoWork());

    internal static UploadAttemptResult NoHealthSignal() =>
        From(UploadAttemptObservation.NoHealthSignal());

    internal static UploadAttemptResult From(params UploadAttemptObservation[] observations) =>
        new(observations ?? Array.Empty<UploadAttemptObservation>());

    internal static UploadAttemptResult From(
        IReadOnlyList<UploadAttemptObservation> observations
    ) => new(observations ?? Array.Empty<UploadAttemptObservation>());
}

/// <summary>
/// Feed factory: the pump supplies real cadence and receives a behavior session (or null when
/// activation fails). PTR channel gating stays on the pump as an activation precondition.
/// </summary>
internal interface IUploadFeed
{
    UploadFeedKind Kind { get; }

    IUploadFeedSession? Activate(
        IBppServices services,
        UploadFeedLogState logState,
        UploadPumpCadence cadence
    );
}

/// <summary>
/// Feed-owned behavior for one pump lifetime: enablement, one attempt, feed-private arm signals,
/// and attempt-resource disposal. The pump owns Unity cadence, shared arms, and shutdown drain.
/// </summary>
internal interface IUploadFeedSession : IDisposable
{
    bool IsEnabled { get; }

    Task<UploadAttemptResult> RunAttemptAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Subscribe feed-private arm signals. Returns null when the feed has none.
    /// The pump holds the handle and disposes it first on shutdown; session.Dispose only owns
    /// attempt resources and may dispose this handle as an idempotent fallback.
    /// </summary>
    IDisposable? SubscribeArmSignals(Action arm);
}
