#nullable enable
using Xunit;

namespace Architecture.Tests;

public sealed class StorageAssemblyBoundaryTests
{
    [Fact]
    public void Storage_assembly_does_not_reference_BepInEx()
    {
        var compiledReferences = typeof(BazaarPlusPlus.Storage.RunLog.IRunLogStore)
            .Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(
            compiledReferences,
            reference => reference.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)
        );
    }
}
