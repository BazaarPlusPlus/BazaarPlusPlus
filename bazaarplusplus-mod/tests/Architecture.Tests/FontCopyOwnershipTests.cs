using Xunit;

namespace Architecture.Tests;

public sealed class FontCopyOwnershipTests
{
    // TMP_FontAsset.OnDestroy destroys its material and atlas textures, and a copy from
    // Object.Instantiate shares both with its source: destroying the copy as it stands breaks
    // the game's own text until the next launch.
    [Fact]
    public void A_font_copy_lets_go_of_the_shared_material_and_atlas_before_it_is_destroyed()
    {
        foreach (var build in CompiledArtifacts.Builds)
        {
            var typography = build.Type("BazaarPlusPlus.GameInterop.Fonts.NativeGameTypography");
            Assert.Contains(
                typography.Instructions,
                instruction => instruction.Targets("TMPro.TMP_Asset", "set_material")
            );
            Assert.Contains(
                typography.Instructions,
                instruction => instruction.Targets("TMPro.TMP_FontAsset", "set_atlasTextures")
            );
        }
    }
}
