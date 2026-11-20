#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay;

public readonly record struct ReplayPlaybackCleanupStep(string Stage, Action Execute);

public readonly record struct ReplayPlaybackPublishOutcome(bool Succeeded, Exception? Exception)
{
    public static ReplayPlaybackPublishOutcome Success() => new(true, null);

    public static ReplayPlaybackPublishOutcome Failure(Exception exception) =>
        new(false, exception ?? throw new ArgumentNullException(nameof(exception)));
}

/// <summary>Each exit path keeps its own cleanup order (ADR-0003).</summary>
public static class ReplayPlaybackCleanup
{
    public static ReplayPlaybackPublishOutcome PublishThenCleanup(
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

    public static void Run(
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
                catch { }
            }
        }
    }
}
