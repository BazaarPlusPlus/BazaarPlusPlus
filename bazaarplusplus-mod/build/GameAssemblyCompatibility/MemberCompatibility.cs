using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace BazaarPlusPlus.GameAssemblyCompatibility;

public sealed record CompatibilityResult(
    int Types,
    int Members,
    int NamedArguments,
    IReadOnlyList<string> Failures
);

public static class MemberCompatibility
{
    public static CompatibilityResult Check(string runtimeAssembly, IEnumerable<string> consumers)
    {
        using var runtimeStream = File.OpenRead(runtimeAssembly);
        using var runtimePe = new PEReader(runtimeStream);
        var runtime = runtimePe.GetMetadataReader();
        var names = new SignatureNames(runtime, runtime, runtimeAssembly, runtimeAssembly);
        var library = runtime.GetString(runtime.GetAssemblyDefinition().Name);
        var types = runtime.TypeDefinitions.ToDictionary(h => names.DefinitionName(runtime, h));
        var failures = new List<string>();
        var checkedTypes = 0;
        var checkedMembers = 0;
        var checkedArguments = 0;
        foreach (var consumer in consumers)
        {
            using var stream = File.OpenRead(consumer);
            using var pe = new PEReader(stream);
            var reader = pe.GetMetadataReader();
            names = new SignatureNames(runtime, reader, runtimeAssembly, consumer);
            void Missing(string dependency) =>
                failures.Add($"{Path.GetFileName(consumer)}: missing in {library}: {dependency}");

            foreach (var handle in reader.TypeReferences)
            {
                var type = names.GetTypeFromReference(reader, handle, 0);
                if (type.Assembly != library)
                    continue;
                checkedTypes++;
                if (!types.ContainsKey(type.Name!))
                    Missing(type.Name!);
            }

            foreach (var handle in reader.MemberReferences)
            {
                var member = reader.GetMemberReference(handle);
                var parent = names.Type(reader, member.Parent, []);
                if (parent.Assembly != library)
                    continue;
                checkedMembers++;
                var name = reader.GetString(member.Name);
                var signature =
                    member.GetKind() == MemberReferenceKind.Method
                        ? SignatureNames.Method(member.DecodeMethodSignature(names, []))
                        : member.DecodeFieldSignature(names, []).Text;
                if (!HasMember(parent, name, signature, member.GetKind()))
                    Missing($"{parent.Name}::{name} {signature}");
            }

            foreach (var handle in reader.CustomAttributes)
            {
                var attribute = reader.GetCustomAttribute(handle);
                if (attribute.Constructor.Kind != HandleKind.MemberReference)
                    continue;
                var constructor = reader.GetMemberReference(
                    (MemberReferenceHandle)attribute.Constructor
                );
                var parent = names.Type(reader, constructor.Parent, []);
                if (parent.Assembly != library)
                    continue;
                foreach (var argument in attribute.DecodeValue(names).NamedArguments)
                {
                    checkedArguments++;
                    if (!HasNamedArgument(parent, argument))
                        Missing(
                            $"{parent.Name}::{argument.Name} attribute {argument.Kind} {argument.Type.Text}"
                        );
                }
            }
        }
        return new(checkedTypes, checkedMembers, checkedArguments, failures);

        IEnumerable<(TypeDefinition Definition, ImmutableArray<SignatureType> Arguments)> Hierarchy(
            SignatureType type
        )
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (
                type.Assembly == library
                && type.Name != null
                && types.TryGetValue(type.Name, out var handle)
            )
            {
                if (!visited.Add(type.Name))
                    throw new BadImageFormatException($"Cyclic base type: {type.Name}");
                var definition = runtime.GetTypeDefinition(handle);
                yield return (definition, type.Arguments);
                type = names.Type(runtime, definition.BaseType, type.Arguments);
            }
        }

        bool HasMember(
            SignatureType parent,
            string name,
            string signature,
            MemberReferenceKind kind
        )
        {
            // MemberRef binds the open declaration before instantiation. Call(!0) is not
            // Call(int), even on Token<int>. Base types still substitute relative to that
            // open declaration (for example Derived : Base<int>).
            foreach (var (definition, arguments) in Hierarchy(parent with { Arguments = [] }))
            {
                if (kind == MemberReferenceKind.Method)
                {
                    foreach (var handle in definition.GetMethods())
                    {
                        var method = runtime.GetMethodDefinition(handle);
                        if (
                            runtime.StringComparer.Equals(method.Name, name)
                            && SignatureNames.Method(method.DecodeSignature(names, arguments))
                                == signature
                        )
                            return true;
                    }
                    // Constructors are never inherited.
                    if (name is ".ctor" or ".cctor")
                        return false;
                }
                else
                {
                    foreach (var handle in definition.GetFields())
                    {
                        var field = runtime.GetFieldDefinition(handle);
                        if (
                            runtime.StringComparer.Equals(field.Name, name)
                            && field.DecodeSignature(names, arguments).Text == signature
                        )
                            return true;
                    }
                }
            }
            return false;
        }

        bool HasNamedArgument(
            SignatureType parent,
            CustomAttributeNamedArgument<SignatureType> argument
        )
        {
            var name =
                argument.Name ?? throw new BadImageFormatException("Unnamed attribute argument.");
            foreach (var (definition, arguments) in Hierarchy(parent))
            {
                if (argument.Kind == CustomAttributeNamedArgumentKind.Field)
                {
                    foreach (var handle in definition.GetFields())
                    {
                        var field = runtime.GetFieldDefinition(handle);
                        if (
                            runtime.StringComparer.Equals(field.Name, name)
                            && field.DecodeSignature(names, arguments).Text == argument.Type.Text
                            && (field.Attributes & FieldAttributes.FieldAccessMask)
                                == FieldAttributes.Public
                            && (
                                field.Attributes
                                & (
                                    FieldAttributes.Static
                                    | FieldAttributes.InitOnly
                                    | FieldAttributes.Literal
                                )
                            ) == 0
                        )
                            return true;
                    }
                }
                else
                {
                    foreach (var handle in definition.GetProperties())
                    {
                        var property = runtime.GetPropertyDefinition(handle);
                        var setter = property.GetAccessors().Setter;
                        var signature = property.DecodeSignature(names, arguments);
                        if (
                            runtime.StringComparer.Equals(property.Name, name)
                            && signature.ReturnType.Text == argument.Type.Text
                            && signature.ParameterTypes.Length == 0
                            && !setter.IsNil
                        )
                        {
                            var attributes = runtime.GetMethodDefinition(setter).Attributes;
                            if (
                                (attributes & MethodAttributes.MemberAccessMask)
                                    == MethodAttributes.Public
                                && (attributes & MethodAttributes.Static) == 0
                            )
                                return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}

internal sealed record SignatureType(
    string Text,
    string? Assembly = null,
    string? Name = null,
    ImmutableArray<SignatureType> Arguments = default
);

internal sealed class SignatureNames(
    MetadataReader runtime,
    MetadataReader consumer,
    string runtimePath,
    string consumerPath
)
    : ISignatureTypeProvider<SignatureType, ImmutableArray<SignatureType>>,
        ICustomAttributeTypeProvider<SignatureType>
{
    public SignatureType Type(
        MetadataReader reader,
        EntityHandle handle,
        ImmutableArray<SignatureType> context
    ) =>
        handle.Kind switch
        {
            HandleKind.TypeReference => GetTypeFromReference(
                reader,
                (TypeReferenceHandle)handle,
                0
            ),
            HandleKind.TypeDefinition => GetTypeFromDefinition(
                reader,
                (TypeDefinitionHandle)handle,
                0
            ),
            HandleKind.TypeSpecification => GetTypeFromSpecification(
                reader,
                context,
                (TypeSpecificationHandle)handle,
                0
            ),
            _ => new("<no type>"),
        };

    public static string Method(MethodSignature<SignatureType> signature) =>
        $"{signature.Header.RawValue:x2} generic={signature.GenericParameterCount} required={signature.RequiredParameterCount} "
        + $"{signature.ReturnType.Text} ({string.Join(", ", signature.ParameterTypes.Select(p => p.Text))})";

    public string DefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        return type.IsNested
            ? DefinitionName(reader, type.GetDeclaringType()) + "+" + reader.GetString(type.Name)
            : Qualified(reader.GetString(type.Namespace), reader.GetString(type.Name));
    }

    private static string Qualified(string ns, string name) =>
        ns.Length == 0 ? name : ns + "." + name;

    private static SignatureType Named(string assembly, string name, byte kind)
    {
        // netstandard is a facade over Unity's mscorlib; versions differ without changing type identity.
        assembly = AssemblyKey(assembly);
        return new($"{kind:x2}[{assembly}]{name}", assembly, name, []);
    }

    private static string AssemblyKey(string name) =>
        name is "mscorlib" or "netstandard" or "System.Runtime" or "System.Private.CoreLib"
            ? "core"
            : name;

    public SignatureType GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind
    ) =>
        Named(
            reader.GetString(reader.GetAssemblyDefinition().Name),
            DefinitionName(reader, handle),
            rawTypeKind
        );

    public SignatureType GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind
    )
    {
        var type = reader.GetTypeReference(handle);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var parent = GetTypeFromReference(
                reader,
                (TypeReferenceHandle)type.ResolutionScope,
                rawTypeKind
            );
            return Named(
                parent.Assembly!,
                parent.Name + "+" + reader.GetString(type.Name),
                rawTypeKind
            );
        }
        var assembly =
            type.ResolutionScope.Kind == HandleKind.AssemblyReference
                ? reader.GetString(
                    reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name
                )
                : reader.GetString(reader.GetAssemblyDefinition().Name);
        return Named(
            assembly,
            Qualified(reader.GetString(type.Namespace), reader.GetString(type.Name)),
            rawTypeKind
        );
    }

    public SignatureType GetTypeFromSpecification(
        MetadataReader reader,
        ImmutableArray<SignatureType> genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind
    ) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public SignatureType GetGenericInstantiation(
        SignatureType genericType,
        ImmutableArray<SignatureType> typeArguments
    ) =>
        genericType with
        {
            Text =
                genericType.Text + "<" + string.Join(",", typeArguments.Select(t => t.Text)) + ">",
            Arguments = typeArguments,
        };

    public SignatureType GetGenericTypeParameter(
        ImmutableArray<SignatureType> genericContext,
        int index
    ) =>
        !genericContext.IsDefaultOrEmpty && index < genericContext.Length
            ? genericContext[index]
            : new("!" + index);

    public SignatureType GetGenericMethodParameter(
        ImmutableArray<SignatureType> genericContext,
        int index
    ) => new("!!" + index);

    public SignatureType GetPrimitiveType(PrimitiveTypeCode typeCode) => new(typeCode.ToString());

    public SignatureType GetSZArrayType(SignatureType elementType) => new(elementType.Text + "[]");

    public SignatureType GetArrayType(SignatureType elementType, ArrayShape shape) =>
        new(
            $"{elementType.Text}[rank={shape.Rank};sizes={string.Join(",", shape.Sizes)};lower={string.Join(",", shape.LowerBounds)}]"
        );

    public SignatureType GetByReferenceType(SignatureType elementType) =>
        new(elementType.Text + "&");

    public SignatureType GetPointerType(SignatureType elementType) => new(elementType.Text + "*");

    public SignatureType GetPinnedType(SignatureType elementType) =>
        new(elementType.Text + " pinned");

    public SignatureType GetModifiedType(
        SignatureType modifier,
        SignatureType unmodifiedType,
        bool isRequired
    ) => new($"{unmodifiedType.Text} mod{(isRequired ? "req" : "opt")}({modifier.Text})");

    public SignatureType GetFunctionPointerType(MethodSignature<SignatureType> signature) =>
        new("fn " + Method(signature));

    public SignatureType GetSystemType() =>
        Named("core", "System.Type", (byte)SignatureTypeKind.Class);

    public bool IsSystemType(SignatureType type) =>
        type.Assembly == "core" && type.Name == "System.Type";

    public SignatureType GetTypeFromSerializedName(string name)
    {
        var parsed = TypeName.Parse(name);
        return Named(
            parsed.AssemblyName?.Name ?? consumer.GetString(consumer.GetAssemblyDefinition().Name),
            parsed.FullName,
            (byte)SignatureTypeKind.ValueType
        );
    }

    public PrimitiveTypeCode GetUnderlyingEnumType(SignatureType type)
    {
        var error = new BadImageFormatException($"Cannot resolve attribute enum: {type.Text}");
        if (type.Assembly == AssemblyKey(runtime.GetString(runtime.GetAssemblyDefinition().Name)))
            return EnumUnderlyingType(runtime, type) ?? throw error;
        if (type.Assembly == AssemblyKey(consumer.GetString(consumer.GetAssemblyDefinition().Name)))
            return EnumUnderlyingType(consumer, type) ?? throw error;

        string[] files =
            type.Assembly == "core"
                ?
                [
                    "mscorlib.dll",
                    "System.Private.CoreLib.dll",
                    "System.Runtime.dll",
                    "netstandard.dll",
                ]
                : [type.Assembly + ".dll"];
        var directories = new[]
        {
            Path.GetDirectoryName(consumerPath)!,
            Path.GetDirectoryName(runtimePath)!,
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
        };
        foreach (var directory in directories.Distinct(StringComparer.Ordinal))
        foreach (var file in files)
        {
            var path = Path.Combine(directory, file);
            if (!File.Exists(path))
                continue;
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var reader = pe.GetMetadataReader();
            if (
                AssemblyKey(reader.GetString(reader.GetAssemblyDefinition().Name)) == type.Assembly
                && EnumUnderlyingType(reader, type) is { } code
            )
                return code;
        }
        throw error;
    }

    private PrimitiveTypeCode? EnumUnderlyingType(MetadataReader reader, SignatureType type)
    {
        foreach (var handle in reader.TypeDefinitions)
            if (DefinitionName(reader, handle) == type.Name)
                foreach (var fieldHandle in reader.GetTypeDefinition(handle).GetFields())
                {
                    var field = reader.GetFieldDefinition(fieldHandle);
                    if (
                        reader.StringComparer.Equals(field.Name, "value__")
                        && Enum.TryParse<PrimitiveTypeCode>(
                            field.DecodeSignature(this, []).Text,
                            out var code
                        )
                    )
                        return code;
                }
        return null;
    }
}
