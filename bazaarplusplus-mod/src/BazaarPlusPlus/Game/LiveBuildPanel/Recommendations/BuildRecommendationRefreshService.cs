#nullable enable
namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

internal readonly struct BuildRecommendationRemoteRefreshResult
{
    private BuildRecommendationRemoteRefreshResult(
        bool succeeded,
        string? error,
        LiveBuildRefreshFailureReasonCode? failureReason,
        Exception? exception
    )
    {
        Item1 = succeeded;
        Item2 = error;
        FailureReason = failureReason;
        Exception = exception;
    }

    // Preserve the tuple-shaped reflection seam used by the repository behavior tests.
    public readonly bool Item1;
    public readonly string? Item2;

    internal bool Succeeded => Item1;

    internal string? Error => Item2;

    internal LiveBuildRefreshFailureReasonCode? FailureReason { get; }

    internal Exception? Exception { get; }

    internal static BuildRecommendationRemoteRefreshResult Success() =>
        new(succeeded: true, error: null, failureReason: null, exception: null);

    internal static BuildRecommendationRemoteRefreshResult Failure(
        LiveBuildRefreshFailureReasonCode reason,
        string? error,
        Exception? exception = null
    ) => new(succeeded: false, error, reason, exception);

    public void Deconstruct(out bool succeeded, out string? error)
    {
        succeeded = Item1;
        error = Item2;
    }
}

internal enum BuildRecommendationRefreshOutcome
{
    Updated,
    NoChange,
    Failed,
}

/// <summary>
/// Shared manual-refresh entry over the ten-win build corpus. Awaits the repository's async
/// remote refresh so panel callers stay off the Unity main thread while the download is in
/// flight. The token only prevents the refresh from starting (the HTTP read is not interruptible
/// once issued); callers guard their own stale continuations.
/// </summary>
internal sealed class BuildRecommendationRefreshService
{
    // A manual pull really hits the server only once per session: the ten-win corpus regenerates
    // only every few hours server-side, so any later pull in the same session reports a synthetic
    // success (a no-op "fake update") instead of re-downloading. A failed pull does not consume the
    // allowance, so the user can retry.
    private bool _hasSuccessfullyPulled;

    public async Task<BuildRecommendationRefreshResult> RefreshAsync(
        BuildRecommendationRepository repository,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_hasSuccessfullyPulled)
            return BuildRecommendationRefreshResult.NoChange();

        try
        {
            var result = await repository
                .TryRefreshFinalBuildsFromRemoteAsync()
                .ConfigureAwait(false);
            if (result.Succeeded)
            {
                _hasSuccessfullyPulled = true;
                return BuildRecommendationRefreshResult.Updated();
            }

            return BuildRecommendationRefreshResult.Failure(
                result.FailureReason ?? LiveBuildRefreshFailureReasonCode.RefreshException,
                result.Error,
                result.Exception
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BuildRecommendationRefreshResult.Failure(
                LiveBuildRefreshFailureReasonCode.RefreshException,
                ex.Message,
                ex
            );
        }
    }
}

internal readonly struct BuildRecommendationRefreshResult
{
    private BuildRecommendationRefreshResult(
        BuildRecommendationRefreshOutcome outcome,
        string? error,
        LiveBuildRefreshFailureReasonCode? failureReason,
        Exception? exception
    )
    {
        Outcome = outcome;
        Error = error;
        FailureReason = failureReason;
        Exception = exception;
    }

    public bool Succeeded => Outcome != BuildRecommendationRefreshOutcome.Failed;

    internal BuildRecommendationRefreshOutcome Outcome { get; }

    public string? Error { get; }

    internal LiveBuildRefreshFailureReasonCode? FailureReason { get; }

    internal Exception? Exception { get; }

    internal static BuildRecommendationRefreshResult Updated() =>
        new(BuildRecommendationRefreshOutcome.Updated, null, null, null);

    internal static BuildRecommendationRefreshResult NoChange() =>
        new(BuildRecommendationRefreshOutcome.NoChange, null, null, null);

    internal static BuildRecommendationRefreshResult Failure(
        LiveBuildRefreshFailureReasonCode reason,
        string? error,
        Exception? exception = null
    ) => new(BuildRecommendationRefreshOutcome.Failed, error, reason, exception);
}
