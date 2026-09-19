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
        Assert.Null(plan.BlockerName);
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
        Assert.Equal("native-info", plan.BlockerName);
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
        Assert.Equal("wall", plan.BlockerName);
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
}
