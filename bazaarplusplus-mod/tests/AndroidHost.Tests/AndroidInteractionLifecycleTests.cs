using BazaarPlusPlus.Android.Host;
using Xunit;

namespace AndroidHost.Tests;

public sealed class AndroidInteractionLifecycleTests
{
    [Fact]
    public void Frame_preserves_guarded_steps_and_order()
    {
        var events = new List<string>();
        var operations = Steps(events) with
        {
            Guarded = (name, step) =>
            {
                events.Add("guard:" + name);
                step();
            },
        };

        new AndroidInteractionLifecycle(operations).Frame();

        Assert.Equal(
            new[]
            {
                "bind",
                "pump",
                "dev",
                "record",
                "replay",
                "panels",
                "mount",
                "guard:touch_ui",
                "touch",
                "guard:quest_preview",
                "quest",
                "guard:encounter_preview",
                "encounter",
                "guard:frame_rate",
                "frame_rate",
                "font",
                "settings",
                "rental",
                "version",
                "guard:update_prompt",
                "prompt",
                "label",
            },
            events
        );
    }

    [Fact]
    public void Scene_and_pause_preserve_independent_guards()
    {
        var events = new List<string>();
        var operations = Steps(events) with
        {
            Guarded = (name, step) =>
            {
                events.Add("guard:" + name);
                step();
            },
        };
        var lifecycle = new AndroidInteractionLifecycle(operations);

        lifecycle.SceneLoaded(true);
        lifecycle.PauseChanged(true);
        lifecycle.PauseChanged(false);

        Assert.Equal(
            new[]
            {
                "guard:scene_loaded:upgrade_preview",
                "release",
                "guard:scene_loaded:combat_status",
                "reset",
                "guard:scene_loaded:overlay_panels",
                "scene_panels",
                "guard:scene_loaded:version_check",
                "begin",
                "guard:app_lifecycle:pause",
                "pause_touch",
                "pause_record",
                "guard:app_lifecycle:resume",
                "resume_record",
            },
            events
        );
    }

    [Fact]
    public void Stop_retires_features_before_ui_history_replay_and_record_once()
    {
        var events = new List<string>();
        var lifecycle = new AndroidInteractionLifecycle(Steps(events));

        lifecycle.Stop();
        lifecycle.Stop();

        Assert.Equal(
            new[]
            {
                "retire",
                "stop_touch",
                "stop_history",
                "stop_replay",
                "stop_panels",
                "stop_font",
                "stop_pump",
                "stop_record",
            },
            events
        );
    }

    [Fact]
    public void Failed_start_rolls_back_and_history_failure_does_not_abort_interaction()
    {
        var events = new List<string>();
        var lifecycle = new AndroidInteractionLifecycle(Steps(events));

        lifecycle.Start(
            () =>
            {
                events.Add("partial");
                throw new InvalidOperationException();
            },
            () => events.Add("rollback"),
            _ => events.Add("start_failed")
        );
        lifecycle.Start(
            () =>
            {
                lifecycle.StartHistory(
                    () => throw new InvalidOperationException(),
                    () => events.Add("history_rollback"),
                    _ => events.Add("history_failed")
                );
                events.Add("interaction_continues");
            },
            () => events.Add("unexpected_rollback"),
            _ => events.Add("unexpected_failure")
        );

        Assert.Equal(
            new[]
            {
                "partial",
                "rollback",
                "start_failed",
                "history_rollback",
                "history_failed",
                "interaction_continues",
            },
            events
        );
    }

    [Fact]
    public void A_failed_scene_step_does_not_skip_panel_release()
    {
        var events = new List<string>();
        var lifecycle = new AndroidInteractionLifecycle(
            Steps(events) with
            {
                ReleaseUpgrade = () => throw new InvalidOperationException(),
                Guarded = (probe, step) =>
                {
                    try
                    {
                        step();
                    }
                    catch (InvalidOperationException)
                    {
                        events.Add("failed:" + probe);
                    }
                },
            }
        );

        lifecycle.SceneLoaded(false);

        Assert.Equal(
            new[] { "failed:scene_loaded:upgrade_preview", "reset", "scene_panels" },
            events
        );
    }

    [Fact]
    public void A_failed_stop_can_finish_on_the_second_loader_callback()
    {
        var events = new List<string>();
        var attempts = 0;
        var lifecycle = new AndroidInteractionLifecycle(
            Steps(events) with
            {
                StopTouch = () =>
                {
                    if (++attempts == 1)
                        throw new InvalidOperationException();
                    events.Add("stop_touch");
                },
            }
        );

        Assert.Throws<InvalidOperationException>(lifecycle.Stop);
        Assert.DoesNotContain("stop_record", events);
        lifecycle.Stop();

        Assert.Equal("stop_record", events.Last());
        Assert.Single(events, step => step == "stop_history");
        Assert.Single(events, step => step == "stop_replay");
    }

    private static AndroidInteractionOperations Steps(List<string> events)
    {
        Action Named(string name) => () => events.Add(name);
        return new AndroidInteractionOperations
        {
            BindMainThread = Named("bind"),
            Pump = Named("pump"),
            DevLogin = Named("dev"),
            Record = Named("record"),
            Replay = Named("replay"),
            Panels = Named("panels"),
            MountTouch = Named("mount"),
            Touch = Named("touch"),
            Quest = Named("quest"),
            Encounter = Named("encounter"),
            FrameRate = Named("frame_rate"),
            Font = Named("font"),
            Settings = Named("settings"),
            Rental = Named("rental"),
            Version = Named("version"),
            Prompt = Named("prompt"),
            RefreshLabel = Named("label"),
            ReleaseUpgrade = Named("release"),
            ResetCombat = Named("reset"),
            ScenePanels = Named("scene_panels"),
            BeginVersion = Named("begin"),
            PauseTouch = Named("pause_touch"),
            PauseRecord = Named("pause_record"),
            ResumeRecord = Named("resume_record"),
            RetireFeatures = Named("retire"),
            StopTouch = Named("stop_touch"),
            StopHistory = Named("stop_history"),
            StopReplay = Named("stop_replay"),
            StopPanels = Named("stop_panels"),
            StopFont = Named("stop_font"),
            StopPump = Named("stop_pump"),
            StopRecord = Named("stop_record"),
            Guarded = (_, step) => step(),
        };
    }
}
