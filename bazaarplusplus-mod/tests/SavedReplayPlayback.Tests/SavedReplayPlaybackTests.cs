#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using Xunit;

namespace BazaarPlusPlus.Tests.SavedReplays;

// The saved-replay protocol both hosts share (ADR-0020), driven through SavedReplayPlayback with
// an in-memory host. A lease is free when the gate grants a fresh playback lease.
public sealed class SavedReplayPlaybackTests
{
    private const string BattleA = "battle-aaaaaaaa";
    private const string BattleB = "battle-bbbbbbbb";

    // --- Admission ---

    [Fact]
    public void Each_rejection_rung_logs_its_reason_and_holds_no_lease()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);

        Assert.False(h.Playback.ReplaySaved("  ", recordVideo: false));
        Assert.False(h.Playback.ReplaySaved("battle-missing", recordVideo: false));
        h.Services.RunContext.IsInGameRun = true;
        Assert.False(h.Playback.ReplaySaved(BattleA, recordVideo: false));
        h.Services.RunContext.IsInGameRun = false;
        h.Host.InReplayState = true;
        Assert.False(h.Playback.ReplaySaved(BattleA, recordVideo: false));
        h.Host.InReplayState = false;
        using (h.Gate.AcquireMaintenance(CancellationToken.None))
        {
            Assert.False(h.Playback.ReplaySaved(BattleA, recordVideo: false));
            Assert.Equal(
                SavedBattleLoadOutcome.Busy,
                h.Playback.TryLoadSavedBattle(BattleA, out _, out _)
            );
        }

        var reasons = h.LogLines("combat_replay.playback.request_rejected")
            .Select(ReasonCode)
            .ToList();
        Assert.Equal(
            [
                "invalid_battle_id",
                "payload_unavailable",
                "active_run",
                "replay_already_active",
                "payload_operation_busy",
            ],
            reasons
        );
        Assert.Equal(["  ", "battle-missing", BattleA, BattleA, BattleA], h.Host.Forgotten);
        Assert.True(h.LeaseIsFree());
        Assert.Null(h.Host.Start);
        Assert.Empty(h.Terminals());
    }

    [Fact]
    public void A_loaded_battle_is_rejected_while_a_saved_replay_is_starting()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        var gate = new TaskCompletionSource();
        h.Host.ConfigureStart = start => start.BootstrapGate = gate;
        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));

        Assert.False(h.Playback.CanReplaySavedCombats(out var reason));
        Assert.Equal("A saved replay is already starting.", reason);
        Assert.False(
            h.Playback.ReplayLoadedBattle(
                SavedReplayPlaybackHarness.Manifest(BattleB),
                SavedReplayPlaybackHarness.Payload(BattleB),
                recordVideo: false,
                CombatReplayPlaybackSource.ImportedGhost
            )
        );
        Assert.Equal(
            "replay_already_starting",
            ReasonCode(Assert.Single(h.LogLines("request_rejected")))
        );
        gate.SetResult();
    }

    [Fact]
    public void The_offer_probe_and_its_reasons()
    {
        using var h = new SavedReplayPlaybackHarness();
        Assert.True(h.Playback.CanReplaySavedCombats(out var reason));
        Assert.Equal(string.Empty, reason);
        Assert.False(h.Playback.CanReplaySavedBattle(" ", out reason));
        Assert.Equal("Select a saved battle to replay.", reason);
        Assert.False(h.Playback.CanReplaySavedBattle(BattleA, out reason));
        Assert.Equal("Replay payload for the selected battle is unavailable.", reason);
        h.Host.Offers.Add(BattleA);
        Assert.True(h.Playback.CanReplaySavedBattle(BattleA, out _));
        h.Services.RunContext.IsInGameRun = true;
        Assert.False(h.Playback.CanReplaySavedBattle(BattleA, out reason));
        Assert.Equal(
            "Saved replay playback is only available while you are outside an active gameplay session.",
            reason
        );
        h.Services.RunContext.IsInGameRun = false;
        h.Host.InReplayState = true;
        Assert.False(h.Playback.CanReplaySavedCombats(out reason));
        Assert.Equal("A replay is already in progress.", reason);
    }

    [Fact]
    public void After_dispose_every_runtime_member_answers_without_throwing()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        h.Host.Offers.Add(BattleA);
        h.Playback.Dispose();
        ISavedReplayRuntime runtime = h.Playback;

        Assert.False(runtime.ReplaySaved(BattleA, recordVideo: false));
        Assert.False(runtime.CanReplaySavedCombats(out var reason));
        Assert.Equal("Combat replay runtime is unavailable.", reason);
        Assert.False(runtime.CanReplaySavedBattle(BattleA, out reason));
        Assert.Equal("Combat replay runtime is unavailable.", reason);
        // Disposing the gate proves the answer comes before the gate is touched.
        h.Gate.Dispose();
        Assert.Equal(
            SavedBattleLoadOutcome.Unavailable,
            runtime.TryLoadSavedBattle(BattleA, out _, out _)
        );
        Assert.Equal(
            "runtime_unavailable",
            ReasonCode(Assert.Single(h.LogLines("request_rejected")))
        );
        Assert.False(
            runtime.ReplayLoadedBattle(
                SavedReplayPlaybackHarness.Manifest(BattleA),
                SavedReplayPlaybackHarness.Payload(BattleA),
                recordVideo: false,
                CombatReplayPlaybackSource.ImportedGhost
            )
        );
        Assert.False(runtime.HasSavedReplaySession);
        Assert.False(h.Playback.TryExitBootstrapped());
        h.Playback.Tick();
        h.Playback.OnReplayStateExited();
        Assert.Null(h.Host.Start);
    }

    // --- Happy path, notices and lease ---

    [Fact]
    public void A_saved_replay_plays_then_completes_on_state_exit()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);

        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: true));
        Assert.True(h.Playback.HoldsPayloadLease);
        Assert.False(h.LeaseIsFree());
        Assert.True(h.Playback.IsSavedReplayPlaybackActive);
        Assert.True(h.Playback.HasSavedReplaySession);
        Assert.Equal(
            new ActiveSavedReplay(BattleA, CombatReplayPlaybackSource.LocalSaved, true),
            h.Playback.Active
        );
        Assert.Single(h.LogLines("combat_replay.playback.started"));

        h.Playback.OnReplayStateExited();

        Assert.Equal(
            [
                $"notice:Accepted:{BattleA}",
                $"begin-start:{BattleA}",
                $"notice:StartBegun:{BattleA}",
                "start:prepare",
                "start:bootstrap",
                "start:opponent",
                "start:inject",
                $"starting:{BattleA}:record=True",
                $"ended:{BattleA}:state-exit:failed=False",
                $"cleanup:StateExit:first:{BattleA}",
                $"cleanup:StateExit:second:{BattleA}",
                $"notice:Completed:{BattleA}",
            ],
            h.Timeline
        );
        var terminal = h.SingleTerminal();
        Assert.Contains("combat_replay.playback.succeeded", terminal);
        Assert.Contains(" end_reason_code=state_exit", terminal);
        Assert.True(h.LeaseIsFree());
        Assert.False(h.Playback.HoldsPayloadLease);
        Assert.False(h.Playback.HasSavedReplaySession);
        Assert.Null(h.Playback.Active);
    }

    [Fact]
    public void A_failed_start_notices_cleans_up_in_its_order_and_frees_the_lease()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        h.Host.ConfigureStart = start =>
            start.BootstrapFailure = new InvalidOperationException("bootstrap boom");

        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));

        Assert.Equal(
            [
                $"notice:Accepted:{BattleA}",
                $"begin-start:{BattleA}",
                $"notice:StartBegun:{BattleA}",
                "start:prepare",
                "start:bootstrap",
                $"notice:StartFailed:{BattleA}",
                $"cleanup:StartFailed:first:{BattleA}",
                $"cleanup:StartFailed:second:{BattleA}",
                $"notice:Completed:{BattleA}",
            ],
            h.Timeline
        );
        var terminal = h.SingleTerminal();
        Assert.Contains("combat_replay.playback.failed", terminal);
        Assert.Contains(" end_reason_code=start_failed", terminal);
        Assert.Contains(" reason_code=start_exception", terminal);
        Assert.Contains(" rollback_status=not_required", terminal);
        Assert.True(h.LeaseIsFree());
        Assert.False(h.Playback.HasSavedReplaySession);
        Assert.Null(h.Publisher.ActiveSessionBattleId);
    }

    [Fact]
    public void A_start_that_cannot_begin_completes_and_frees_the_lease()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        h.Host.BeginStartFailure = new InvalidOperationException("loader boom");

        Assert.False(h.Playback.ReplaySaved(BattleA, recordVideo: false));

        Assert.Contains(" reason_code=start_exception", h.SingleTerminal());
        Assert.True(h.LeaseIsFree());
        Assert.False(h.Playback.HasSavedReplaySession);
        Assert.False(h.Playback.IsReplayStartInProgress);
        Assert.DoesNotContain(h.Host.Notices, n => n.Kind == SavedReplayNoticeKind.StartBegun);
        Assert.Equal(SavedReplayNoticeKind.Completed, h.Host.Notices[^1].Kind);
    }

    [Fact]
    public void The_start_runs_in_the_callers_context_and_holds_none_across_an_await()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        var gate = new TaskCompletionSource();
        h.Host.ConfigureStart = start =>
        {
            start.NeedsBootstrap = true;
            start.BootstrapGate = gate;
        };

        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));
        Assert.Equal(0, h.Host.ScopeDepth);
        var running = h.Host.Start!;
        Assert.Equal(1, running.DepthAt["prepare"]);
        Assert.Equal(1, running.DepthAt["bootstrap"]);
        Assert.True(h.Playback.IsReplayStartInProgress);

        gate.SetResult();

        Assert.Equal(0, running.DepthAt["bootstrap-resumed"]);
        Assert.Equal(0, running.DepthAt["inject"]);
        Assert.Equal(0, h.Host.ScopeDepth);
        Assert.False(h.Playback.IsReplayStartInProgress);
        Assert.True(h.Playback.IsSavedReplayPlaybackActive);
    }

    [Fact]
    public void Record_video_is_off_on_a_host_that_records_none()
    {
        using var h = new SavedReplayPlaybackHarness(recordsVideo: false);
        h.Save(BattleA);

        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: true));

        Assert.False(h.Playback.Active!.RecordVideo);
        Assert.Contains($"starting:{BattleA}:record=False", h.Timeline);
    }

    // --- Failure fold ---

    [Fact]
    public void A_failed_starting_publish_folds_to_its_reason_and_inner_exception()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        h.StartingPublishFailure = new InvalidOperationException("starting subscriber boom");

        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));

        var terminal = h.SingleTerminal();
        Assert.Contains(" reason_code=starting_publish_failed", terminal);
        Assert.Contains("starting subscriber boom", terminal);
        // The starting signal went out, so the failed start ends it.
        Assert.Contains($"ended:{BattleA}:start-failed:failed=True", h.Timeline);
        Assert.True(h.LeaseIsFree());
    }

    [Fact]
    public void A_failed_ended_publish_overrides_the_start_failure()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        h.StartingPublishFailure = new InvalidOperationException("starting boom");
        h.EndedPublishFailure = new InvalidOperationException("ended boom");

        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));

        var terminal = h.SingleTerminal();
        Assert.Contains(" reason_code=ended_publish_failed", terminal);
        Assert.Contains("ended boom", terminal);
        Assert.Contains($"cleanup:StartFailed:second:{BattleA}", h.Timeline);
        Assert.True(h.LeaseIsFree());
    }

    [Fact]
    public void A_wrapped_exception_reports_its_inner_cause()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        h.Host.ConfigureStart = start =>
            start.InjectFailure = new InvalidOperationException(
                "outer",
                new FormatException("inner cause")
            );

        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));

        var terminal = h.SingleTerminal();
        Assert.Contains(" reason_code=start_exception", terminal);
        Assert.Contains("inner cause", terminal);
        Assert.DoesNotContain("outer", terminal);
    }

    [Fact]
    public void Rollback_runs_only_after_a_bootstrap_attempt_and_its_failure_overrides()
    {
        using var noBootstrap = new SavedReplayPlaybackHarness();
        noBootstrap.Save(BattleA);
        noBootstrap.Host.ConfigureStart = start =>
            start.InjectFailure = new InvalidOperationException("inject boom");
        Assert.True(noBootstrap.Playback.ReplaySaved(BattleA, recordVideo: false));
        Assert.Equal(0, noBootstrap.Host.Start!.Rollbacks);
        Assert.Contains(" rollback_status=not_required", noBootstrap.SingleTerminal());

        using var rolledBack = new SavedReplayPlaybackHarness();
        rolledBack.Save(BattleA);
        rolledBack.Host.ConfigureStart = start =>
        {
            start.NeedsBootstrap = true;
            start.InjectFailure = new InvalidOperationException("inject boom");
        };
        Assert.True(rolledBack.Playback.ReplaySaved(BattleA, recordVideo: false));
        Assert.Equal(1, rolledBack.Host.Start!.Rollbacks);
        var succeeded = rolledBack.SingleTerminal();
        Assert.Contains(" rollback_status=succeeded", succeeded);
        Assert.Contains(" reason_code=start_exception", succeeded);

        using var failed = new SavedReplayPlaybackHarness();
        failed.Save(BattleA);
        failed.Host.ConfigureStart = start =>
        {
            start.NeedsBootstrap = true;
            start.BootstrapFailure = new InvalidOperationException("bootstrap boom");
            start.Rollback = ReplayBootstrapRollback.Failure(
                new InvalidOperationException("rollback boom")
            );
        };
        Assert.True(failed.Playback.ReplaySaved(BattleA, recordVideo: false));
        var rollbackFailed = failed.SingleTerminal();
        Assert.Contains(" rollback_status=failed", rollbackFailed);
        Assert.Contains(" reason_code=bootstrap_rollback_failed", rollbackFailed);
        Assert.Contains("rollback boom", rollbackFailed);
        Assert.True(failed.LeaseIsFree());

        using var thrown = new SavedReplayPlaybackHarness();
        thrown.Save(BattleA);
        thrown.Host.ConfigureStart = start =>
        {
            start.NeedsBootstrap = true;
            start.BootstrapFailure = new InvalidOperationException("bootstrap boom");
            start.RollbackThrows = new InvalidOperationException("rollback threw");
        };
        Assert.True(thrown.Playback.ReplaySaved(BattleA, recordVideo: false));
        Assert.Contains(" reason_code=bootstrap_rollback_failed", thrown.SingleTerminal());
        Assert.False(thrown.Playback.IsReplayStartInProgress);
    }

    [Fact]
    public void An_opponent_failure_only_degrades_the_replay()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        h.Host.ConfigureStart = start =>
            start.OpponentFailure = new InvalidOperationException("portrait boom");

        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));
        Assert.True(h.Playback.IsSavedReplayPlaybackActive);
        h.Playback.OnReplayStateExited();

        var terminal = h.SingleTerminal();
        Assert.Contains("combat_replay.playback.degraded", terminal);
        Assert.Contains(" reason_code=opponent_portrait_unavailable", terminal);
    }

    [Fact]
    public void A_state_exit_mid_start_interrupts_the_start_which_owns_the_terminal()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        var gate = new TaskCompletionSource();
        h.Host.ConfigureStart = start =>
        {
            start.BootstrappedFromLobby = true;
            start.InjectGate = gate;
        };
        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));

        h.Playback.OnReplayStateExited();
        Assert.Contains($"ended:{BattleA}:state-exit:failed=True", h.Timeline);
        Assert.Empty(h.Terminals());
        Assert.Equal(0, h.Host.MenuReturns);
        Assert.False(h.LeaseIsFree());

        gate.SetResult();

        var terminal = h.SingleTerminal();
        Assert.Contains(" end_reason_code=start_failed", terminal);
        Assert.Contains(" reason_code=start_exception", terminal);
        Assert.True(h.LeaseIsFree());
        Assert.False(h.Playback.HasSavedReplaySession);
    }

    // --- State exit, bootstrapped exit and menu return ---

    [Fact]
    public void A_state_exit_with_no_saved_replay_still_ends_another_session_and_cleans_up()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Publisher.BeginSession(
            "current-native",
            null,
            CombatReplayPlaybackSource.CurrentNative,
            recordVideo: false
        );
        Assert.True(h.Publisher.PublishStarting().Succeeded);

        h.Playback.OnReplayStateExited();

        Assert.Equal(
            [
                "starting:current-native:record=False",
                "ended:current-native:state-exit:failed=False",
                "cleanup:StateExit:first:",
                "cleanup:StateExit:second:",
            ],
            h.Timeline
        );
        Assert.Null(h.Publisher.ActiveSessionBattleId);
        Assert.Empty(h.Host.Notices);
        Assert.Empty(h.Terminals());
    }

    [Fact]
    public void A_failed_cleanup_step_is_observed_and_the_rest_still_run()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        h.Host.CleanupFailure = new InvalidOperationException("cleanup boom");
        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));

        h.Playback.OnReplayStateExited();

        Assert.Contains(" stage=second", Assert.Single(h.LogLines("cleanup_observed")));
        Assert.Contains("combat_replay.playback.succeeded", h.SingleTerminal());
        Assert.True(h.LeaseIsFree());
    }

    [Fact]
    public void A_bootstrapped_replay_returns_to_the_menu_and_completes_when_the_lobby_loads()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        h.Host.ConfigureStart = start =>
        {
            start.NeedsBootstrap = true;
            start.BootstrappedFromLobby = true;
        };
        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));

        h.Host.Now = 10f;
        h.Playback.OnReplayStateExited();
        Assert.Equal(1, h.Host.MenuReturns);
        Assert.Empty(h.Terminals());
        Assert.False(h.LeaseIsFree());
        Assert.True(h.Playback.HasSavedReplaySession);
        var started = h.Host.Notices.Single(n => n.Kind == SavedReplayNoticeKind.MenuReturnStarted);
        Assert.Equal(ReplayPlaybackEndReasonCode.StateExit, started.EndReason);

        h.Playback.Tick();
        Assert.Empty(h.Terminals());
        h.Host.LobbyLoaded = true;
        h.Host.Now = 11f;
        h.Playback.Tick();

        var settled = h.Host.Notices.Single(n => n.Kind == SavedReplayNoticeKind.MenuReturnSettled);
        Assert.False(settled.TimedOut);
        Assert.Contains("combat_replay.playback.succeeded", h.SingleTerminal());
        Assert.True(h.LeaseIsFree());
        Assert.False(h.Playback.HasSavedReplaySession);
    }

    [Fact]
    public void A_menu_return_that_never_lands_times_out_as_a_failure()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        h.Host.ConfigureStart = start => start.BootstrappedFromLobby = true;
        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));
        h.Host.Now = 100f;
        h.Playback.OnReplayStateExited();

        h.Host.Now = 114.9f;
        h.Playback.Tick();
        Assert.Empty(h.Terminals());
        h.Host.Now = 115f;
        h.Playback.Tick();

        Assert.True(
            h.Host.Notices.Single(n => n.Kind == SavedReplayNoticeKind.MenuReturnSettled).TimedOut
        );
        var terminal = h.SingleTerminal();
        Assert.Contains(" reason_code=menu_return_failed", terminal);
        Assert.True(h.LeaseIsFree());
    }

    [Fact]
    public void A_menu_return_that_cannot_dispatch_completes_at_once()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        h.Host.ConfigureStart = start => start.BootstrappedFromLobby = true;
        h.Host.MenuReturnFailure = new InvalidOperationException("RunManager is unavailable.");
        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));

        h.Playback.OnReplayStateExited();

        Assert.Contains(" reason_code=menu_return_failed", h.SingleTerminal());
        Assert.True(h.LeaseIsFree());
        Assert.False(h.Playback.HasSavedReplaySession);
        h.Host.Now = 500f;
        h.Playback.Tick();
        Assert.Single(h.Terminals());
        Assert.DoesNotContain(
            h.Host.Notices,
            n => n.Kind == SavedReplayNoticeKind.MenuReturnStarted
        );
    }

    [Fact]
    public void The_bootstrapped_exit_proceeds_once_then_suppresses_repeats_inside_the_window()
    {
        using var h = new SavedReplayPlaybackHarness();
        Assert.False(h.Playback.TryExitBootstrapped());
        Assert.Equal(SavedReplayExitOutcome.NotActive, h.Host.Notices.Single().ExitOutcome);
        h.Host.Notices.Clear();

        h.Save(BattleA);
        h.Host.ConfigureStart = start => start.BootstrappedFromLobby = true;
        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));
        h.Host.InReplayState = true;
        h.Host.Now = 50f;

        Assert.True(h.Playback.TryExitBootstrapped());
        Assert.Contains($"ended:{BattleA}:saved-replay-exit:failed=False", h.Timeline);
        Assert.Contains($"cleanup:BootstrappedExit:second:{BattleA}", h.Timeline);
        Assert.Equal(1, h.Host.MenuReturns);
        Assert.Equal(
            ReplayPlaybackEndReasonCode.SavedReplayExit,
            h.Host.Notices.Single(n => n.Kind == SavedReplayNoticeKind.MenuReturnStarted).EndReason
        );

        h.Host.Now = 51f;
        Assert.True(h.Playback.TryExitBootstrapped());
        Assert.Equal(1, h.Host.MenuReturns);
        Assert.Equal(
            [SavedReplayExitOutcome.Proceed, SavedReplayExitOutcome.Suppressed],
            h.Host.Notices.Where(n => n.Kind == SavedReplayNoticeKind.ExitRequested)
                .Select(n => n.ExitOutcome!.Value)
        );

        // The game leaves ReplayState through the menu return; the lobby confirms it.
        h.Host.InReplayState = false;
        h.Host.LobbyLoaded = true;
        h.Playback.Tick();
        var terminal = h.SingleTerminal();
        Assert.Contains(" end_reason_code=saved_replay_exit", terminal);
        Assert.True(h.LeaseIsFree());

        // ReplayState is gone, so the latch is clear and nothing is left to exit.
        h.Host.InReplayState = true;
        Assert.False(h.Playback.TryExitBootstrapped());
    }

    [Fact]
    public void An_idle_tick_enters_no_host_context_and_reads_no_game_state()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Playback.Tick();
        h.Host.InReplayState = true;
        h.Playback.Tick();
        Assert.Equal(0, h.Host.LobbyReads);
        Assert.Equal(0, h.Host.ReplayStateReads);
        Assert.Equal(0, h.Host.ScopeEntries);
        Assert.Empty(h.Host.Notices);
    }

    [Fact]
    public void A_latched_exit_keeps_the_tick_polling_until_ReplayState_is_gone()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        h.Host.ConfigureStart = start => start.BootstrappedFromLobby = true;
        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));
        h.Host.InReplayState = true;
        h.Host.MenuReturnFailure = new InvalidOperationException("menu return failed");
        // The failed dispatch drops the pending menu return; only the exit latch is left.
        Assert.True(h.Playback.TryExitBootstrapped());
        h.Host.Now = 2f;
        Assert.True(h.Playback.TryExitBootstrapped());

        h.Playback.Tick();
        Assert.True(h.Playback.TryExitBootstrapped());
        Assert.Equal(
            SavedReplayExitOutcome.Suppressed,
            h.Host.Notices.Last(n => n.Kind == SavedReplayNoticeKind.ExitRequested).ExitOutcome
        );

        h.Host.InReplayState = false;
        h.Playback.Tick();
        h.Host.InReplayState = true;
        Assert.False(h.Playback.TryExitBootstrapped());

        var reads = h.Host.ReplayStateReads;
        h.Playback.Tick();
        Assert.Equal(reads, h.Host.ReplayStateReads);
    }

    // --- Displacement and teardown ---

    [Fact]
    public void A_loaded_battle_during_a_pending_menu_return_frees_the_displaced_lease()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        h.Host.ConfigureStart = start => start.BootstrappedFromLobby = true;
        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));
        h.Playback.OnReplayStateExited();
        Assert.False(h.LeaseIsFree());

        h.Host.ConfigureStart = null;
        Assert.True(
            h.Playback.ReplayLoadedBattle(
                SavedReplayPlaybackHarness.Manifest(BattleB),
                SavedReplayPlaybackHarness.Payload(BattleB),
                recordVideo: false,
                CombatReplayPlaybackSource.ImportedGhost
            )
        );
        Assert.True(h.LeaseIsFree());
        Assert.False(h.Playback.HoldsPayloadLease);
        Assert.Equal(BattleB, h.Playback.Active!.BattleId);

        // The parked menu return still settles the displaced battle.
        h.Host.LobbyLoaded = true;
        h.Playback.Tick();
        Assert.Single(h.Terminals());
        Assert.Equal(BattleB, h.Playback.Active!.BattleId);
    }

    [Fact]
    public void A_loaded_battle_over_a_playing_one_frees_its_lease_without_completing_it()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));
        Assert.True(h.Playback.IsSavedReplayPlaybackActive);

        Assert.True(
            h.Playback.ReplayLoadedBattle(
                SavedReplayPlaybackHarness.Manifest(BattleB),
                SavedReplayPlaybackHarness.Payload(BattleB),
                recordVideo: false,
                CombatReplayPlaybackSource.ImportedGhost
            )
        );

        Assert.True(h.LeaseIsFree());
        Assert.Empty(h.Terminals());
        Assert.Equal(
            new ActiveSavedReplay(BattleB, CombatReplayPlaybackSource.ImportedGhost, false),
            h.Playback.Active
        );
    }

    [Fact]
    public void Dispose_mid_start_completes_the_operation_and_the_late_start_is_harmless()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        var gate = new TaskCompletionSource();
        h.Host.ConfigureStart = start => start.BootstrapGate = gate;
        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));

        h.Playback.Dispose();

        var terminal = h.SingleTerminal();
        Assert.Contains(" end_reason_code=runtime_destroyed", terminal);
        Assert.True(h.LeaseIsFree());
        Assert.Equal(0, h.Host.ScopeDepth);

        gate.SetResult();
        Assert.Single(h.Terminals());
        Assert.True(h.LeaseIsFree());
    }

    [Fact]
    public void Dispose_during_a_pending_menu_return_completes_and_frees_the_lease()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        h.Host.ConfigureStart = start => start.BootstrappedFromLobby = true;
        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));
        h.Playback.OnReplayStateExited();

        h.Playback.Dispose();

        Assert.Contains(" end_reason_code=runtime_destroyed", h.SingleTerminal());
        Assert.True(h.LeaseIsFree());
        Assert.False(h.Playback.HasSavedReplaySession);
    }

    // --- Desktop managed recording restart ---

    [Fact]
    public void A_playing_replay_is_promoted_to_recording()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        Assert.False(h.Playback.TryPromoteActiveToRecording());
        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));

        Assert.True(h.Playback.TryPromoteActiveToRecording());
        Assert.True(h.Playback.Active!.RecordVideo);
    }

    [Fact]
    public void A_failed_recording_restart_ends_completes_frees_then_parks_for_the_state_exit()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));
        h.Timeline.Clear();
        var cause = new InvalidOperationException("restart boom");

        var failure = h.Playback.FailActiveRecordingRestart(
            "recording-restart-failed",
            ReplayPlaybackReasonCode.RecordingRestartInvokeFailed,
            cause
        );

        Assert.Same(cause, failure);
        Assert.Equal(
            [
                $"ended:{BattleA}:recording-restart-failed:failed=True",
                $"notice:Completed:{BattleA}",
            ],
            h.Timeline
        );
        var terminal = h.SingleTerminal();
        Assert.Contains(" end_reason_code=start_failed", terminal);
        Assert.Contains(" reason_code=recording_restart_invoke_failed", terminal);
        Assert.True(h.LeaseIsFree());
        Assert.Null(h.Playback.Active);

        // The parked operation names the next state exit's cleanup, which writes no second terminal.
        h.Playback.OnReplayStateExited();
        Assert.Contains($"cleanup:StateExit:first:{BattleA}", h.Timeline);
        Assert.Single(h.Terminals());
    }

    [Fact]
    public void A_failed_ended_publish_overrides_a_recording_restart_failure()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(BattleA);
        Assert.True(h.Playback.ReplaySaved(BattleA, recordVideo: false));
        var ended = new InvalidOperationException("ended boom");
        h.EndedPublishFailure = ended;

        var failure = h.Playback.FailActiveRecordingRestart(
            "recording-restart-failed",
            ReplayPlaybackReasonCode.RecordingRestartPublishFailed,
            new InvalidOperationException("restart boom")
        );

        Assert.Same(ended, failure);
        Assert.Contains(" reason_code=ended_publish_failed", h.SingleTerminal());
    }

    private static string ReasonCode(string line)
    {
        const string key = " reason_code=";
        var start = line.IndexOf(key, StringComparison.Ordinal) + key.Length;
        var end = line.IndexOf(' ', start);
        return end < 0 ? line[start..] : line[start..end];
    }
}
