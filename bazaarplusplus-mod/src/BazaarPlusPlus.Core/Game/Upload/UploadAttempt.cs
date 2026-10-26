#nullable enable

namespace BazaarPlusPlus.Game.Upload;

public enum UploadAttemptObservationKind
{
    NoWork,
    Deferred,
    Degraded,
    Succeeded,
    NoHealthSignal,
}

public enum UploadLogReasonCode
{
    LiveRunActive,
    AttemptException,
    RemoteUploadFailed,
    ActivationDisposeException,
    PtrBuild,
    ShutdownDrainTimeout,
    PayloadInvalid,
    PayloadUnreadable,

    // Pending Bundles wait out the queue backoff after the endpoint was unavailable.
    QueueBackoff,
}

public enum UploadCleanupPhase
{
    ActivationDispose,
}

public readonly record struct UploadPumpCadence(int StartupDelaySeconds, int RetryIntervalSeconds);

public readonly record struct UploadAttemptObservation(
    UploadAttemptObservationKind Kind,
    string? RunId,
    UploadLogReasonCode? ReasonCode,
    int? PendingCount,
    Exception? Exception
)
{
    public static UploadAttemptObservation NoWork() =>
        new(UploadAttemptObservationKind.NoWork, null, null, null, null);

    public static UploadAttemptObservation NoHealthSignal() =>
        new(UploadAttemptObservationKind.NoHealthSignal, null, null, null, null);

    public static UploadAttemptObservation Deferred(
        UploadLogReasonCode reasonCode,
        int? pendingCount = null
    ) => new(UploadAttemptObservationKind.Deferred, null, reasonCode, pendingCount, null);

    public static UploadAttemptObservation Degraded(
        string? runId,
        UploadLogReasonCode reasonCode,
        Exception? exception = null
    ) => new(UploadAttemptObservationKind.Degraded, runId, reasonCode, null, exception);

    public static UploadAttemptObservation Succeeded(string runId) =>
        new(UploadAttemptObservationKind.Succeeded, runId, null, null, null);
}

public sealed class UploadAttemptResult
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

    public IReadOnlyList<UploadAttemptObservation> Observations => _readOnlyObservations;

    public static UploadAttemptResult NoWork() => From(UploadAttemptObservation.NoWork());

    public static UploadAttemptResult NoHealthSignal() =>
        From(UploadAttemptObservation.NoHealthSignal());

    public static UploadAttemptResult From(params UploadAttemptObservation[] observations) =>
        new(observations ?? Array.Empty<UploadAttemptObservation>());

    public static UploadAttemptResult From(IReadOnlyList<UploadAttemptObservation> observations) =>
        new(observations ?? Array.Empty<UploadAttemptObservation>());
}
