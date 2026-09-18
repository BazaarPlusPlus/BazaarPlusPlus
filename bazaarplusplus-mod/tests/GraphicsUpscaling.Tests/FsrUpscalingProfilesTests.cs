using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.GraphicsUpscaling;
using Xunit;

namespace GraphicsUpscaling.Tests;

public sealed class FsrUpscalingProfilesTests
{
    [Fact]
    public void ResolveMapsFsrPresetsToStandardRenderScales()
    {
        AssertProfile(GraphicsUpscalingMode.FsrUltraQuality, 0.77f);
        AssertProfile(GraphicsUpscalingMode.FsrQuality, 0.67f);
        AssertProfile(GraphicsUpscalingMode.FsrBalanced, 0.59f);
        AssertProfile(GraphicsUpscalingMode.FsrPerformance, 0.5f);
    }

    private static void AssertProfile(GraphicsUpscalingMode mode, float expectedRenderScale)
    {
        var profile = FsrUpscalingProfiles.Resolve(mode);

        Assert.True(profile.Enabled);
        Assert.Equal(expectedRenderScale, profile.RenderScale);
    }

    [Fact]
    public void ResolveKeepsNativeRenderingAtFullScale()
    {
        var profile = FsrUpscalingProfiles.Resolve(GraphicsUpscalingMode.Native);

        Assert.False(profile.Enabled);
        Assert.Equal(1f, profile.RenderScale);
    }
}
