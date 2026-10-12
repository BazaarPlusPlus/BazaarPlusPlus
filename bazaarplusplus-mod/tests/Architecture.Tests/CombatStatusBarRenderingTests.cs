using Xunit;

namespace Architecture.Tests;

public sealed class CombatStatusBarRenderingTests
{
    [Fact]
    public void Controls_use_the_native_render_camera_and_portrait_sorting()
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
                instruction => instruction.Targets("UnityEngine.Renderer", "get_sortingLayerID")
            );
            Assert.Contains(
                controls.Instructions,
                instruction => instruction.Targets("UnityEngine.Renderer", "get_sortingOrder")
            );
        }
    }
}
