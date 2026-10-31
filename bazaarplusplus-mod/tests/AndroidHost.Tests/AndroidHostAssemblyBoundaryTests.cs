#nullable enable
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Architecture.Tests;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The Android host compiles against the Loader Release, the Interop Snapshot, and the shared
/// assemblies (ADR-0012). A reference to BepInEx or to the desktop host assembly would load
/// nothing on a device, and a linked desktop source would fork shared code, so both are caught
/// here, over the compiled artifact, rather than by a failed launch. The one linked source is the
/// Mobile UI tree (ADR-0025), which reaches the host only through its Mobile Platform Port.
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

    private const string MobileTree = "BazaarPlusPlus.Mobile";

    // The port, its holder, art record and panel handle, the theme, the kit and leaves #701
    // steps 2 and 3 moved, and Ten-Win Builds; a dropped <Compile Include> of the tree leaves
    // fewer, so the rules over the tree cannot pass on nothing.
    private const int MinimumMobileTreeTypes = 21;

    [Fact]
    public void Android_host_defines_only_its_own_types()
    {
        // A Compile-Include of a Core or desktop file keeps that file's namespace, so any BPP type
        // outside BazaarPlusPlus.Android is a linked source that should come from Core; the Mobile
        // UI tree is the one linked source (ADR-0025).
        using var reader = OpenHost();
        var metadata = reader.GetMetadataReader();
        var namespaces = metadata
            .TypeDefinitions.Select(handle => metadata.GetTypeDefinition(handle))
            .Where(type => type.GetDeclaringType().IsNil)
            .Select(type => metadata.GetString(type.Namespace))
            .ToArray();
        var foreign = namespaces
            .Where(ns =>
                IsIn(ns, "BazaarPlusPlus")
                && !IsIn(ns, "BazaarPlusPlus.Android")
                && !IsIn(ns, MobileTree)
            )
            .Distinct()
            .ToArray();

        Assert.True(
            foreign.Length == 0,
            "The Android host compiles BPP sources outside its namespace: "
                + string.Join(", ", foreign)
        );
        Assert.True(
            namespaces.Count(ns => IsIn(ns, MobileTree)) >= MinimumMobileTreeTypes,
            "The Android host no longer compiles the Mobile UI tree (src/BazaarPlusPlus.Mobile/)."
        );
    }

    [Fact]
    public void Mobile_tree_reaches_the_android_host_only_through_its_port()
    {
        // ADR-0025 decision 3: shared Mobile UI reaches the host through IMobilePlatformPort, whose
        // implementation the host installs; a direct reference to a host type would not compile on
        // the other host.
        var host = CompiledAssembly.Load(HostPath());
        var tree = host.Types.Where(type => IsIn(type.Namespace, MobileTree)).ToArray();
        Assert.True(
            tree.Length >= MinimumMobileTreeTypes,
            $"Expected at least {MinimumMobileTreeTypes} Mobile UI types, found {tree.Length}."
        );

        var violations = tree.SelectMany(type =>
                type.References.Where(target =>
                        target.Assembly == host.Name
                        && IsIn(target.Namespace, "BazaarPlusPlus")
                        && !IsIn(target.Namespace, MobileTree)
                    )
                    .Select(target => $"{type} references {target}")
            )
            .ToArray();
        Assert.True(
            violations.Length == 0,
            "Reach the Android host from the Mobile UI through IMobilePlatformPort:\n  "
                + string.Join("\n  ", violations)
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

    private static bool IsIn(string ns, string scope) =>
        ns == scope || ns.StartsWith(scope + ".", StringComparison.Ordinal);

    private static PEReader OpenHost() => Open("BppAndroidHostAssembly");

    private static string HostPath() =>
        typeof(AndroidHostAssemblyBoundaryTests)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "BppAndroidHostAssembly")
            .Value!;

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
