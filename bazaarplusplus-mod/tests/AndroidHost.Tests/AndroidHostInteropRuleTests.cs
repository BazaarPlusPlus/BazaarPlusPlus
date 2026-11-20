#nullable enable
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace AndroidHost.Tests;

// An int literal binds to the interop array's IntPtr overload, wrapping the literal as an address.
// Decode the signature here because the shared IL index drops primitive parameter types.
public sealed class AndroidHostInteropRuleTests
{
    private const string ArrayNamespace = "Il2CppInterop.Runtime.InteropTypes.Arrays";

    // TMP_FontAsset.OnDestroy destroys its material and atlas textures, and the host's font copy
    // shares both with the game's font.
    [Fact]
    public void The_font_copy_lets_go_of_the_shared_material_and_atlas_before_it_is_destroyed()
    {
        var path = typeof(AndroidHostInteropRuleTests)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "BppAndroidHostAssembly")
            .Value!;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var names = new SignatureNames();

        var setters = reader
            .MemberReferences.Select(reader.GetMemberReference)
            .Select(member =>
                (
                    Owner: OwnerName(reader, member.Parent, names),
                    Name: reader.GetString(member.Name)
                )
            )
            .ToHashSet();

        Assert.Contains(("Il2CppTMPro.TMP_Asset", "set_material"), setters);
        Assert.Contains(("Il2CppTMPro.TMP_FontAsset", "set_atlasTextures"), setters);
    }

    [Fact]
    public void No_interop_array_is_constructed_over_a_raw_pointer()
    {
        var path = typeof(AndroidHostInteropRuleTests)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "BppAndroidHostAssembly")
            .Value!;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var names = new SignatureNames();

        var arrayConstructors = 0;
        var violations = new List<string>();
        foreach (var handle in reader.MemberReferences)
        {
            var member = reader.GetMemberReference(handle);
            if (reader.GetString(member.Name) != ".ctor")
                continue;
            var owner = OwnerName(reader, member.Parent, names);
            if (owner == null || !owner.StartsWith(ArrayNamespace + ".", StringComparison.Ordinal))
                continue;
            arrayConstructors++;
            var signature = member.DecodeMethodSignature(names, null);
            if (signature.ParameterTypes is ["System.IntPtr"])
                violations.Add($"{owner}::.ctor(System.IntPtr)");
        }

        Assert.True(arrayConstructors > 0, "the host constructs no interop array");
        Assert.True(
            violations.Count == 0,
            "Size an interop array with a long (new Il2CppStringArray(0L)), never an int literal:\n  "
                + string.Join("\n  ", violations)
        );
    }

    private static string? OwnerName(
        MetadataReader reader,
        EntityHandle parent,
        SignatureNames names
    ) =>
        parent.Kind switch
        {
            HandleKind.TypeReference => names.GetTypeFromReference(
                reader,
                (TypeReferenceHandle)parent,
                0
            ),
            HandleKind.TypeSpecification => reader
                .GetTypeSpecification((TypeSpecificationHandle)parent)
                .DecodeSignature(names, null),
            _ => null,
        };

    private sealed class SignatureNames : ISignatureTypeProvider<string, object?>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) =>
            typeCode switch
            {
                PrimitiveTypeCode.IntPtr => "System.IntPtr",
                PrimitiveTypeCode.Int64 => "System.Int64",
                PrimitiveTypeCode.Int32 => "System.Int32",
                _ => "System." + typeCode,
            };

        public string GetTypeFromDefinition(
            MetadataReader metadata,
            TypeDefinitionHandle handle,
            byte rawTypeKind
        )
        {
            var definition = metadata.GetTypeDefinition(handle);
            return $"{metadata.GetString(definition.Namespace)}.{metadata.GetString(definition.Name)}";
        }

        public string GetTypeFromReference(
            MetadataReader metadata,
            TypeReferenceHandle handle,
            byte rawTypeKind
        )
        {
            var reference = metadata.GetTypeReference(handle);
            return $"{metadata.GetString(reference.Namespace)}.{metadata.GetString(reference.Name)}";
        }

        public string GetTypeFromSpecification(
            MetadataReader metadata,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind
        ) => metadata.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetGenericInstantiation(
            string genericType,
            ImmutableArray<string> typeArguments
        ) => genericType;

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[*]";

        public string GetByReferenceType(string elementType) => elementType + "&";

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetPinnedType(string elementType) => elementType;

        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

        public string GetFunctionPointerType(MethodSignature<string> signature) => "method*";

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) =>
            unmodifiedType;
    }
}
