#nullable enable
using System.Reflection;
using Architecture.Tests;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The desktop's <c>EncounterPreviewModule</c> returns no preview in combat, so its event and
/// hero-level tooltips show only native content in a fight. Android's renderers read the same
/// flag. <c>Data.IsInCombat</c> needs the IL2CPP runtime, so the compiled read is held here
/// (ADR-0009: compiled artifacts, never source text).
/// </summary>
public sealed class AndroidEncounterPreviewCombatGateTests
{
    private const string Preview = "BazaarPlusPlus.Android.Game.AndroidEncounterPreview";

    private static readonly Lazy<CompiledAssembly> Host = new(() =>
        CompiledAssembly.Load(
            typeof(AndroidEncounterPreviewCombatGateTests)
                .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "BppAndroidHostAssembly")
                .Value!
        )
    );

    [Theory]
    [InlineData("Render")]
    [InlineData("RenderLevelUp")]
    public void A_preview_renderer_returns_nothing_in_combat(string method)
    {
        var reads = Host
            .Value.Types.Single(type => type.FullName == Preview)
            .Methods.Single(candidate =>
                candidate.Name == method && candidate.DeclaringType == Preview
            )
            .Instructions.Count(op => op.Targets("Il2CppTheBazaar.Data", "get_IsInCombat"));

        Assert.Equal(1, reads);
    }
}
