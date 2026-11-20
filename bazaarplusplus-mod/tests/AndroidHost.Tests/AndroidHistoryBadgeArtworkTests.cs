#nullable enable
using System.Reflection;
using Architecture.Tests;
using Xunit;

namespace AndroidHost.Tests;

// Non-additive scene loads unload the runtime badge atlas unless both its sprites and texture opt out.
public sealed class AndroidHistoryBadgeArtworkTests
{
    private const string Badges = "BazaarPlusPlus.Mobile.History.TouchHistoryBadges";

    private static readonly Lazy<CompiledAssembly> Host = new(() =>
        CompiledAssembly.Load(
            typeof(AndroidHistoryBadgeArtworkTests)
                .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "BppAndroidHostAssembly")
                .Value!
        )
    );

    [Fact]
    public void Historys_badge_artwork_survives_a_scene_loads_asset_unload()
    {
        var hideFlags = Host
            .Value.Types.Single(type => type.FullName == Badges)
            .Methods.Single(method => method.Name == "LoadAtlas" && method.DeclaringType == Badges)
            .Instructions.Count(op => op.Targets("UnityEngine.Object", "set_hideFlags"));

        Assert.Equal(2, hideFlags);
    }

    // Cut sprites before dropping the CPU texture copy because their meshes read those pixels.
    [Theory]
    [InlineData(Badges, "LoadAtlas")]
    [InlineData("BazaarPlusPlus.Mobile.Hud.TouchHudIcons", "GpuOnlySprite")]
    public void Decoded_artwork_drops_its_cpu_pixels_once_its_sprites_exist(
        string type,
        string method
    )
    {
        var instructions = Host
            .Value.Types.Single(candidate => candidate.FullName == type)
            .Methods.Single(candidate =>
                candidate.Name == method && candidate.DeclaringType == type
            )
            .Instructions.ToList();

        var lastSprite = instructions.FindLastIndex(op =>
            op.Targets("UnityEngine.Sprite", "Create")
        );
        var apply = instructions.FindLastIndex(op => op.Targets("UnityEngine.Texture2D", "Apply"));

        Assert.True(lastSprite >= 0, $"{type}.{method} cuts no sprite");
        Assert.True(
            apply > lastSprite,
            $"{type}.{method} must apply its texture after its last sprite is cut"
        );
    }
}
