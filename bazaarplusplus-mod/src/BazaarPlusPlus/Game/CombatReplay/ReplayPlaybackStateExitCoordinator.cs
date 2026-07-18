#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay;

internal readonly record struct ReplayPlaybackCleanupStep(string Stage, Action Execute);

internal readonly record struct ReplayPlaybackPublishOutcome(bool Succeeded, Exception? Exception)
{
    internal static ReplayPlaybackPublishOutcome Success() => new(true, null);

    internal static ReplayPlaybackPublishOutcome Failure(Exception exception) =>
        new(false, exception ?? throw new ArgumentNullException(nameof(exception)));
}

internal static class ReplayPlaybackStateExitCoordinator
{
    internal static ReplayPlaybackPublishOutcome Handle(
        bool startCoordinatorOwnsTerminal,
        Func<ReplayPlaybackPublishOutcome> publishEnded,
        Action<ReplayPlaybackReasonCode, Exception?>? latchStartupInterruption,
        Action<string, Exception>? observeCleanupFailure,
        params ReplayPlaybackCleanupStep[] cleanupSteps
    )
    {
        if (publishEnded == null)
            throw new ArgumentNullException(nameof(publishEnded));
        if (startCoordinatorOwnsTerminal && latchStartupInterruption == null)
            throw new ArgumentNullException(nameof(latchStartupInterruption));

        ReplayPlaybackPublishOutcome ended;
        try
        {
            ended = publishEnded();
        }
        catch (Exception ex)
        {
            ended = ReplayPlaybackPublishOutcome.Failure(ex);
        }

        if (startCoordinatorOwnsTerminal)
        {
            latchStartupInterruption!(
                ended.Succeeded
                    ? ReplayPlaybackReasonCode.StartException
                    : ReplayPlaybackReasonCode.EndedPublishFailed,
                ended.Exception
            );
        }

        RunCleanup(observeCleanupFailure, cleanupSteps);
        return ended;
    }

    internal static void RunCleanup(
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
