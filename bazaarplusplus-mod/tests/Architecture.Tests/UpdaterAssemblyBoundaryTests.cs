#nullable enable
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// The Updater swaps BPP's files before any of them load, so it may bind only its loader and the
/// BCL (ADR-0014). A BPP or NuGet reference would load a file it is about to replace, or change
/// the Updater with every release and force every user through the installer.
/// </summary>
public sealed class UpdaterAssemblyBoundaryTests
{
    private static readonly string[] Loader = { "BepInEx", "Mono.Cecil" };

    [Fact]
    public void Desktop_updater_references_only_bepinex_and_the_bcl()
    {
        var path = CompiledArtifacts.Metadata("BppUpdaterAssembly");
        Assert.True(File.Exists(path), $"The Updater was not built at '{path}'.");
        using var pe = new PEReader(File.OpenRead(path));
        var metadata = pe.GetMetadataReader();
        var foreign = metadata
            .AssemblyReferences.Select(handle =>
                metadata.GetString(metadata.GetAssemblyReference(handle).Name)
            )
            .Where(name =>
                name != "netstandard"
                && !name.StartsWith("System.", StringComparison.Ordinal)
                && !Loader.Contains(name)
            )
            .ToArray();

        Assert.True(
            foreign.Length == 0,
            "The desktop Updater references more than its loader and the BCL: "
                + string.Join(", ", foreign)
        );
    }

    // UpdaterRun.Version names the Updater to the loader and in its log; the csproj <Version>
    // stamps the assembly. The built file must carry one value.
    [Fact]
    public void Desktop_updater_assembly_version_is_the_version_it_reports()
    {
        var path = CompiledArtifacts.Metadata("BppUpdaterAssembly");
        Assert.True(File.Exists(path), $"The Updater was not built at '{path}'.");
        using var pe = new PEReader(File.OpenRead(path));
        var metadata = pe.GetMetadataReader();
        var updaterRun = metadata
            .TypeDefinitions.Select(metadata.GetTypeDefinition)
            .Single(type =>
                metadata.GetString(type.Namespace) == "BazaarPlusPlus.Updater"
                && metadata.GetString(type.Name) == "UpdaterRun"
            );
        var field = updaterRun
            .GetFields()
            .Select(metadata.GetFieldDefinition)
            .Single(definition => metadata.GetString(definition.Name) == "Version");
        var blob = metadata.GetBlobReader(metadata.GetConstant(field.GetDefaultValue()).Value);
        var reported = blob.ReadUTF16(blob.Length);

        Assert.Equal(reported, metadata.GetAssemblyDefinition().Version.ToString(3));
    }
}
