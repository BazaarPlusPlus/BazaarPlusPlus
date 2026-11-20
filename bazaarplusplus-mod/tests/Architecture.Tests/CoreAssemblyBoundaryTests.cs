#nullable enable
using Xunit;

namespace Architecture.Tests;

public sealed class CoreAssemblyBoundaryTests
{
    private static readonly string[] ForbiddenPrefixes =
    {
        "UnityEngine",
        "Unity.",
        "BepInEx",
        "0Harmony",
        "MelonLoader",
        "Il2Cpp",
        "Assembly-CSharp",
        "TheBazaar",
        "BazaarGame",
        "BazaarBattleService",
        "MessagePack",
        "Microsoft.Data.Sqlite",
        "SQLitePCLRaw",
    };

    [Fact]
    public void Core_targets_reference_no_host_loader_engine_or_game_assembly()
    {
        var violations = SharedAssemblies
            .TargetFrameworks.SelectMany(tfm =>
                SharedAssemblies
                    .ReferencedAssemblies(CompiledArtifacts.CoreAssembly, tfm)
                    .Where(reference =>
                        reference.Name == CompiledArtifacts.MainAssembly
                        || ForbiddenPrefixes.Any(prefix =>
                            reference.Name!.StartsWith(prefix, StringComparison.Ordinal)
                        )
                    )
                    .Select(reference => $"{tfm}: {reference.FullName}")
            )
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "BazaarPlusPlus.Core must stay host-independent:\n  " + string.Join("\n  ", violations)
        );
    }
}
