#nullable enable
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The compiled host's interop-array rule (ADR-0009: compiled artifacts, never source text).
/// <c>new Il2CppStringArray(0)</c> or <c>new Il2CppStructArray&lt;Vector3&gt;(4)</c> binds an int
/// literal to the array's <c>(IntPtr pointer)</c> constructor, not its <c>(long size)</c> one,
/// because <c>int</c> converts implicitly to <c>nint</c>: the proxy wraps address 0 or 4. On the
/// device the first faulted History with a SIGSEGV in <c>RectTransform.GetWorldCorners</c> and the
/// second threw from the replay's opponent loadout, so the host never wraps a raw pointer. The
/// shared IL index drops primitive parameter types, so this rule decodes the constructor
/// references the host's metadata holds itself.
/// </summary>
public sealed class AndroidHostInteropRuleTests
{
    private const string ArrayNamespace = "Il2CppInterop.Runtime.InteropTypes.Arrays";

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

        // The rule must see the host's interop arrays, or it proves nothing.
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

    /// <summary>Full names, with primitives named by their System type.</summary>
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
