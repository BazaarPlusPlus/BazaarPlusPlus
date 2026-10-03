#nullable enable
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Architecture.Tests;

/// <summary>
/// Metadata and IL index over the assemblies the same build produced (ADR-0009). The csproj
/// builds <c>src/BazaarPlusPlus</c> in Debug and Release, so <c>#if DEBUG</c> and
/// <c>#else</c> bodies are both scanned; every rule must hold in each configuration.
/// Compiler-generated and nested types fold into their outermost declaring type.
/// </summary>
internal static class CompiledArtifacts
{
    internal const string MainAssembly = "BazaarPlusPlus";
    internal const string ModApiAssembly = "BazaarPlusPlus.ModApi";
    internal const string StorageAssembly = "BazaarPlusPlus.Storage";

    private static readonly string[] ScannedAssemblies =
    {
        MainAssembly,
        ModApiAssembly,
        StorageAssembly,
    };

    private static readonly Lazy<IReadOnlyList<CompiledBuild>> LazyBuilds = new(LoadBuilds);
    private static readonly Lazy<ReferenceUniverse> LazyUniverse = new(LoadUniverse);

    /// <summary>One entry per configuration (Debug, Release).</summary>
    internal static IReadOnlyList<CompiledBuild> Builds => LazyBuilds.Value;

    /// <summary>Type definitions every rule target must resolve against.</summary>
    internal static ReferenceUniverse Universe => LazyUniverse.Value;

    private static string Metadata(string key)
    {
        var value = typeof(CompiledArtifacts)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == key)
            ?.Value;
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Architecture.Tests is missing metadata '{key}'.");
        return value;
    }

    private static IReadOnlyList<CompiledBuild> LoadBuilds() =>
        new[] { "Debug", "Release" }
            .Select(configuration =>
            {
                var directory = Path.GetFullPath(Metadata("BppMainOutput." + configuration));
                var assemblies = ScannedAssemblies
                    .Select(name => CompiledAssembly.Load(Path.Combine(directory, name + ".dll")))
                    .ToArray();
                return new CompiledBuild(configuration, directory, assemblies);
            })
            .ToArray();

    private static ReferenceUniverse LoadUniverse()
    {
        var directories = new List<string> { Builds[0].Directory };
        var managed = Metadata("BppManagedPath");
        if (Directory.Exists(managed))
            directories.Add(managed);
        return ReferenceUniverse.Load(directories);
    }
}

internal sealed class CompiledBuild
{
    internal CompiledBuild(
        string configuration,
        string directory,
        IReadOnlyList<CompiledAssembly> assemblies
    )
    {
        Configuration = configuration;
        Directory = directory;
        Assemblies = assemblies;
    }

    internal string Configuration { get; }
    internal string Directory { get; }
    internal IReadOnlyList<CompiledAssembly> Assemblies { get; }

    internal CompiledAssembly Assembly(string name) =>
        Assemblies.SingleOrDefault(assembly => assembly.Name == name)
        ?? throw new InvalidOperationException($"{Configuration}: assembly '{name}' not scanned.");

    internal IEnumerable<CompiledType> Types => Assemblies.SelectMany(assembly => assembly.Types);

    /// <summary>The named type; a rule whose scope does not resolve fails.</summary>
    internal CompiledType Type(string fullName) =>
        Types.SingleOrDefault(type => type.FullName == fullName)
        ?? throw new InvalidOperationException(
            $"{Configuration}: rule scope type '{fullName}' does not exist in the build."
        );

    /// <summary>Types in <paramref name="ns"/> or a child namespace; empty scopes fail.</summary>
    internal IReadOnlyList<CompiledType> TypesIn(string ns)
    {
        var types = Types.Where(type => IsInNamespace(type.Namespace, ns)).ToArray();
        if (types.Length == 0)
            throw new InvalidOperationException(
                $"{Configuration}: rule scope namespace '{ns}' has no types."
            );
        return types;
    }

    internal static bool IsInNamespace(string candidate, string ns) =>
        candidate == ns || candidate.StartsWith(ns + ".", StringComparison.Ordinal);
}

/// <summary>A referenced type: defining assembly name plus namespace-qualified name.</summary>
internal readonly record struct TypeName(string Assembly, string Namespace, string Name)
{
    /// <summary>Namespace-qualified name; nested types use <c>+</c>.</summary>
    internal string FullName => Namespace.Length == 0 ? Name : Namespace + "." + Name;

    public override string ToString() => $"[{Assembly}]{FullName}";
}

/// <summary>One IL instruction with its resolved operand.</summary>
internal sealed record IlInstruction(
    string OpCode,
    TypeName? TargetType,
    string? TargetMember,
    IReadOnlyList<TypeName> MentionedTypes,
    string? Literal
)
{
    internal bool Targets(string declaringType, string member) =>
        TargetType?.FullName == declaringType && TargetMember == member;

    internal bool IsMemberAccess => TargetMember != null && OpCode is not ("ldtoken" or "ldstr");

    public override string ToString() =>
        Literal != null ? $"{OpCode} \"{Literal}\"" : $"{OpCode} {TargetType}::{TargetMember}";
}

internal sealed class CompiledMethod
{
    internal CompiledMethod(
        CompiledType owner,
        string declaringType,
        string name,
        IReadOnlyList<IlInstruction> instructions,
        IReadOnlyCollection<TypeName> references
    )
    {
        Owner = owner;
        DeclaringType = declaringType;
        Name = name;
        Instructions = instructions;
        References = references;
    }

    /// <summary>The outermost declaring type.</summary>
    internal CompiledType Owner { get; }

    /// <summary>The (possibly nested or compiler-generated) metadata declaring type.</summary>
    internal string DeclaringType { get; }
    internal string Name { get; }
    internal IReadOnlyList<IlInstruction> Instructions { get; }

    /// <summary>Types named by this method's signature, locals, and IL operands.</summary>
    internal IReadOnlyCollection<TypeName> References { get; }

    internal IEnumerable<string> Literals =>
        Instructions.Where(op => op.Literal != null).Select(op => op.Literal!);

    public override string ToString() => $"{DeclaringType}::{Name}";
}

internal sealed class CompiledType
{
    private readonly HashSet<TypeName> _references = new();
    private readonly List<CompiledMethod> _methods = new();
    private readonly List<(string Member, TypeName? Type)> _publicSurface = new();
    private readonly List<(string DeclaringType, string Name, TypeName? Type)> _fields = new();

    internal CompiledType(string assembly, string ns, string name)
    {
        Assembly = assembly;
        Namespace = ns;
        Name = name;
    }

    internal string Assembly { get; }
    internal string Namespace { get; }
    internal string Name { get; }
    internal string FullName => Namespace.Length == 0 ? Name : Namespace + "." + Name;

    /// <summary>Every type named by a signature, base type, interface, local, or IL operand.</summary>
    internal IReadOnlyCollection<TypeName> References => _references;
    internal IReadOnlyList<CompiledMethod> Methods => _methods;

    /// <summary>Fields of this type and its nested types, including closure captures.</summary>
    internal IReadOnlyList<(string DeclaringType, string Name, TypeName? Type)> Fields => _fields;

    /// <summary>Public methods (by return type) and public fields declared on this type itself.</summary>
    internal IReadOnlyList<(string Member, TypeName? Type)> PublicSurface => _publicSurface;

    internal IEnumerable<IlInstruction> Instructions =>
        _methods.SelectMany(method => method.Instructions);

    internal IEnumerable<string> Literals => _methods.SelectMany(method => method.Literals);

    /// <summary>
    /// The named method plus the compiler-generated bodies it owns (lambdas, local functions,
    /// iterator and async state machines). Fails when no method has that name.
    /// </summary>
    internal IReadOnlyList<CompiledMethod> MethodClosure(string name)
    {
        var generatedPrefix = "<" + name + ">";
        var methods = _methods
            .Where(method =>
                (method.Name == name && method.DeclaringType == FullName)
                || method.Name.StartsWith(generatedPrefix, StringComparison.Ordinal)
                || method.DeclaringType.Contains("+" + generatedPrefix, StringComparison.Ordinal)
            )
            .ToArray();
        if (!methods.Any(method => method.Name == name))
            throw new InvalidOperationException(
                $"Rule scope method '{FullName}::{name}' is missing."
            );
        return methods;
    }

    internal void AddReference(TypeName type) => _references.Add(type);

    internal void AddMethod(CompiledMethod method) => _methods.Add(method);

    internal void AddField(string declaringType, string name, TypeName? type) =>
        _fields.Add((declaringType, name, type));

    internal void AddPublicMember(string member, TypeName? type) =>
        _publicSurface.Add((member, type));

    public override string ToString() => FullName;
}

internal sealed class CompiledAssembly
{
    private static readonly IReadOnlyDictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => unchecked((ushort)code.Value));

    private CompiledAssembly(string name, string path, IReadOnlyList<CompiledType> types)
    {
        Name = name;
        Path = path;
        Types = types;
    }

    internal string Name { get; }
    internal string Path { get; }
    internal IReadOnlyList<CompiledType> Types { get; }

    internal static CompiledAssembly Load(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"Compiled artifact '{path}' is missing; build Architecture.Tests to produce it."
            );

        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var name = reader.GetString(reader.GetAssemblyDefinition().Name);
        var names = new TypeNames(reader, name);
        var signatures = new SignatureCollector(reader, names);
        var types = new Dictionary<TypeDefinitionHandle, CompiledType>();

        foreach (var handle in reader.TypeDefinitions)
        {
            var outer = Outermost(reader, handle);
            var outerName = names.Definition(outer);
            if (outerName.Name == "<Module>" || outerName.Name.StartsWith('<'))
                continue;
            if (!types.TryGetValue(outer, out var owner))
            {
                owner = new CompiledType(name, outerName.Namespace, outerName.Name);
                types.Add(outer, owner);
            }

            Index(pe, reader, handle, owner, names, signatures);
        }

        return new CompiledAssembly(name, path, types.Values.ToArray());
    }

    private static TypeDefinitionHandle Outermost(MetadataReader reader, TypeDefinitionHandle type)
    {
        while (true)
        {
            var declaring = reader.GetTypeDefinition(type).GetDeclaringType();
            if (declaring.IsNil)
                return type;
            type = declaring;
        }
    }

    private static void Index(
        PEReader pe,
        MetadataReader reader,
        TypeDefinitionHandle handle,
        CompiledType owner,
        TypeNames names,
        SignatureCollector signatures
    )
    {
        var definition = reader.GetTypeDefinition(handle);
        var declaringName = names.Definition(handle).FullName;
        var isOwner = declaringName == owner.FullName;

        if (!definition.BaseType.IsNil)
            foreach (var type in signatures.Types(definition.BaseType))
                owner.AddReference(type);
        foreach (var interfaceHandle in definition.GetInterfaceImplementations())
        {
            var implemented = reader.GetInterfaceImplementation(interfaceHandle).Interface;
            foreach (var type in signatures.Types(implemented))
                owner.AddReference(type);
        }

        foreach (var fieldHandle in definition.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            foreach (var type in signatures.Field(field))
                owner.AddReference(type);
            owner.AddField(
                declaringName,
                reader.GetString(field.Name),
                signatures.FieldType(field)
            );
            if (
                isOwner
                && (field.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public
            )
                owner.AddPublicMember(reader.GetString(field.Name), signatures.FieldType(field));
        }

        foreach (var methodHandle in definition.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            var methodReferences = new HashSet<TypeName>(signatures.Method(method));
            if (
                isOwner
                && (method.Attributes & MethodAttributes.MemberAccessMask)
                    == MethodAttributes.Public
            )
                owner.AddPublicMember(reader.GetString(method.Name), signatures.ReturnType(method));

            var instructions = Array.Empty<IlInstruction>();
            if (method.RelativeVirtualAddress != 0)
            {
                var body = pe.GetMethodBody(method.RelativeVirtualAddress);
                if (!body.LocalSignature.IsNil)
                {
                    var locals = reader.GetStandaloneSignature(body.LocalSignature);
                    methodReferences.UnionWith(signatures.Locals(locals));
                }

                instructions = ReadIl(reader, body, names, signatures).ToArray();
                foreach (var instruction in instructions)
                    methodReferences.UnionWith(instruction.MentionedTypes);
            }

            foreach (var type in methodReferences)
                owner.AddReference(type);

            owner.AddMethod(
                new CompiledMethod(
                    owner,
                    declaringName,
                    reader.GetString(method.Name),
                    instructions,
                    methodReferences
                )
            );
        }
    }

    private static IEnumerable<IlInstruction> ReadIl(
        MetadataReader reader,
        MethodBodyBlock body,
        TypeNames names,
        SignatureCollector signatures
    )
    {
        var il = body.GetILReader();
        var result = new List<IlInstruction>();
        while (il.RemainingBytes > 0)
        {
            ushort value = il.ReadByte();
            if (value == 0xFE)
                value = (ushort)(0xFE00 | il.ReadByte());
            if (!OpCodesByValue.TryGetValue(value, out var opCode))
                throw new InvalidOperationException($"Unknown IL opcode 0x{value:X4}.");

            switch (opCode.OperandType)
            {
                case OperandType.InlineNone:
                    continue;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    il.Offset += 1;
                    continue;
                case OperandType.InlineVar:
                    il.Offset += 2;
                    continue;
                case OperandType.InlineBrTarget:
                case OperandType.InlineI:
                case OperandType.ShortInlineR:
                case OperandType.InlineSig:
                    il.Offset += 4;
                    continue;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    il.Offset += 8;
                    continue;
                case OperandType.InlineSwitch:
                    var targets = il.ReadInt32();
                    il.Offset += 4 * targets;
                    continue;
                case OperandType.InlineString:
                    var literal = reader.GetUserString(
                        MetadataTokens.UserStringHandle(il.ReadInt32() & 0x00FFFFFF)
                    );
                    result.Add(
                        new IlInstruction(
                            opCode.Name!,
                            null,
                            null,
                            Array.Empty<TypeName>(),
                            literal
                        )
                    );
                    continue;
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    result.Add(Resolve(opCode.Name!, il.ReadInt32(), reader, names, signatures));
                    continue;
                default:
                    throw new InvalidOperationException($"Unhandled operand {opCode.OperandType}.");
            }
        }

        return result;
    }

    private static IlInstruction Resolve(
        string opCode,
        int token,
        MetadataReader reader,
        TypeNames names,
        SignatureCollector signatures
    )
    {
        var handle = MetadataTokens.EntityHandle(token);
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
            case HandleKind.TypeReference:
            case HandleKind.TypeSpecification:
                return new IlInstruction(
                    opCode,
                    signatures.Primary(handle),
                    null,
                    signatures.Types(handle).ToArray(),
                    null
                );
            case HandleKind.MethodDefinition:
            {
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                var declaring = names.Definition(method.GetDeclaringType());
                return new IlInstruction(
                    opCode,
                    declaring,
                    reader.GetString(method.Name),
                    new[] { declaring }.Concat(signatures.Method(method)).ToArray(),
                    null
                );
            }
            case HandleKind.FieldDefinition:
            {
                var field = reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                var declaring = names.Definition(field.GetDeclaringType());
                return new IlInstruction(
                    opCode,
                    declaring,
                    reader.GetString(field.Name),
                    new[] { declaring }.Concat(signatures.Field(field)).ToArray(),
                    null
                );
            }
            case HandleKind.MemberReference:
            {
                var member = reader.GetMemberReference((MemberReferenceHandle)handle);
                var parent = member.Parent;
                var mentioned = new List<TypeName>();
                TypeName? declaring = null;
                if (parent.Kind == HandleKind.MethodDefinition)
                {
                    var owner = reader.GetMethodDefinition((MethodDefinitionHandle)parent);
                    declaring = names.Definition(owner.GetDeclaringType());
                    mentioned.Add(declaring.Value);
                }
                else if (parent.Kind != HandleKind.ModuleReference)
                {
                    declaring = signatures.Primary(parent);
                    mentioned.AddRange(signatures.Types(parent));
                }

                mentioned.AddRange(signatures.Member(member));
                return new IlInstruction(
                    opCode,
                    declaring,
                    reader.GetString(member.Name),
                    mentioned,
                    null
                );
            }
            case HandleKind.MethodSpecification:
            {
                var spec = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                var inner = Resolve(
                    opCode,
                    MetadataTokens.GetToken(spec.Method),
                    reader,
                    names,
                    signatures
                );
                return inner with
                {
                    MentionedTypes = inner
                        .MentionedTypes.Concat(signatures.Instantiation(spec))
                        .ToArray(),
                };
            }
            default:
                return new IlInstruction(opCode, null, null, Array.Empty<TypeName>(), null);
        }
    }
}

/// <summary>Resolves definition and reference handles to assembly-qualified names.</summary>
internal sealed class TypeNames
{
    private readonly MetadataReader _reader;
    private readonly string _assembly;

    internal TypeNames(MetadataReader reader, string assembly)
    {
        _reader = reader;
        _assembly = assembly;
    }

    internal TypeName Definition(TypeDefinitionHandle handle)
    {
        var definition = _reader.GetTypeDefinition(handle);
        var name = _reader.GetString(definition.Name);
        var declaring = definition.GetDeclaringType();
        if (declaring.IsNil)
            return new TypeName(_assembly, _reader.GetString(definition.Namespace), name);
        var outer = Definition(declaring);
        return outer with { Name = outer.Name + "+" + name };
    }

    internal TypeName Reference(TypeReferenceHandle handle)
    {
        var reference = _reader.GetTypeReference(handle);
        var name = _reader.GetString(reference.Name);
        var scope = reference.ResolutionScope;
        switch (scope.Kind)
        {
            case HandleKind.TypeReference:
                var outer = Reference((TypeReferenceHandle)scope);
                return outer with { Name = outer.Name + "+" + name };
            case HandleKind.AssemblyReference:
                var assembly = _reader.GetAssemblyReference((AssemblyReferenceHandle)scope);
                return new TypeName(
                    _reader.GetString(assembly.Name),
                    _reader.GetString(reference.Namespace),
                    name
                );
            default:
                return new TypeName(_assembly, _reader.GetString(reference.Namespace), name);
        }
    }
}

/// <summary>Collects every named type a signature or type handle mentions.</summary>
internal sealed class SignatureCollector : ISignatureTypeProvider<TypeName?, object?>
{
    private readonly MetadataReader _reader;
    private readonly TypeNames _names;
    private List<TypeName> _sink = new();

    internal SignatureCollector(MetadataReader reader, TypeNames names)
    {
        _reader = reader;
        _names = names;
    }

    internal IReadOnlyList<TypeName> Types(EntityHandle handle) =>
        Collect(() =>
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                    _sink.Add(_names.Definition((TypeDefinitionHandle)handle));
                    break;
                case HandleKind.TypeReference:
                    _sink.Add(_names.Reference((TypeReferenceHandle)handle));
                    break;
                case HandleKind.TypeSpecification:
                    _ = _reader
                        .GetTypeSpecification((TypeSpecificationHandle)handle)
                        .DecodeSignature(this, null);
                    break;
            }
        });

    /// <summary>The handle's own type; for a generic instance, its generic definition.</summary>
    internal TypeName? Primary(EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                return _names.Definition((TypeDefinitionHandle)handle);
            case HandleKind.TypeReference:
                return _names.Reference((TypeReferenceHandle)handle);
            case HandleKind.TypeSpecification:
                TypeName? primary = null;
                Collect(() =>
                    primary = _reader
                        .GetTypeSpecification((TypeSpecificationHandle)handle)
                        .DecodeSignature(this, null)
                );
                return primary;
            default:
                return null;
        }
    }

    internal IReadOnlyList<TypeName> Field(FieldDefinition field) =>
        Collect(() => field.DecodeSignature(this, null));

    internal IReadOnlyList<TypeName> Method(MethodDefinition method) =>
        Collect(() => method.DecodeSignature(this, null));

    internal TypeName? ReturnType(MethodDefinition method)
    {
        TypeName? returnType = null;
        Collect(() => returnType = method.DecodeSignature(this, null).ReturnType);
        return returnType;
    }

    internal TypeName? FieldType(FieldDefinition field)
    {
        TypeName? fieldType = null;
        Collect(() => fieldType = field.DecodeSignature(this, null));
        return fieldType;
    }

    internal IReadOnlyList<TypeName> Locals(StandaloneSignature signature) =>
        Collect(() => signature.DecodeLocalSignature(this, null));

    internal IReadOnlyList<TypeName> Member(MemberReference member) =>
        Collect(() =>
        {
            if (member.GetKind() == MemberReferenceKind.Field)
                member.DecodeFieldSignature(this, null);
            else
                member.DecodeMethodSignature(this, null);
        });

    internal IReadOnlyList<TypeName> Instantiation(MethodSpecification spec) =>
        Collect(() => spec.DecodeSignature(this, null));

    private IReadOnlyList<TypeName> Collect(Action decode)
    {
        var previous = _sink;
        _sink = new List<TypeName>();
        try
        {
            decode();
            return _sink;
        }
        finally
        {
            _sink = previous;
        }
    }

    public TypeName? GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind
    )
    {
        var name = _names.Definition(handle);
        _sink.Add(name);
        return name;
    }

    public TypeName? GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind
    )
    {
        var name = _names.Reference(handle);
        _sink.Add(name);
        return name;
    }

    public TypeName? GetTypeFromSpecification(
        MetadataReader reader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind
    ) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public TypeName? GetPrimitiveType(PrimitiveTypeCode typeCode) => null;

    public TypeName? GetSZArrayType(TypeName? elementType) => elementType;

    public TypeName? GetArrayType(TypeName? elementType, ArrayShape shape) => elementType;

    public TypeName? GetByReferenceType(TypeName? elementType) => elementType;

    public TypeName? GetPointerType(TypeName? elementType) => elementType;

    public TypeName? GetPinnedType(TypeName? elementType) => elementType;

    public TypeName? GetGenericInstantiation(
        TypeName? genericType,
        ImmutableArray<TypeName?> typeArguments
    ) => genericType;

    public TypeName? GetGenericMethodParameter(object? genericContext, int index) => null;

    public TypeName? GetGenericTypeParameter(object? genericContext, int index) => null;

    public TypeName? GetFunctionPointerType(MethodSignature<TypeName?> signature) => null;

    public TypeName? GetModifiedType(
        TypeName? modifier,
        TypeName? unmodifiedType,
        bool isRequired
    ) => unmodifiedType;
}

/// <summary>
/// Type and member definitions from the build output and the game's Managed directory, so a
/// rule naming a forbidden game type or member fails when that name does not exist.
/// </summary>
internal sealed class ReferenceUniverse
{
    private readonly Dictionary<string, HashSet<string>> _members;
    private readonly HashSet<string> _namespaces;

    private ReferenceUniverse(
        Dictionary<string, HashSet<string>> members,
        HashSet<string> namespaces
    )
    {
        _members = members;
        _namespaces = namespaces;
    }

    internal static ReferenceUniverse Load(IEnumerable<string> directories)
    {
        var members = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (
            var path in directories
                .SelectMany(directory => Directory.EnumerateFiles(directory, "*.dll"))
                .Distinct(StringComparer.Ordinal)
        )
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
                continue;
            var reader = pe.GetMetadataReader();
            var names = new TypeNames(reader, "");
            foreach (var handle in reader.TypeDefinitions)
            {
                var definition = reader.GetTypeDefinition(handle);
                var name = names.Definition(handle);
                namespaces.Add(name.Namespace);
                if (!members.TryGetValue(name.FullName, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    members.Add(name.FullName, set);
                }

                foreach (var method in definition.GetMethods())
                    set.Add(reader.GetString(reader.GetMethodDefinition(method).Name));
                foreach (var field in definition.GetFields())
                    set.Add(reader.GetString(reader.GetFieldDefinition(field).Name));
            }
        }

        return new ReferenceUniverse(members, namespaces);
    }

    /// <summary>Fails unless <paramref name="fullName"/> names a defined type.</summary>
    internal string RequireType(string fullName)
    {
        if (!_members.ContainsKey(fullName))
            throw new InvalidOperationException($"Rule target type '{fullName}' does not resolve.");
        return fullName;
    }

    /// <summary>Fails unless the type defines a method or field named <paramref name="member"/>.</summary>
    internal (string Type, string Member) RequireMember(string fullName, string member)
    {
        RequireType(fullName);
        if (!_members[fullName].Contains(member))
            throw new InvalidOperationException(
                $"Rule target member '{fullName}::{member}' does not resolve."
            );
        return (fullName, member);
    }

    /// <summary>Fails unless some defined type lives in <paramref name="ns"/> or below.</summary>
    internal string RequireNamespace(string ns)
    {
        if (!_namespaces.Any(candidate => CompiledBuild.IsInNamespace(candidate, ns)))
            throw new InvalidOperationException($"Rule target namespace '{ns}' does not resolve.");
        return ns;
    }
}
