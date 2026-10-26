using BazaarPlusPlus.Game.Settings;
using Xunit;

namespace BazaarPlusPlus.Tests.SettingsDockRegistry;

public class BppCollectionDockButtonLayoutPlannerTests
{
    private static readonly BppDockButtonBounds Viewport = new(0f, 1200f, 0f, 900f);
    private static readonly BppDockButtonBounds Gear = BppDockButtonBounds.FromCenter(
        1100f,
        100f,
        100f,
        100f
    );

    [Fact]
    public void Resolve_places_collection_directly_above_gear_when_clear()
    {
        var plan = BppCollectionDockButtonLayoutPlanner.Resolve(
            Viewport,
            Gear,
            collectionWidth: 100f,
            collectionHeight: 100f,
            gap: 20f,
            Array.Empty<BppDockButtonObstacle>()
        );

        Assert.True(plan.CanApply);
        Assert.Equal(1100f, plan.Bounds.CenterX);
        Assert.Equal(220f, plan.Bounds.CenterY);
        Assert.Null(plan.Blocker);
    }

    [Fact]
    public void Resolve_stacks_above_a_visible_native_blocker()
    {
        var blocker = new BppDockButtonObstacle(
            "native-info",
            BppDockButtonBounds.FromCenter(1100f, 220f, 100f, 100f),
            isActive: true
        );

        var plan = BppCollectionDockButtonLayoutPlanner.Resolve(
            Viewport,
            Gear,
            100f,
            100f,
            20f,
            new[] { blocker }
        );

        Assert.True(plan.CanApply);
        Assert.Equal(340f, plan.Bounds.CenterY);
        Assert.Equal("native-info", plan.Blocker?.Name);
    }

    [Fact]
    public void Resolve_refuses_to_place_collection_outside_viewport()
    {
        var blocker = new BppDockButtonObstacle(
            "wall",
            new BppDockButtonBounds(1000f, 1200f, 150f, 900f),
            isActive: true
        );

        var plan = BppCollectionDockButtonLayoutPlanner.Resolve(
            Viewport,
            Gear,
            100f,
            100f,
            20f,
            new[] { blocker }
        );

        Assert.False(plan.CanApply);
        Assert.Equal("wall", plan.Blocker?.Name);
    }

    [Fact]
    public void Resolve_reports_an_unnamed_scanned_blocker_by_source_ahead_of_the_viewport()
    {
        // Scanned native buttons carry only a source index; the caller names the one that blocks.
        var blocker = new BppDockButtonObstacle(
            name: null,
            new BppDockButtonBounds(1000f, 1200f, 150f, 900f),
            isActive: true,
            sourceIndex: 3
        );

        var plan = BppCollectionDockButtonLayoutPlanner.Resolve(
            Viewport,
            Gear,
            100f,
            100f,
            20f,
            new[] { blocker }
        );

        Assert.False(plan.CanApply);
        Assert.Equal(3, plan.Blocker?.SourceIndex);
    }

    [Fact]
    public void Resolve_clamps_collection_horizontally_inside_viewport()
    {
        var edgeGear = BppDockButtonBounds.FromCenter(1190f, 100f, 100f, 100f);

        var plan = BppCollectionDockButtonLayoutPlanner.Resolve(
            Viewport,
            edgeGear,
            140f,
            100f,
            20f,
            Array.Empty<BppDockButtonObstacle>()
        );

        Assert.True(plan.CanApply);
        Assert.Equal(1130f, plan.Bounds.CenterX);
        Assert.True(Viewport.Contains(plan.Bounds));
    }

    [Fact]
    public void Resolve_places_collection_below_gear_when_top_edge_blocks_upward_placement()
    {
        var topGear = BppDockButtonBounds.FromCenter(1100f, 840f, 100f, 100f);

        var plan = BppCollectionDockButtonLayoutPlanner.Resolve(
            Viewport,
            topGear,
            collectionWidth: 100f,
            collectionHeight: 100f,
            gap: 20f,
            Array.Empty<BppDockButtonObstacle>()
        );

        Assert.True(plan.CanApply);
        Assert.Equal(720f, plan.Bounds.CenterY);
        Assert.True(Viewport.Contains(plan.Bounds));
    }

    [Theory]
    [InlineData(100f)]
    [InlineData(840f)]
    public void Panel_and_replay_entries_keep_distinct_slots_near_either_screen_edge(float gearY)
    {
        var gear = BppDockButtonBounds.FromCenter(1100f, gearY, 100f, 100f);
        var collection = BppCollectionDockButtonLayoutPlanner.Resolve(
            Viewport,
            gear,
            100f,
            100f,
            20f,
            []
        );
        var collectionBlocker = new BppDockButtonObstacle("collection", collection.Bounds, true);
        var builds = BppCollectionDockButtonLayoutPlanner.Resolve(
            Viewport,
            gear,
            100f,
            100f,
            20f,
            [collectionBlocker]
        );
        var replay = BppCollectionDockButtonLayoutPlanner.Resolve(
            Viewport,
            builds.Bounds,
            100f,
            100f,
            20f,
            [collectionBlocker, new BppDockButtonObstacle("gear", gear, true)]
        );

        Assert.True(collection.CanApply);
        Assert.True(builds.CanApply);
        Assert.True(replay.CanApply);
        var slots = new[] { gear, collection.Bounds, builds.Bounds, replay.Bounds };
        for (var index = 0; index < slots.Length; index++)
        {
            Assert.True(Viewport.Contains(slots[index]));
            for (var next = index + 1; next < slots.Length; next++)
                Assert.False(slots[index].Overlaps(slots[next]));
        }
    }

    [Fact]
    public void ResolveBeside_places_the_button_left_of_the_gear_on_its_row()
    {
        var plan = BppCollectionDockButtonLayoutPlanner.ResolveBeside(
            Viewport,
            Gear,
            width: 100f,
            height: 100f,
            gap: 20f,
            Array.Empty<BppDockButtonObstacle>()
        );

        Assert.True(plan.CanApply);
        Assert.Equal(980f, plan.Bounds.CenterX);
        Assert.Equal(100f, plan.Bounds.CenterY);
        Assert.Null(plan.Blocker);
    }

    [Fact]
    public void ResolveBeside_places_nothing_when_the_slot_next_to_the_gear_is_blocked()
    {
        var blocker = new BppDockButtonObstacle(
            "native-currency",
            BppDockButtonBounds.FromCenter(980f, 100f, 100f, 100f),
            isActive: true
        );

        var plan = BppCollectionDockButtonLayoutPlanner.ResolveBeside(
            Viewport,
            Gear,
            100f,
            100f,
            20f,
            new[] { blocker }
        );

        Assert.False(plan.CanApply);
        Assert.Equal(980f, plan.Bounds.CenterX);
        Assert.Equal("native-currency", plan.Blocker?.Name);
    }

    [Fact]
    public void ResolveBeside_refuses_a_slot_outside_the_viewport()
    {
        var gearAtEdge = BppDockButtonBounds.FromCenter(50f, 100f, 100f, 100f);

        var plan = BppCollectionDockButtonLayoutPlanner.ResolveBeside(
            Viewport,
            gearAtEdge,
            100f,
            100f,
            20f,
            Array.Empty<BppDockButtonObstacle>()
        );

        Assert.False(plan.CanApply);
        Assert.Equal("viewport", plan.Blocker?.Name);
    }
}
