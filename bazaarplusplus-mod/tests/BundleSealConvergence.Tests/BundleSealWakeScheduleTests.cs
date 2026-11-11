using BazaarPlusPlus.Game.BundlePipeline;
using Xunit;

// Not BundleSealConvergence.Tests: that namespace would hide the Core type of the same name.
namespace BundleSeal.Tests;

// The seal worker wakes only for a job that needs a pass; with nothing waiting it does not scan
// until the idle cap after its last job discovery. Each test keeps the call of the capsule check
// it replaces.
public sealed class BundleSealWakeScheduleTests
{
    private const float Cap = BundleSealWakeSchedule.MaximumIdleWaitSeconds;

    [Fact]
    public void No_waiting_job_sleeps_until_the_idle_cap() =>
        Near(Cap, BundleSealWakeSchedule.WaitSeconds(null, 0f, maintenanceFailed: false));

    [Fact]
    public void The_idle_cap_counts_from_the_last_discovery() =>
        Near(Cap - 200f, BundleSealWakeSchedule.WaitSeconds(null, 200f, maintenanceFailed: false));

    [Fact]
    public void An_overdue_discovery_runs_on_the_next_short_wait() =>
        Near(
            BundleSealWakeSchedule.MinimumWaitSeconds,
            BundleSealWakeSchedule.WaitSeconds(null, Cap + 30f, maintenanceFailed: false)
        );

    [Fact]
    public void A_failed_discovery_retries_at_the_input_wait_cadence_never_faster() =>
        Near(
            BundleSealWakeSchedule.InputWaitPollSeconds,
            BundleSealWakeSchedule.WaitSeconds(null, Cap + 30f, maintenanceFailed: true)
        );

    [Fact]
    public void A_failed_discovery_does_not_delay_a_job_due_sooner() =>
        Near(2f, BundleSealWakeSchedule.WaitSeconds(2f, Cap + 30f, maintenanceFailed: true));

    [Fact]
    public void A_job_due_at_once_still_waits_the_minimum() =>
        Near(
            BundleSealWakeSchedule.MinimumWaitSeconds,
            BundleSealWakeSchedule.WaitSeconds(0.01f, 0f, maintenanceFailed: false)
        );

    [Fact]
    public void A_long_backoff_is_cut_to_the_idle_cap() =>
        Near(Cap, BundleSealWakeSchedule.WaitSeconds(3500f, 0f, maintenanceFailed: false));

    [Fact]
    public void A_backoff_shorter_than_the_cap_wakes_at_its_end() =>
        Near(15f, BundleSealWakeSchedule.WaitSeconds(15f, 0f, maintenanceFailed: false));

    [Fact]
    public void A_job_backing_off_wakes_the_worker_when_its_backoff_ends() =>
        Near(
            15f,
            BundleSealWakeSchedule.SecondsUntilAttempt(
                "seal_publish_failed",
                3,
                5f,
                100f,
                60f,
                attemptedInPass: false
            )
        );

    [Fact]
    public void A_job_parked_in_this_launch_never_wakes_the_worker() =>
        Assert.Null(
            BundleSealWakeSchedule.SecondsUntilAttempt(
                BundleSealFailurePolicy.EnvironmentBlockedCode,
                1,
                5f,
                100f,
                60f,
                attemptedInPass: false
            )
        );

    [Fact]
    public void A_due_job_still_waiting_on_an_input_is_polled_until_its_deadline() =>
        Near(
            BundleSealWakeSchedule.InputWaitPollSeconds,
            BundleSealWakeSchedule.SecondsUntilAttempt(
                null,
                0,
                null,
                100f,
                60f,
                attemptedInPass: true
            )
        );

    [Fact]
    public void A_due_job_waiting_on_an_input_wakes_the_worker_at_its_deadline() =>
        Near(
            2f,
            BundleSealWakeSchedule.SecondsUntilAttempt(
                null,
                0,
                null,
                100f,
                2f,
                attemptedInPass: true
            )
        );

    [Fact]
    public void A_due_job_past_its_deadline_is_polled_never_in_a_tight_loop() =>
        Near(
            BundleSealWakeSchedule.InputWaitPollSeconds,
            BundleSealWakeSchedule.SecondsUntilAttempt(
                "seal_publish_failed",
                1,
                30f,
                100f,
                -10f,
                attemptedInPass: true
            )
        );

    // Advance relative time through a pass: the backoff expires while a different Run seals.
    // The three checks are one sequence; the last one reads the second one's result.
    [Theory]
    [InlineData(60f)]
    [InlineData(-10f)]
    public void A_skipped_job_keeps_its_backoff_and_requests_the_next_pass_once_due(float deadline)
    {
        // A skipped job keeps its remaining backoff.
        Near(
            1f,
            BundleSealWakeSchedule.SecondsUntilAttempt(
                "seal_publish_failed",
                1,
                4f,
                100f,
                deadline,
                attemptedInPass: false
            )
        );
        var due = BundleSealWakeSchedule.SecondsUntilAttempt(
            "seal_publish_failed",
            1,
            6f,
            102f,
            deadline - 2f,
            attemptedInPass: false
        );
        // A skipped job that becomes due requests the next pass immediately.
        Near(0f, due);
        // The worker still applies its minimum wait to avoid a busy loop.
        Near(
            BundleSealWakeSchedule.MinimumWaitSeconds,
            BundleSealWakeSchedule.WaitSeconds(due, 2f, maintenanceFailed: false)
        );
    }

    [Fact]
    public void A_newly_listed_due_job_has_not_yet_waited_on_an_input() =>
        Near(
            0f,
            BundleSealWakeSchedule.SecondsUntilAttempt(
                null,
                0,
                null,
                100f,
                60f,
                attemptedInPass: false
            )
        );

    [Fact]
    public void A_requested_discovery_runs() =>
        Assert.True(BundleSealWakeSchedule.DiscoveryDue(requested: true, 0f));

    [Fact]
    public void Other_wakes_skip_discovery() =>
        Assert.False(BundleSealWakeSchedule.DiscoveryDue(requested: false, Cap - 1f));

    [Fact]
    public void Discovery_runs_once_the_idle_cap_has_passed() =>
        Assert.True(BundleSealWakeSchedule.DiscoveryDue(requested: false, Cap));

    private static void Near(float expected, float? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected, actual.Value, 0.001f);
    }
}
