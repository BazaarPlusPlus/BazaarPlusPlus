#nullable enable
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The Android host compiles against the Loader Release, the Interop Snapshot, and the shared
/// assemblies (ADR-0012). A reference to BepInEx or to the desktop host assembly would load
/// nothing on a device, and a linked desktop source would fork shared code, so both are caught
/// here, over the compiled artifact, rather than by a failed launch.
/// </summary>
public sealed class AndroidHostAssemblyBoundaryTests
{
    private static readonly string[] SharedAssemblies =
    {
        "BazaarPlusPlus.Core",
        "BazaarPlusPlus.ModApi",
        "BazaarPlusPlus.Storage",
    };

    [Fact]
    public void Android_host_references_the_loader_and_core_and_never_a_desktop_assembly()
    {
        var references = ReferencedAssemblyNames();

        Assert.Contains("MelonLoader", references);
        Assert.Contains("Il2CppInterop.Runtime", references);
        Assert.Contains("BazaarPlusPlus.Core", references);
        Assert.DoesNotContain(
            references,
            reference => reference.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)
        );
        Assert.DoesNotContain("BazaarPlusPlus", references);
        Assert.DoesNotContain(
            references,
            reference =>
                reference.StartsWith("BazaarPlusPlus.", StringComparison.Ordinal)
                && !SharedAssemblies.Contains(reference)
        );
    }

    [Fact]
    public void Android_host_defines_only_its_own_types()
    {
        // A Compile-Include of a Core or desktop file keeps that file's namespace, so any BPP type
        // outside BazaarPlusPlus.Android is a linked source that should come from Core.
        using var reader = OpenHost();
        var metadata = reader.GetMetadataReader();
        var foreign = metadata
            .TypeDefinitions.Select(handle => metadata.GetTypeDefinition(handle))
            .Where(type => type.GetDeclaringType().IsNil)
            .Select(type => metadata.GetString(type.Namespace))
            .Where(ns =>
                (
                    ns == "BazaarPlusPlus"
                    || ns.StartsWith("BazaarPlusPlus.", StringComparison.Ordinal)
                )
                && ns != "BazaarPlusPlus.Android"
                && !ns.StartsWith("BazaarPlusPlus.Android.", StringComparison.Ordinal)
            )
            .Distinct()
            .ToArray();

        Assert.True(
            foreign.Length == 0,
            "The Android host compiles BPP sources outside its namespace: "
                + string.Join(", ", foreign)
        );
    }

    [Fact]
    public void Android_updater_references_only_melonloader_and_the_bcl()
    {
        // ADR-0014: the Updater runs before the Mods it replaces load, and a changed Updater
        // reaches users only through a new APK.
        using var reader = Open("BppUpdaterAssembly");
        var metadata = reader.GetMetadataReader();
        var foreign = metadata
            .AssemblyReferences.Select(handle =>
                metadata.GetString(metadata.GetAssemblyReference(handle).Name)
            )
            .Where(name =>
                name != "MelonLoader" && !name.StartsWith("System.", StringComparison.Ordinal)
            )
            .ToArray();

        Assert.True(
            foreign.Length == 0,
            "The Android Updater references more than its loader and the BCL: "
                + string.Join(", ", foreign)
        );
    }

    private static IReadOnlyList<string> ReferencedAssemblyNames()
    {
        using var reader = OpenHost();
        var metadata = reader.GetMetadataReader();
        return metadata
            .AssemblyReferences.Select(handle =>
                metadata.GetString(metadata.GetAssemblyReference(handle).Name)
            )
            .ToArray();
    }

    private static PEReader OpenHost() => Open("BppAndroidHostAssembly");

    private static PEReader Open(string key)
    {
        var path = typeof(AndroidHostAssemblyBoundaryTests)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == key)
            .Value;
        Assert.True(File.Exists(path), $"'{key}' was not built at '{path}'.");
        return new PEReader(File.OpenRead(path!));
    }
}
