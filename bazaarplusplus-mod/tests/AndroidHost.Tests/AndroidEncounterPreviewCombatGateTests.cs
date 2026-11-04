#nullable enable
using System.Reflection;
using Architecture.Tests;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// Core's <c>EncounterPreviewModule</c> returns no preview in combat, so the event and hero-level
/// tooltips show only native content in a fight. Android's reads answer its combat question with
/// the game's own flag. <c>Data.IsInCombat</c> needs the IL2CPP runtime, so the compiled read is
/// held here (ADR-0009: compiled artifacts, never source text).
/// </summary>
public sealed class AndroidEncounterPreviewCombatGateTests
{
    private const string Reads = "BazaarPlusPlus.Android.Game.AndroidEncounterPreviewReads";

    private static readonly Lazy<CompiledAssembly> Host = new(() =>
        CompiledAssembly.Load(
            typeof(AndroidEncounterPreviewCombatGateTests)
                .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "BppAndroidHostAssembly")
                .Value!
        )
    );

    [Fact]
    public void The_combat_read_is_the_games_flag()
    {
        var reads = Host
            .Value.Types.Single(type => type.FullName == Reads)
            .Methods.Single(candidate =>
                candidate.Name == "get_IsInCombat" && candidate.DeclaringType == Reads
            )
            .Instructions.Count(op => op.Targets("Il2CppTheBazaar.Data", "get_IsInCombat"));

        Assert.Equal(1, reads);
    }
}
