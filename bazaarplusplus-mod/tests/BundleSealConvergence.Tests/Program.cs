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

// A Run that waited on omitted replays is not composed again, its screenshot not encoded,
// until replay persistence completes or its deadline arrives.
Equal(
    BundleSealConvergenceDecision.Wait,
    BundleSealConvergence.Resolve(
        waitingScreenshot,
        1f,
        BundleSealInputObservation.RememberedReplayPayload(1, persistenceChangedSince: false)
    ),
    "remembered replay omission without persistence skips the attempt"
);
Equal(
    BundleSealConvergenceDecision.Continue,
    BundleSealConvergence.Resolve(
        waitingScreenshot,
        1f,
        BundleSealInputObservation.RememberedReplayPayload(1, persistenceChangedSince: true)
    ),
    "replay persistence since the remembered omission recomposes"
);
Equal(
    BundleSealConvergenceDecision.Continue,
    BundleSealConvergence.Resolve(
        waitingScreenshot,
        0f,
        BundleSealInputObservation.RememberedReplayPayload(1, persistenceChangedSince: false)
    ),
    "remembered replay omission at deadline recomposes"
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

// The seal worker wakes only for a job that needs a pass; with nothing waiting it does not scan
// until the idle cap after its last job discovery.
const float Cap = BundleSealWakeSchedule.MaximumIdleWaitSeconds;
Near(
    Cap,
    BundleSealWakeSchedule.WaitSeconds(null, 0f, maintenanceFailed: false),
    "no waiting job sleeps until the idle cap"
);
Near(
    Cap - 200f,
    BundleSealWakeSchedule.WaitSeconds(null, 200f, maintenanceFailed: false),
    "the idle cap counts from the last discovery"
);
Near(
    BundleSealWakeSchedule.MinimumWaitSeconds,
    BundleSealWakeSchedule.WaitSeconds(null, Cap + 30f, maintenanceFailed: false),
    "an overdue discovery runs on the next short wait"
);
Near(
    BundleSealWakeSchedule.InputWaitPollSeconds,
    BundleSealWakeSchedule.WaitSeconds(null, Cap + 30f, maintenanceFailed: true),
    "a failed discovery retries at the input-wait cadence, never faster"
);
Near(
    2f,
    BundleSealWakeSchedule.WaitSeconds(2f, Cap + 30f, maintenanceFailed: true),
    "a failed discovery does not delay a job due sooner"
);
Near(
    BundleSealWakeSchedule.MinimumWaitSeconds,
    BundleSealWakeSchedule.WaitSeconds(0.01f, 0f, maintenanceFailed: false),
    "a job due at once still waits the minimum"
);
Near(
    Cap,
    BundleSealWakeSchedule.WaitSeconds(3500f, 0f, maintenanceFailed: false),
    "a long backoff is cut to the idle cap"
);
Near(
    15f,
    BundleSealWakeSchedule.WaitSeconds(15f, 0f, maintenanceFailed: false),
    "a backoff shorter than the cap wakes at its end"
);

Near(
    15f,
    BundleSealWakeSchedule.SecondsUntilAttempt(
        "seal_publish_failed",
        3,
        5f,
        100f,
        60f,
        attemptedInPass: false
    ),
    "a job backing off wakes the worker when its backoff ends"
);
Equal(
    null,
    BundleSealWakeSchedule.SecondsUntilAttempt(
        BundleSealFailurePolicy.EnvironmentBlockedCode,
        1,
        5f,
        100f,
        60f,
        attemptedInPass: false
    ),
    "a job parked in this launch never wakes the worker"
);
Near(
    BundleSealWakeSchedule.InputWaitPollSeconds,
    BundleSealWakeSchedule.SecondsUntilAttempt(null, 0, null, 100f, 60f, attemptedInPass: true),
    "a due job still waiting on an input is polled until its deadline"
);
Near(
    2f,
    BundleSealWakeSchedule.SecondsUntilAttempt(null, 0, null, 100f, 2f, attemptedInPass: true),
    "a due job waiting on an input wakes the worker at its deadline"
);
Near(
    BundleSealWakeSchedule.InputWaitPollSeconds,
    BundleSealWakeSchedule.SecondsUntilAttempt(
        "seal_publish_failed",
        1,
        30f,
        100f,
        -10f,
        attemptedInPass: true
    ),
    "a due job past its deadline is polled, never in a tight loop"
);

// Advance relative time through a pass: the backoff expires while a different Run seals.
foreach (var deadline in new[] { 60f, -10f })
{
    Near(
        1f,
        BundleSealWakeSchedule.SecondsUntilAttempt(
            "seal_publish_failed",
            1,
            4f,
            100f,
            deadline,
            attemptedInPass: false
        ),
        "a skipped job keeps its remaining backoff"
    );
    var due = BundleSealWakeSchedule.SecondsUntilAttempt(
        "seal_publish_failed",
        1,
        6f,
        102f,
        deadline - 2f,
        attemptedInPass: false
    );
    Near(0f, due, "a skipped job that becomes due requests the next pass immediately");
    Near(
        BundleSealWakeSchedule.MinimumWaitSeconds,
        BundleSealWakeSchedule.WaitSeconds(due, 2f, maintenanceFailed: false),
        "the worker still applies its minimum wait to avoid a busy loop"
    );
}
Near(
    0f,
    BundleSealWakeSchedule.SecondsUntilAttempt(null, 0, null, 100f, 60f, attemptedInPass: false),
    "a newly listed due job has not yet waited on an input"
);
Equal(true, BundleSealWakeSchedule.DiscoveryDue(requested: true, 0f), "a requested discovery runs");
Equal(
    false,
    BundleSealWakeSchedule.DiscoveryDue(requested: false, Cap - 1f),
    "other wakes skip discovery"
);
Equal(
    true,
    BundleSealWakeSchedule.DiscoveryDue(requested: false, Cap),
    "discovery runs once the idle cap has passed"
);

BundleSealFailurePolicyTests.Run();
Console.WriteLine("Bundle seal convergence checks passed.");

static void Near(float expected, float? actual, string message)
{
    if (actual is not { } value || Math.Abs(value - expected) > 0.001f)
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
}

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
