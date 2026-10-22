#nullable enable
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Storage is shared by every host (ADR-0012, ADR-0013), so neither compiled target may bind the
/// desktop loader.
/// </summary>
public sealed class StorageAssemblyBoundaryTests
{
    [Fact]
    public void Storage_targets_do_not_reference_BepInEx()
    {
        var violations = SharedAssemblies
            .TargetFrameworks.SelectMany(tfm =>
                SharedAssemblies
                    .ReferencedAssemblies(CompiledArtifacts.StorageAssembly, tfm)
                    .Where(reference =>
                        reference.Name!.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)
                    )
                    .Select(reference => $"{tfm}: {reference.FullName}")
            )
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "BazaarPlusPlus.Storage must not bind BepInEx:\n  " + string.Join("\n  ", violations)
        );
    }
}
