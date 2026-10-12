using Xunit;

namespace Architecture.Tests;

public sealed class CombatStatusBarRenderingTests
{
    [Fact]
    public void Controls_render_above_the_scene_without_using_portrait_depth_or_sorting()
    {
        foreach (var build in CompiledArtifacts.Builds)
        {
            var controls = build.Type("BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBar");
            Assert.Contains(
                controls.Instructions,
                instruction => instruction.Targets("UnityEngine.Canvas", "set_worldCamera")
            );
            Assert.Contains(
                controls.Instructions,
                instruction => instruction.Targets("TheBazaar.ObjectLayers", "get_aboveBoardLayer")
            );
            Assert.Contains(
                controls.Instructions,
                instruction => instruction.Targets("UnityEngine.Canvas", "get_planeDistance")
            );
            Assert.DoesNotContain(
                controls.Instructions,
                instruction =>
                    instruction.Targets("UnityEngine.Renderer", "get_sortingOrder")
                    || instruction.Targets("UnityEngine.Renderer", "get_bounds")
            );
        }
    }
}
