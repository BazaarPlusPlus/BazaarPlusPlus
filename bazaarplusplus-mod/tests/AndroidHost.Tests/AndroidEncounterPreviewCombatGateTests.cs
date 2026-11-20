#nullable enable
using System.Reflection;
using Architecture.Tests;
using Xunit;

namespace AndroidHost.Tests;

// Data.IsInCombat requires IL2CPP, so inspect the compiled read here.
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
