#nullable enable
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Core owns each Remote Embedded Catalog's seed resource name, and each host embeds the seed
/// under it. The name is read from Core's compiled constant and looked up in the main assembly's
/// manifest, so a renamed constant or a dropped <c>LogicalName</c> fails here instead of falling
/// back to an empty catalog at runtime.
/// </summary>
public sealed class EmbeddedCatalogSeedArchitectureTests
{
    public static TheoryData<string> SeedOwners =>
        new()
        {
            "BazaarPlusPlus.Game.LiveBuildPanel.Recommendations.TenWinBuildCatalogSettings",
            "BazaarPlusPlus.Game.Supporters.SupporterCatalogSettings",
        };

    [Theory]
    [MemberData(nameof(SeedOwners))]
    public void The_main_assembly_embeds_each_seed_under_the_name_core_owns(string owner)
    {
        foreach (var build in CompiledArtifacts.Builds)
        {
            var name = StringConstant(
                build.Assembly(CompiledArtifacts.CoreAssembly).Path,
                owner,
                "EmbeddedResourceName"
            );
            var resources = ManifestResourceNames(
                build.Assembly(CompiledArtifacts.MainAssembly).Path
            );

            Assert.True(
                resources.Contains(name),
                $"{build.Configuration}: the main assembly does not embed '{name}' ({owner}); it embeds "
                    + string.Join(", ", resources)
            );
        }
    }

    internal static string StringConstant(string assemblyPath, string typeName, string fieldName)
    {
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var metadata = pe.GetMetadataReader();
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            var fullName = metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
            if (fullName != typeName)
                continue;
            foreach (var fieldHandle in type.GetFields())
            {
                var field = metadata.GetFieldDefinition(fieldHandle);
                if (metadata.GetString(field.Name) != fieldName)
                    continue;
                var constant = metadata.GetConstant(field.GetDefaultValue());
                var blob = metadata.GetBlobReader(constant.Value);
                return blob.ReadUTF16(blob.Length);
            }
        }
        throw new InvalidOperationException(
            $"'{assemblyPath}' has no string constant {typeName}.{fieldName}."
        );
    }

    internal static IReadOnlyCollection<string> ManifestResourceNames(string assemblyPath)
    {
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var metadata = pe.GetMetadataReader();
        return metadata
            .ManifestResources.Select(handle =>
                metadata.GetString(metadata.GetManifestResource(handle).Name)
            )
            .ToHashSet(StringComparer.Ordinal);
    }
}
