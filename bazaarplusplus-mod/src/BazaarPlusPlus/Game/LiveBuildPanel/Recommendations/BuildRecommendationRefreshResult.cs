#nullable enable
namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

internal enum BuildRecommendationRefreshOutcome
{
    Updated,
    NoChange,
    Failed,
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
