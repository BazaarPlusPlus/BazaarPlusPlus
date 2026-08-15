#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay;

internal readonly record struct ReplayPlaybackCleanupStep(string Stage, Action Execute);

internal readonly record struct ReplayPlaybackPublishOutcome(bool Succeeded, Exception? Exception)
{
    internal static ReplayPlaybackPublishOutcome Success() => new(true, null);

    internal static ReplayPlaybackPublishOutcome Failure(Exception exception) =>
        new(false, exception ?? throw new ArgumentNullException(nameof(exception)));
}

/// <summary>
/// Runtime-private cleanup runner. Each exit path keeps its own cleanup order (ADR-0009);
/// this helper only isolates per-step failure observation so one bad cleanup cannot abort the rest.
/// </summary>
internal static class ReplayPlaybackCleanup
{
    internal static ReplayPlaybackPublishOutcome PublishThenCleanup(
        Func<ReplayPlaybackPublishOutcome> publishEnded,
        Action<string, Exception>? observeCleanupFailure,
        params ReplayPlaybackCleanupStep[] cleanupSteps
    )
    {
        if (publishEnded == null)
            throw new ArgumentNullException(nameof(publishEnded));

        ReplayPlaybackPublishOutcome ended;
        try
        {
            ended = publishEnded();
        }
        catch (Exception ex)
        {
            ended = ReplayPlaybackPublishOutcome.Failure(ex);
        }

        Run(observeCleanupFailure, cleanupSteps);
        return ended;
    }

    internal static void Run(
        Action<string, Exception>? observeCleanupFailure,
        params ReplayPlaybackCleanupStep[] cleanupSteps
    )
    {
        if (cleanupSteps == null)
            throw new ArgumentNullException(nameof(cleanupSteps));

        foreach (var cleanupStep in cleanupSteps)
        {
            try
            {
                cleanupStep.Execute();
            }
            catch (Exception ex)
            {
                try
                {
                    observeCleanupFailure?.Invoke(cleanupStep.Stage, ex);
                }
                catch
                {
                    // Cleanup observers are diagnostic only and cannot interrupt lifecycle state.
                }
            }
        }
    }
}
