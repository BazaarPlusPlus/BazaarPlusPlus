using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace RuntimeIntegration.Tests;

public sealed class GameSuppliedLibraryAccessTests
{
    // Publicizing lets the mod compile against members the game's copy keeps internal; the
    // runtime then rejects them. Krafs records every publicized assembly as IgnoresAccessChecksTo.
    [Fact]
    public void Compiled_mod_does_not_ignore_access_checks_on_game_supplied_libraries()
    {
        var declared = typeof(GameSuppliedLibraryAccessTests)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "BppGameLibraries")
            .Value!.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var ignored = IgnoredAccessChecks(
            Path.Combine(AppContext.BaseDirectory, "BazaarPlusPlus.dll")
        );

        Assert.NotEmpty(declared);
        Assert.Contains("Assembly-CSharp", ignored);
        Assert.Empty(declared.Intersect(ignored));
    }

    private static HashSet<string> IgnoredAccessChecks(string assembly)
    {
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            if (AttributeTypeName(metadata, attribute) != "IgnoresAccessChecksToAttribute")
                continue;
            var value = metadata.GetBlobReader(attribute.Value);
            Assert.Equal(1, value.ReadUInt16());
            names.Add(value.ReadSerializedString()!);
        }
        return names;
    }

    private static string AttributeTypeName(MetadataReader metadata, CustomAttribute attribute)
    {
        var parent = attribute.Constructor.Kind switch
        {
            HandleKind.MethodDefinition => (EntityHandle)
                metadata
                    .GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor)
                    .GetDeclaringType(),
            HandleKind.MemberReference => metadata
                .GetMemberReference((MemberReferenceHandle)attribute.Constructor)
                .Parent,
            _ => default,
        };
        return parent.Kind switch
        {
            HandleKind.TypeDefinition => metadata.GetString(
                metadata.GetTypeDefinition((TypeDefinitionHandle)parent).Name
            ),
            HandleKind.TypeReference => metadata.GetString(
                metadata.GetTypeReference((TypeReferenceHandle)parent).Name
            ),
            _ => string.Empty,
        };
    }
}
