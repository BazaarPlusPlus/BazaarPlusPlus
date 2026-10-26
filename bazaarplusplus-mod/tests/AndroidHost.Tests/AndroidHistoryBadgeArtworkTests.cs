#nullable enable
using System.Reflection;
using Architecture.Tests;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// A non-additive scene load unloads assets no live object references; History's badge atlases
/// are runtime textures, so both the texture and each sprite opt out of that unload (a replay's
/// round trip otherwise left the run rows on their "?" fallback). The atlas needs interop objects
/// (ADR-0016), so the compiled calls are held here (ADR-0009: compiled artifacts, never source
/// text).
/// </summary>
public sealed class AndroidHistoryBadgeArtworkTests
{
    private const string Badges = "BazaarPlusPlus.Android.Ui.AndroidHistoryBadges";

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

    /// <summary>
    /// History's atlases and the touch controls' artwork keep no CPU copy of their pixels: the
    /// texture is applied as no longer readable, and only after its sprites are cut, because a
    /// sprite's mesh is read from those pixels.
    /// </summary>
    [Theory]
    [InlineData(Badges, "LoadAtlas")]
    [InlineData("BazaarPlusPlus.Android.Ui.AndroidControlIcons", "GpuOnlySprite")]
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
