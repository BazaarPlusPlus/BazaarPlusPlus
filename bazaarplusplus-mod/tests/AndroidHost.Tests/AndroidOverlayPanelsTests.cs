#nullable enable
using BazaarPlusPlus.Android.Ui;
using BazaarPlusPlus.Game.OverlayPanels;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The Android adapter of the Overlay Panel Host, run with fake panels: a scene load closes what
/// the scene policies close, tells every panel and releases the rented cards once; the back key
/// closes the open panel's modal, then the panel, then the touch menu, and one press seen twice
/// in a frame closes one surface; a switch-off closes the panel and releases its cards, a close
/// the panel asks for does not. The adapter runs no IL2CPP code: combat, the frame count and the
/// release are the host's delegates (ADR-0016 §3).
/// </summary>
public sealed class AndroidOverlayPanelsTests
{
    [Fact]
    public void A_scene_load_closes_the_menu_then_the_panel_tells_every_panel_and_releases_once()
    {
        var rig = new Rig();
        var panel = rig.Add("live_build");
        var other = rig.Add("collection");
        Assert.Equal(OverlayRequestOutcome.Executed, panel.Handle.RequestOpen("touch_menu"));
        rig.MenuOpen = true;
        rig.Events.Clear();

        rig.Panels.SceneLoaded();

        Assert.Equal(
            new[]
            {
                "menu:scene_changed",
                "close:live_build:scene_changed",
                "scene:live_build",
                "scene:collection",
                "release:scene_changed",
            },
            rig.Events
        );
        Assert.False(panel.Handle.IsVisible);
        Assert.False(other.Handle.IsVisible);
    }

    [Fact]
    public void A_scene_load_with_nothing_open_still_releases_the_rented_cards()
    {
        var rig = new Rig();
        rig.Add("collection");

        rig.Panels.SceneLoaded();
        rig.Panels.SceneLoaded();

        Assert.Equal(2, rig.Events.Count(entry => entry == "release:scene_changed"));
    }

    // History's policy, the desktop's: it stays open across a scene change and closes only into
    // a combat.
    [Fact]
    public void An_only_when_in_combat_panel_survives_a_scene_load_outside_combat()
    {
        var rig = new Rig();
        var history = rig.Add("history", SceneChangeClosePolicy.OnlyWhenInCombat);
        history.Handle.RequestOpen("launcher");
        rig.Events.Clear();

        rig.Panels.SceneLoaded();

        Assert.True(history.Handle.IsVisible);
        Assert.Equal(new[] { "scene:history", "release:scene_changed" }, rig.Events);

        rig.InCombat = true;
        rig.Events.Clear();
        rig.Panels.SceneLoaded();

        Assert.False(history.Handle.IsVisible);
        Assert.Equal(
            new[] { "close:history:scene_changed", "scene:history", "release:scene_changed" },
            rig.Events
        );
    }

    [Fact]
    public void Combat_closes_the_open_panel_and_suppresses_an_open()
    {
        var rig = new Rig();
        var panel = rig.Add("collection");
        panel.Handle.RequestOpen("touch_menu");
        rig.InCombat = true;
        rig.Events.Clear();

        rig.Panels.Frame(0.016f);

        Assert.Equal(new[] { "close:collection:combat" }, rig.Events);
        Assert.Equal(
            OverlayRequestOutcome.SuppressedByCombat,
            panel.Handle.RequestOpen("touch_menu")
        );
        Assert.False(panel.Handle.IsVisible);
    }

    [Fact]
    public void Opening_a_panel_closes_the_open_one_as_superseded()
    {
        var rig = new Rig();
        var history = rig.Add("history", SceneChangeClosePolicy.OnlyWhenInCombat);
        var collection = rig.Add("collection");
        history.Handle.RequestOpen("launcher");
        rig.Events.Clear();

        Assert.Equal(OverlayRequestOutcome.Executed, collection.Handle.RequestOpen("touch_menu"));

        Assert.Equal(
            new[] { "close:history:superseded", "open:collection:touch_menu" },
            rig.Events
        );
        Assert.Equal("collection", rig.Panels.OpenPanelId);
    }

    [Fact]
    public void The_back_key_closes_the_modal_then_the_panel_then_the_menu_then_passes()
    {
        var rig = new Rig();
        var panel = rig.Add("history", SceneChangeClosePolicy.OnlyWhenInCombat);
        panel.Handle.RequestOpen("launcher");
        panel.Modal = true;
        rig.MenuOpen = true;
        rig.Events.Clear();

        Assert.True(rig.Back("back_key"));
        Assert.Equal(new[] { "dismiss:history" }, rig.Events);
        Assert.True(panel.Handle.IsVisible);

        Assert.True(rig.Back("back_key"));
        Assert.Equal("close:history:back_key", rig.Events[^1]);
        Assert.False(panel.Handle.IsVisible);

        Assert.True(rig.Back("back_key"));
        Assert.Equal("menu:back_key", rig.Events[^1]);

        Assert.False(rig.Back("back_key"));
        Assert.Equal(3, rig.Events.Count);
    }

    // Collection's back steps: an inspection that animates closed stays modal until it is gone,
    // then an expanded rail picker collapses, and only then does the panel close.
    [Fact]
    public void The_back_key_answers_every_modal_step_before_it_closes_the_panel()
    {
        var rig = new Rig();
        var steps = new Queue<string>(new[] { "inspection", "inspection_animating", "picker" });
        var handle = rig.Panels.Register(h => new OverlayPanelRegistration(
            "collection",
            onOpen: () => true,
            onClose: reason => rig.Events.Add($"close:collection:{h.Describe(reason)}"),
            tick: (_, _) => { }
        )
        {
            IsModalInputActive = () => steps.Count > 0,
            DismissModal = () => rig.Events.Add($"dismiss:{steps.Dequeue()}"),
        });
        handle.RequestOpen("touch_menu");
        rig.Events.Clear();

        Assert.True(rig.Back("back_key"));
        Assert.True(rig.Back("back_key"));
        Assert.True(rig.Back("back_key"));
        Assert.True(handle.IsVisible);
        Assert.True(rig.Back("back_key"));

        Assert.Equal(
            new[]
            {
                "dismiss:inspection",
                "dismiss:inspection_animating",
                "dismiss:picker",
                "close:collection:back_key",
            },
            rig.Events
        );
        Assert.False(handle.IsVisible);
    }

    // The game's escape hook and the touch root's keyboard read both see one press.
    [Fact]
    public void One_press_seen_twice_in_a_frame_closes_one_surface()
    {
        var rig = new Rig();
        var panel = rig.Add("collection");
        panel.Handle.RequestOpen("touch_menu");
        rig.MenuOpen = true;
        rig.Events.Clear();

        Assert.True(rig.Panels.TryConsumeBack("escape_hook"));
        Assert.True(rig.Panels.TryConsumeBack("keyboard"));

        Assert.Equal(new[] { "close:collection:back_key" }, rig.Events);
        Assert.True(rig.MenuOpen);
        Assert.Equal(
            new (string, object?)[]
            {
                ("back_key_presses", 1L),
                ("back_key_consumed", 1L),
                ("back_key_passed", 0L),
            },
            Fields(rig.Panels)
        );
    }

    [Fact]
    public void The_summary_counts_presses_once_per_frame()
    {
        var rig = new Rig();
        var panel = rig.Add("collection");
        panel.Handle.RequestOpen("touch_menu");

        rig.Back("escape_hook");
        rig.Frame++;
        Assert.False(rig.Panels.TryConsumeBack("escape_hook"));
        Assert.False(rig.Panels.TryConsumeBack("keyboard"));
        rig.Back("keyboard");

        Assert.Equal(
            new (string, object?)[]
            {
                ("back_key_presses", 3L),
                ("back_key_consumed", 1L),
                ("back_key_passed", 2L),
            },
            Fields(rig.Panels)
        );
    }

    [Fact]
    public void A_switch_off_closes_the_panel_and_releases_its_cards()
    {
        var rig = new Rig();
        var panel = rig.Add("live_build");
        panel.Handle.RequestOpen("touch_menu");
        rig.Events.Clear();

        panel.Enabled = false;
        rig.Panels.Frame(0.016f);

        Assert.Equal(
            new[] { "close:live_build:switched_off", "off:live_build", "release:switched_off" },
            rig.Events
        );
        Assert.Equal(OverlayRequestOutcome.Disabled, panel.Handle.RequestOpen("touch_menu"));
    }

    [Fact]
    public void A_close_the_panel_asks_for_keeps_its_reason_and_releases_nothing()
    {
        var rig = new Rig();
        var panel = rig.Add("collection");
        panel.Handle.RequestOpen("touch_menu");
        rig.Events.Clear();

        Assert.Equal(OverlayRequestOutcome.Executed, panel.Handle.RequestClose("close_button"));
        rig.Panels.Frame(0.016f);

        Assert.Equal(new[] { "close:collection:close_button" }, rig.Events);
    }

    [Fact]
    public void A_panel_that_cannot_open_stays_closed_and_the_launcher_reads_no_open_panel()
    {
        var rig = new Rig();
        var panel = rig.Add("collection");
        panel.Opens = false;

        Assert.Equal(OverlayRequestOutcome.OpenFailed, panel.Handle.RequestOpen("touch_menu"));

        Assert.False(panel.Handle.IsVisible);
        Assert.Null(rig.Panels.OpenPanelId);
    }

    [Fact]
    public void An_open_panel_ticks_and_a_disposed_one_leaves_without_a_close()
    {
        var rig = new Rig();
        var panel = rig.Add("collection");
        panel.Handle.RequestOpen("touch_menu");
        rig.Events.Clear();

        rig.Panels.Frame(0.5f);
        Assert.Equal(new[] { "tick:collection" }, rig.Events);

        panel.Handle.Dispose();
        rig.Events.Clear();
        rig.Panels.Frame(0.5f);
        rig.Panels.SceneLoaded();

        Assert.Null(rig.Panels.OpenPanelId);
        Assert.Equal(new[] { "release:scene_changed" }, rig.Events);
        Assert.Equal(OverlayRequestOutcome.UnknownPanel, panel.Handle.RequestOpen("touch_menu"));
    }

    private static (string, object?)[] Fields(AndroidOverlayPanels panels) =>
        panels.BackSummary().Select(field => (field.Name, (object?)field.Value)).ToArray();

    private sealed class Rig
    {
        public Rig()
        {
            Panels = new AndroidOverlayPanels(
                () => InCombat,
                reason => Events.Add($"release:{reason}"),
                () => Frame
            );
            Panels.SetTouchMenu(reason =>
            {
                if (!MenuOpen)
                    return false;
                MenuOpen = false;
                Events.Add($"menu:{reason}");
                return true;
            });
        }

        public AndroidOverlayPanels Panels { get; }

        public List<string> Events { get; } = new();

        public bool InCombat { get; set; }

        public bool MenuOpen { get; set; }

        public int Frame { get; set; }

        // A press on a frame of its own.
        public bool Back(string source)
        {
            Frame++;
            return Panels.TryConsumeBack(source);
        }

        public FakePanel Add(
            string id,
            SceneChangeClosePolicy policy = SceneChangeClosePolicy.Always
        ) => new(this, id, policy);
    }

    private sealed class FakePanel
    {
        public FakePanel(Rig rig, string id, SceneChangeClosePolicy policy)
        {
            Handle = rig.Panels.Register(handle => new OverlayPanelRegistration(
                id,
                onOpen: () =>
                {
                    if (!Opens)
                        return false;
                    rig.Events.Add($"open:{id}:{handle.OpenSource}");
                    return true;
                },
                onClose: reason => rig.Events.Add($"close:{id}:{handle.Describe(reason)}"),
                tick: (_, isOpen) =>
                {
                    if (isOpen)
                        rig.Events.Add($"tick:{id}");
                }
            )
            {
                SceneChangeClose = policy,
                Enabled = () => Enabled,
                IsModalInputActive = () => Modal,
                DismissModal = () =>
                {
                    Modal = false;
                    rig.Events.Add($"dismiss:{id}");
                },
                OnSceneChanged = () => rig.Events.Add($"scene:{id}"),
                OnSwitchedOff = () => rig.Events.Add($"off:{id}"),
            });
            rig.Events.Clear();
        }

        public AndroidPanelHandle Handle { get; }

        public bool Enabled { get; set; } = true;

        public bool Modal { get; set; }

        public bool Opens { get; set; } = true;
    }
}
