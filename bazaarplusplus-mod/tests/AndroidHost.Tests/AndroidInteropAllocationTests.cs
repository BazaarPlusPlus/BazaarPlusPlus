using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace AndroidHost.Tests;

public sealed class AndroidInteropAllocationTests
{
    [Fact]
    public void Struct_array_allocations_do_not_use_the_native_pointer_overload()
    {
        var path = typeof(AndroidInteropAllocationTests)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "BppAndroidHostAssembly")
            .Value!;
        using var stream = File.OpenRead(path);
        using var assembly = new PEReader(stream);
        var metadata = assembly.GetMetadataReader();
        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);
            if (
                metadata.GetString(member.Name) != ".ctor"
                || member.Parent.Kind != HandleKind.TypeSpecification
            )
                continue;
            var generic = metadata.GetBlobReader(
                metadata.GetTypeSpecification((TypeSpecificationHandle)member.Parent).Signature
            );
            if (generic.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
                continue;
            generic.ReadByte(); // class/value type marker before the generic definition
            var definition = generic.ReadTypeHandle();
            if (
                definition.Kind != HandleKind.TypeReference
                || metadata.GetString(
                    metadata.GetTypeReference((TypeReferenceHandle)definition).Name
                ) != "Il2CppStructArray`1"
            )
                continue;
            var signature = metadata.GetBlobReader(member.Signature);
            signature.ReadSignatureHeader();
            if (signature.ReadCompressedInteger() != 1)
                continue;
            signature.ReadSignatureTypeCode(); // constructor return type
            // An int literal binds to IntPtr, not the long length overload; GetWorldCorners
            // then dereferences that small address in native code instead of throwing.
            Assert.NotEqual(SignatureTypeCode.IntPtr, signature.ReadSignatureTypeCode());
        }
    }
}
