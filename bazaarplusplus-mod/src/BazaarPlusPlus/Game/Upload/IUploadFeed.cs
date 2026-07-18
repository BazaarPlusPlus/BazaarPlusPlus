#nullable enable
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.Upload;

internal enum UploadFeedKind
{
    RunBundle,
    BazaarDbSnapshot,
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
    RunBundleNotReady,
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

internal interface IUploadFeed
{
    UploadFeedKind Kind { get; }
    UploadFeedActivation? Activate(IBppServices services, UploadFeedLogState logState);
}

internal sealed class UploadFeedActivation
{
    public Func<
        CancellationToken,
        Task<UploadAttemptResult>
    > UploadInBackgroundAsync { get; init; } =
        _ => throw new InvalidOperationException("Upload delegate is not configured.");
    public Func<bool> IsEnabled { get; init; } = static () => true;
    public IDisposable? Disposable { get; init; }
    public UploadArmHook? ExtraArmHook { get; init; }
}

internal sealed record UploadArmHook(Func<IBppServices, Action, IDisposable> Subscribe);
