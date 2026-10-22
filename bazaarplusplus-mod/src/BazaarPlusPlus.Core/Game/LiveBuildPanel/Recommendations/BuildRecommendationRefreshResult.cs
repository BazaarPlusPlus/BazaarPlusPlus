#nullable enable
namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

public enum BuildRecommendationRefreshOutcome
{
    Updated,
    NoChange,
    Failed,
}

public readonly struct BuildRecommendationRefreshResult
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

    public BuildRecommendationRefreshOutcome Outcome { get; }

    public string? Error { get; }

    public LiveBuildRefreshFailureReasonCode? FailureReason { get; }

    public Exception? Exception { get; }

    public static BuildRecommendationRefreshResult Updated() =>
        new(BuildRecommendationRefreshOutcome.Updated, null, null, null);

    public static BuildRecommendationRefreshResult NoChange() =>
        new(BuildRecommendationRefreshOutcome.NoChange, null, null, null);

    public static BuildRecommendationRefreshResult Failure(
        LiveBuildRefreshFailureReasonCode reason,
        string? error,
        Exception? exception = null
    ) => new(BuildRecommendationRefreshOutcome.Failed, error, reason, exception);
}
