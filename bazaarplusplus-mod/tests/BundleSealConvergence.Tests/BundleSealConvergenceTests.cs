using BazaarPlusPlus.Game.BundlePipeline;
using Xunit;

// Not BundleSealConvergence.Tests: that namespace would hide the Core type of the same name.
namespace BundleSeal.Tests;

// Each test keeps the call of the capsule check it replaces.
public sealed class BundleSealConvergenceTests
{
    private static readonly BundleSealJobFacts NoScreenshot = new(false, false);
    private static readonly BundleSealJobFacts WaitingScreenshot = new(true, false);
    private static readonly BundleSealJobFacts UnavailableScreenshot = new(true, true);

    [Fact]
    public void Pending_replay_before_deadline_waits() =>
        Assert.Equal(
            BundleSealConvergenceDecision.Wait,
            Resolve(NoScreenshot, 1f, BundleSealInputGate.ReplayPersistence, false)
        );

    [Fact]
    public void Pending_replay_at_deadline_continues() =>
        Assert.Equal(
            BundleSealConvergenceDecision.Continue,
            Resolve(NoScreenshot, 0f, BundleSealInputGate.ReplayPersistence, false)
        );

    [Fact]
    public void Pending_replay_after_deadline_continues() =>
        Assert.Equal(
            BundleSealConvergenceDecision.Continue,
            Resolve(NoScreenshot, -1f, BundleSealInputGate.ReplayPersistence, false)
        );

    [Fact]
    public void Missing_account_before_deadline_waits() =>
        Assert.Equal(
            BundleSealConvergenceDecision.Wait,
            Resolve(NoScreenshot, 1f, BundleSealInputGate.PlayerAccount, false)
        );

    [Fact]
    public void Missing_account_at_deadline_is_terminal() =>
        Assert.Equal(
            BundleSealConvergenceDecision.MarkTerminal,
            Resolve(NoScreenshot, 0f, BundleSealInputGate.PlayerAccount, false)
        );

    [Fact]
    public void Missing_requested_screenshot_before_deadline_waits() =>
        Assert.Equal(
            BundleSealConvergenceDecision.Wait,
            Resolve(WaitingScreenshot, 1f, BundleSealInputGate.Screenshot, false)
        );

    [Fact]
    public void Missing_requested_screenshot_at_deadline_times_out_and_continues() =>
        Assert.Equal(
            BundleSealConvergenceDecision.MarkScreenshotTimedOutAndContinue,
            Resolve(WaitingScreenshot, 0f, BundleSealInputGate.Screenshot, false)
        );

    [Fact]
    public void Explicitly_unavailable_screenshot_does_not_wait() =>
        Assert.Equal(
            BundleSealConvergenceDecision.Continue,
            Resolve(UnavailableScreenshot, 1f, BundleSealInputGate.Screenshot, false)
        );

    [Fact]
    public void Replay_omission_before_deadline_waits() =>
        Assert.Equal(
            BundleSealConvergenceDecision.Wait,
            BundleSealConvergence.Resolve(
                NoScreenshot,
                1f,
                BundleSealInputObservation.ReplayPayload(1)
            )
        );

    [Fact]
    public void Replay_omission_at_deadline_continues() =>
        Assert.Equal(
            BundleSealConvergenceDecision.Continue,
            BundleSealConvergence.Resolve(
                NoScreenshot,
                0f,
                BundleSealInputObservation.ReplayPayload(1)
            )
        );

    // A Run that waited on omitted replays is not composed again, its screenshot not encoded,
    // until replay persistence completes or its deadline arrives.
    [Fact]
    public void Remembered_replay_omission_without_persistence_skips_the_attempt() =>
        Assert.Equal(
            BundleSealConvergenceDecision.Wait,
            BundleSealConvergence.Resolve(
                WaitingScreenshot,
                1f,
                BundleSealInputObservation.RememberedReplayPayload(
                    1,
                    persistenceChangedSince: false
                )
            )
        );

    [Fact]
    public void Replay_persistence_since_the_remembered_omission_recomposes() =>
        Assert.Equal(
            BundleSealConvergenceDecision.Continue,
            BundleSealConvergence.Resolve(
                WaitingScreenshot,
                1f,
                BundleSealInputObservation.RememberedReplayPayload(1, persistenceChangedSince: true)
            )
        );

    [Fact]
    public void Remembered_replay_omission_at_deadline_recomposes() =>
        Assert.Equal(
            BundleSealConvergenceDecision.Continue,
            BundleSealConvergence.Resolve(
                WaitingScreenshot,
                0f,
                BundleSealInputObservation.RememberedReplayPayload(
                    1,
                    persistenceChangedSince: false
                )
            )
        );

    [Fact]
    public void Minimal_payload_overflow_is_terminal() =>
        Assert.Equal(
            BundleSealConvergenceDecision.MarkTerminal,
            BundleSealConvergence.Resolve(
                NoScreenshot,
                10f,
                BundleSealInputObservation.EncodedPayload(true)
            )
        );

    private static BundleSealConvergenceDecision Resolve(
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
}
