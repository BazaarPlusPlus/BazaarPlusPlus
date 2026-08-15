#nullable enable
namespace BazaarPlusPlus.Game.BundlePipeline;

internal enum BundleSealInputGate
{
    ReplayPersistence,
    PlayerAccount,
    Screenshot,
    ReplayPayload,
    EncodedPayload,
}

internal enum BundleSealConvergenceDecision
{
    Continue,
    Wait,
    MarkScreenshotTimedOutAndContinue,
    MarkTerminal,
}

internal readonly struct BundleSealJobFacts
{
    internal BundleSealJobFacts(bool screenshotRequested, bool screenshotUnavailable)
    {
        ScreenshotRequested = screenshotRequested;
        ScreenshotUnavailable = screenshotUnavailable;
    }

    internal bool ScreenshotRequested { get; }
    internal bool ScreenshotUnavailable { get; }
}

internal readonly struct BundleSealInputObservation
{
    private BundleSealInputObservation(
        BundleSealInputGate gate,
        bool inputAvailable,
        int replayOmittedCount,
        bool encodedPayloadTooLarge
    )
    {
        Gate = gate;
        InputAvailable = inputAvailable;
        ReplayOmittedCount = replayOmittedCount;
        EncodedPayloadTooLarge = encodedPayloadTooLarge;
    }

    internal BundleSealInputGate Gate { get; }
    internal bool InputAvailable { get; }
    internal int ReplayOmittedCount { get; }
    internal bool EncodedPayloadTooLarge { get; }

    internal static BundleSealInputObservation Availability(
        BundleSealInputGate gate,
        bool available
    ) => new(gate, available, 0, false);

    internal static BundleSealInputObservation ReplayPayload(int omittedCount) =>
        new(BundleSealInputGate.ReplayPayload, omittedCount == 0, omittedCount, false);

    internal static BundleSealInputObservation EncodedPayload(bool tooLarge) =>
        new(BundleSealInputGate.EncodedPayload, !tooLarge, 0, tooLarge);
}

internal static class BundleSealConvergence
{
    internal static BundleSealConvergenceDecision Resolve(
        BundleSealJobFacts job,
        float secondsUntilInputDeadline,
        BundleSealInputObservation observation
    )
    {
        var deadlineReached = secondsUntilInputDeadline <= 0f;
        switch (observation.Gate)
        {
            case BundleSealInputGate.ReplayPersistence:
                return !observation.InputAvailable && !deadlineReached
                    ? BundleSealConvergenceDecision.Wait
                    : BundleSealConvergenceDecision.Continue;
            case BundleSealInputGate.PlayerAccount:
                if (observation.InputAvailable)
                    return BundleSealConvergenceDecision.Continue;
                return deadlineReached
                    ? BundleSealConvergenceDecision.MarkTerminal
                    : BundleSealConvergenceDecision.Wait;
            case BundleSealInputGate.Screenshot:
                if (
                    !job.ScreenshotRequested
                    || job.ScreenshotUnavailable
                    || observation.InputAvailable
                )
                    return BundleSealConvergenceDecision.Continue;
                return deadlineReached
                    ? BundleSealConvergenceDecision.MarkScreenshotTimedOutAndContinue
                    : BundleSealConvergenceDecision.Wait;
            case BundleSealInputGate.ReplayPayload:
                return observation.ReplayOmittedCount > 0 && !deadlineReached
                    ? BundleSealConvergenceDecision.Wait
                    : BundleSealConvergenceDecision.Continue;
            case BundleSealInputGate.EncodedPayload:
                return observation.EncodedPayloadTooLarge
                    ? BundleSealConvergenceDecision.MarkTerminal
                    : BundleSealConvergenceDecision.Continue;
            default:
                return BundleSealConvergenceDecision.MarkTerminal;
        }
    }
}
