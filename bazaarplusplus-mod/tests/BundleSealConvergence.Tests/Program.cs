#nullable enable
using BazaarPlusPlus.Game.BundlePipeline;

var noScreenshot = new BundleSealJobFacts(false, false);
var waitingScreenshot = new BundleSealJobFacts(true, false);
var unavailableScreenshot = new BundleSealJobFacts(true, true);

Equal(
    BundleSealConvergenceDecision.Wait,
    Resolve(noScreenshot, 1f, BundleSealInputGate.ReplayPersistence, false),
    "pending replay before deadline"
);
Equal(
    BundleSealConvergenceDecision.Continue,
    Resolve(noScreenshot, 0f, BundleSealInputGate.ReplayPersistence, false),
    "pending replay at deadline"
);
Equal(
    BundleSealConvergenceDecision.Continue,
    Resolve(noScreenshot, -1f, BundleSealInputGate.ReplayPersistence, false),
    "pending replay after deadline"
);
Equal(
    BundleSealConvergenceDecision.Wait,
    Resolve(noScreenshot, 1f, BundleSealInputGate.PlayerAccount, false),
    "missing account before deadline"
);
Equal(
    BundleSealConvergenceDecision.MarkTerminal,
    Resolve(noScreenshot, 0f, BundleSealInputGate.PlayerAccount, false),
    "missing account at deadline"
);
Equal(
    BundleSealConvergenceDecision.Wait,
    Resolve(waitingScreenshot, 1f, BundleSealInputGate.Screenshot, false),
    "missing requested screenshot before deadline"
);
Equal(
    BundleSealConvergenceDecision.MarkScreenshotTimedOutAndContinue,
    Resolve(waitingScreenshot, 0f, BundleSealInputGate.Screenshot, false),
    "missing requested screenshot at deadline"
);
Equal(
    BundleSealConvergenceDecision.Continue,
    Resolve(unavailableScreenshot, 1f, BundleSealInputGate.Screenshot, false),
    "explicitly unavailable screenshot does not wait"
);
Equal(
    BundleSealConvergenceDecision.Wait,
    BundleSealConvergence.Resolve(noScreenshot, 1f, BundleSealInputObservation.ReplayPayload(1)),
    "replay omission before deadline"
);
Equal(
    BundleSealConvergenceDecision.Continue,
    BundleSealConvergence.Resolve(noScreenshot, 0f, BundleSealInputObservation.ReplayPayload(1)),
    "replay omission at deadline"
);
Equal(
    BundleSealConvergenceDecision.MarkTerminal,
    BundleSealConvergence.Resolve(
        noScreenshot,
        10f,
        BundleSealInputObservation.EncodedPayload(true)
    ),
    "minimal payload overflow is terminal"
);

Console.WriteLine("Bundle seal convergence checks passed.");

static BundleSealConvergenceDecision Resolve(
    BundleSealJobFacts job,
    float seconds,
    BundleSealInputGate gate,
    bool available
) =>
    BundleSealConvergence.Resolve(
        job,
        seconds,
        BundleSealInputObservation.Availability(gate, available)
    );

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
}
