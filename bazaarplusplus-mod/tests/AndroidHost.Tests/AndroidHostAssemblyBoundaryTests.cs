#nullable enable
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The Android host compiles against the Loader Release and the Interop Snapshot only. A
/// reference to BepInEx or to the desktop host assembly would load nothing on a device, so it is
/// caught here, over the compiled artifact, rather than by a failed launch.
/// </summary>
public sealed class AndroidHostAssemblyBoundaryTests
{
    [Fact]
    public void Android_host_references_the_loader_and_never_a_desktop_assembly()
    {
        var references = ReferencedAssemblyNames();

        Assert.Contains("MelonLoader", references);
        Assert.Contains("Il2CppInterop.Runtime", references);
        Assert.DoesNotContain(
            references,
            reference => reference.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)
        );
        Assert.DoesNotContain("BazaarPlusPlus", references);
        Assert.DoesNotContain(
            references,
            reference =>
                reference.StartsWith("BazaarPlusPlus.", StringComparison.Ordinal)
                && reference != "BazaarPlusPlus.Android"
        );
    }

    private static IReadOnlyList<string> ReferencedAssemblyNames()
    {
        var path = typeof(AndroidHostAssemblyBoundaryTests)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "BppAndroidHostAssembly")
            .Value;
        Assert.True(File.Exists(path), $"The Android host was not built at '{path}'.");

        using var stream = File.OpenRead(path!);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        return metadata
            .AssemblyReferences.Select(handle =>
                metadata.GetString(metadata.GetAssemblyReference(handle).Name)
            )
            .ToArray();
    }
}
