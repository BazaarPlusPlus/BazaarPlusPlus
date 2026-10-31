#nullable enable
using BazaarPlusPlus.Mobile.History;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// History's modal layer on Android, run with fake overlays: the back key closes the top-most
/// open one (note, More, inspection, skills); a scene change closes only the overlays that rent
/// cards, so a note keeps its unsaved draft and More stays open; a close of the panel closes all.
/// </summary>
public sealed class AndroidHistoryOverlaysTests
{
    [Fact]
    public void A_scene_change_closes_the_card_overlays_and_keeps_the_note_and_more_open()
    {
        var rig = new Rig();
        rig.Note.IsOpen = true;
        rig.More.IsOpen = true;
        rig.Inspection.IsOpen = true;
        rig.Skills.IsOpen = true;

        var kept = rig.Overlays.SceneChanged("scene_changed");

        Assert.Equal(2, kept);
        Assert.True(rig.Note.IsOpen);
        Assert.True(rig.More.IsOpen);
        Assert.Equal(
            new[]
            {
                "close:inspection:scene_changed",
                "return:inspection",
                "close:skills:scene_changed",
                "return:skills",
            },
            rig.Events
        );
        Assert.True(rig.Overlays.AnyOpen);
    }

    // A card overlay returns its cards on a scene change even when it is not open, since the
    // rental destroys every card right after it.
    [Fact]
    public void A_scene_change_with_nothing_open_still_returns_every_card()
    {
        var rig = new Rig();

        Assert.Equal(0, rig.Overlays.SceneChanged("scene_changed"));

        Assert.Equal(new[] { "return:inspection", "return:skills" }, rig.Events);
        Assert.False(rig.Overlays.AnyOpen);
    }

    [Fact]
    public void The_back_key_closes_the_note_then_more_then_the_inspection_then_skills()
    {
        var rig = new Rig();
        foreach (var overlay in rig.All)
            overlay.IsOpen = true;

        for (var press = 0; press < 4; press++)
            Assert.True(rig.Overlays.DismissTop("back_key"));
        Assert.False(rig.Overlays.DismissTop("back_key"));

        Assert.Equal(
            new[]
            {
                "close:note:back_key",
                "close:more:back_key",
                "close:inspection:back_key",
                "close:skills:back_key",
            },
            rig.Events
        );
        Assert.False(rig.Overlays.AnyOpen);
    }

    [Fact]
    public void Closing_the_panel_closes_every_overlay_and_returns_every_card()
    {
        var rig = new Rig();
        rig.Note.IsOpen = true;
        rig.Skills.IsOpen = true;

        rig.Overlays.CloseAll("close_button");

        Assert.Equal(
            new[]
            {
                "close:note:close_button",
                "return:inspection",
                "close:skills:close_button",
                "return:skills",
            },
            rig.Events
        );
        Assert.False(rig.Overlays.AnyOpen);
    }

    private sealed class Rig
    {
        public Rig()
        {
            Note = new FakeOverlay("note", Events);
            More = new FakeOverlay("more", Events);
            Inspection = new FakeOverlay("inspection", Events);
            Skills = new FakeOverlay("skills", Events);
            Overlays = new TouchHistoryOverlays([Note, More], [Inspection, Skills]);
        }

        public List<string> Events { get; } = new();

        public FakeOverlay Note { get; }

        public FakeOverlay More { get; }

        public FakeOverlay Inspection { get; }

        public FakeOverlay Skills { get; }

        public FakeOverlay[] All => [Note, More, Inspection, Skills];

        public TouchHistoryOverlays Overlays { get; }
    }

    private sealed class FakeOverlay(string name, List<string> events) : IHistoryCardOverlay
    {
        public bool IsOpen { get; set; }

        public bool Close(string reason)
        {
            if (!IsOpen)
                return false;
            IsOpen = false;
            events.Add($"close:{name}:{reason}");
            return true;
        }

        public void ReturnCards() => events.Add($"return:{name}");
    }
}
