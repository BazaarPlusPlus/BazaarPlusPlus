using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.GameInterop;
using Xunit;

namespace RuntimeIntegration.Tests;

public sealed class GameBuildInfoResolverTests
{
    [Theory]
    [InlineData("1.0.12575-staging-macos-arm64-fecb8f8e", false)]
    [InlineData("1.0.12575-staging-macos-arm64-fecb8f8e", true)]
    [InlineData("1.0.12575-staging-macos-arm64-fecb8f8e", null)]
    [InlineData("1.0.12575-STAGING-windows-x64-fecb8f8e", false)]
    public void Staging_uses_the_persisted_test_channel_and_blocks_uploads(
        string version,
        bool? hasServerOption
    )
    {
        var build = GameBuildInfoResolver.Resolve(version, hasServerOption);

        Assert.Equal(GameBuildChannel.Ptr, build.Channel);
        Assert.Equal(version, build.RawVersion);
        Assert.Null(build.DetectionWarning);
        Assert.False(UploadPumpBootstrap.CanActivate(build.Channel));
    }

    [Theory]
    [InlineData("1.0.12292-prod-macos-arm64-a0455053", false, "Online", false)]
    [InlineData("1.0.12292-prod-macos-arm64-a0455053", null, "Online", false)]
    [InlineData("1.0.12292-prod-macos-arm64-a0455053", true, "Ptr", true)]
    [InlineData("1.0.11358-ptr-macos-arm64-947c079a", true, "Ptr", false)]
    [InlineData("1.0.11358-ptr-macos-arm64-947c079a", false, "Ptr", true)]
    [InlineData("1.0.11358-ptr-macos-arm64-947c079a", null, "Ptr", false)]
    [InlineData("", false, "Online", true)]
    [InlineData("", true, "Ptr", true)]
    [InlineData("", null, "Unknown", true)]
    public void Existing_channels_keep_their_fallback_and_upload_policy(
        string version,
        bool? hasServerOption,
        string expectedChannel,
        bool expectsWarning
    )
    {
        var build = GameBuildInfoResolver.Resolve(version, hasServerOption);

        Assert.Equal(expectedChannel, build.Channel.ToString());
        Assert.Equal(version, build.RawVersion);
        Assert.Equal(expectsWarning, build.DetectionWarning != null);
        Assert.Equal(expectedChannel != "Ptr", UploadPumpBootstrap.CanActivate(build.Channel));
    }
}
