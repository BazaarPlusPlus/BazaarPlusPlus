#nullable enable
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Architecture.Tests;
using Xunit;

namespace AndroidHost.Tests;

// The host's loader cannot resolve desktop dependencies on device. Inspect compiled references here.
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

    // Require the Mobile UI types so a dropped compile include cannot make the boundary rules pass.
    private const int MinimumMobileTreeTypes = 62;

    [Fact]
    public void Android_host_defines_only_its_own_types()
    {
        // Linked source retains its original namespace, so namespace checks expose stray compile includes.
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
    public void Tooltip_preview_adapters_leave_the_play_rules_to_the_mobile_ui()
    {
        var host = CompiledAssembly.Load(HostPath());
        CompiledType Type(string name) =>
            host.Types.SingleOrDefault(type => type.FullName == name)
            ?? throw new InvalidOperationException($"'{name}' is missing from the Android host.");
        IEnumerable<string> PlayReads(CompiledType type) =>
            type
                .Instructions.Where(op =>
                    op.IsMemberAccess
                    && (
                        op.Targets("Il2CppTheBazaar.Data", "get_IsInCombat")
                        || op.Targets("Il2Cpp.BoardManager", "get_IsRecapViewOpen")
                    )
                )
                .Select(op => $"{type} {op}")
                .Concat(
                    type.References.Where(target =>
                            target.FullName == "Il2CppTheBazaar.ReplayState"
                        )
                        .Select(target => $"{type} references {target}")
                );

        Assert.Equal(
            3,
            PlayReads(Type("BazaarPlusPlus.Android.Host.AndroidMobilePlatform"))
                .Select(read => read.Split(' ')[^1])
                .Distinct()
                .Count()
        );
        var adapters = new[]
        {
            "BazaarPlusPlus.Android.GameInterop.AndroidTooltipPreviewHost",
            "BazaarPlusPlus.Android.GameInterop.AndroidEnchantPreviewHost",
            "BazaarPlusPlus.Android.GameInterop.AndroidUpgradePreview",
        }
            .Select(Type)
            .ToArray();
        var violations = adapters.SelectMany(PlayReads).ToArray();
        Assert.True(
            violations.Length == 0,
            "Read the play state through the Mobile Platform Port:\n  "
                + string.Join("\n  ", violations)
        );
    }

    [Fact]
    public void Android_updater_references_only_melonloader_and_the_bcl()
    {
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
