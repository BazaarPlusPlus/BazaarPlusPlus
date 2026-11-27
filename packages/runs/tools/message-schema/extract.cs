#:property PublishAot=false
// Reads the MessagePack layout of the game's network messages from one assembly of a Game
// Assembly Snapshot and prints it as JSON. runs/tools/message-schema/generate.ts merges the output
// into the Game Message Schema; it does not interpret anything itself.
//
//   dotnet run extract.cs -- <assembly.dll> attributes <out.json>   desktop BazaarGameShared.dll:
//                                                                    [Key] and [Union]
//   dotnet run extract.cs -- <assembly.dll> names <out.json>        Android Il2CppBazaarGameShared.dll:
//                                                                    no attributes, so member names only
//
// Types are written by qualified name; generic arguments recurse, e.g.
// System.Collections.Generic.HashSet<BazaarGameShared.Infra.Messages.Shared.IEffectTarget>.
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json.Nodes;

if (args.Length != 3 || (args[1] != "attributes" && args[1] != "names"))
{
    Console.Error.WriteLine("usage: extract.cs <assembly.dll> attributes|names <out.json>");
    return 2;
}

const string Root = "BazaarGameShared.Infra.Messages.INetMessage";
var assemblies = new Assemblies(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
var main = assemblies.Load(Path.GetFullPath(args[0]));
var types = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);

if (args[1] == "attributes")
{
    var pending = new Queue<string>();
    pending.Enqueue(Root);
    var seen = new HashSet<string>();
    while (pending.Count > 0)
    {
        var name = pending.Dequeue();
        if (!seen.Add(name)) continue;
        var found = assemblies.Find(name);
        if (found is null)
        {
            if (!Names.IsSystem(name)) throw new InvalidOperationException($"Cannot resolve {name}");
            continue;
        }
        var (asm, handle) = found.Value;
        var layout = asm.Layout(handle, keyed: true);
        if (layout is null) continue;
        types[name] = layout.Value.Json;
        foreach (var reference in layout.Value.References) pending.Enqueue(reference);
    }
}
else
{
    foreach (var handle in main.Reader.TypeDefinitions)
    {
        var name = main.TypeName(handle);
        if (name.Contains('<') || !name.Contains('.')) continue;
        var layout = main.Layout(handle, keyed: false);
        if (layout is not null) types[name] = layout.Value.Json;
    }
}

var output = new JsonObject { ["assembly"] = main.Name, ["mode"] = args[1], ["types"] = new JsonObject() };
foreach (var (name, layout) in types) output["types"]![name] = layout;
File.WriteAllText(args[2], output.ToJsonString());
return 0;

static class Names
{
    public static bool IsSystem(string name) => name.StartsWith("System.", StringComparison.Ordinal);

    // Il2CppInterop prefixes every namespace: Il2CppSystem.* for the BCL, Il2Cpp<Ns> for the game.
    public static string Normalize(string ns) =>
        ns == "Il2CppSystem" ? "System"
        : ns.StartsWith("Il2CppSystem.", StringComparison.Ordinal) ? "System." + ns["Il2CppSystem.".Length..]
        : ns.StartsWith("Il2CppBazaar", StringComparison.Ordinal) ? ns[6..]
        : ns;

    public static string Primitive(PrimitiveTypeCode code) => code switch
    {
        PrimitiveTypeCode.Boolean => "System.Boolean",
        PrimitiveTypeCode.Byte => "System.Byte",
        PrimitiveTypeCode.SByte => "System.SByte",
        PrimitiveTypeCode.Char => "System.Char",
        PrimitiveTypeCode.Int16 => "System.Int16",
        PrimitiveTypeCode.UInt16 => "System.UInt16",
        PrimitiveTypeCode.Int32 => "System.Int32",
        PrimitiveTypeCode.UInt32 => "System.UInt32",
        PrimitiveTypeCode.Int64 => "System.Int64",
        PrimitiveTypeCode.UInt64 => "System.UInt64",
        PrimitiveTypeCode.Single => "System.Single",
        PrimitiveTypeCode.Double => "System.Double",
        PrimitiveTypeCode.String => "System.String",
        PrimitiveTypeCode.Object => "System.Object",
        PrimitiveTypeCode.IntPtr => "System.IntPtr",
        PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
        PrimitiveTypeCode.Void => "System.Void",
        _ => throw new InvalidOperationException($"Unexpected primitive {code}"),
    };

    // A type expression: Name, Name<Arg,...>, or Element[]. Il2CppInterop arrays become T[].
    public static string Generic(string definition, ImmutableArray<string> arguments)
    {
        var bare = definition.Split('`')[0];
        if (bare.EndsWith(".Il2CppStructArray", StringComparison.Ordinal)
            || bare.EndsWith(".Il2CppReferenceArray", StringComparison.Ordinal))
            return arguments[0] + "[]";
        return bare + "<" + string.Join(",", arguments) + ">";
    }
}

sealed class Assemblies(string directory)
{
    private readonly Dictionary<string, Assembly> _byName = new(StringComparer.OrdinalIgnoreCase);

    public Assembly Load(string path)
    {
        var assembly = new Assembly(this, path);
        _byName[assembly.Name] = assembly;
        return assembly;
    }

    public Assembly? ByName(string name)
    {
        if (_byName.TryGetValue(name, out var found)) return found;
        var path = Path.Combine(directory, name + ".dll");
        return File.Exists(path) ? Load(path) : null;
    }

    public (Assembly, TypeDefinitionHandle)? Find(string name)
    {
        foreach (var assembly in _byName.Values.ToList())
            if (assembly.Lookup(name) is { } handle) return (assembly, handle);
        foreach (var file in Directory.GetFiles(directory, "*.dll").Order(StringComparer.Ordinal))
        {
            var simple = Path.GetFileNameWithoutExtension(file);
            if (_byName.ContainsKey(simple) || simple.StartsWith("System", StringComparison.Ordinal)
                || simple.StartsWith("Unity", StringComparison.Ordinal) || simple.StartsWith("Microsoft", StringComparison.Ordinal))
                continue;
            if (ByName(simple)?.Lookup(name) is { } handle) return (_byName[simple], handle);
        }
        return null;
    }
}

readonly record struct Layout(JsonNode Json, IReadOnlyList<string> References);

sealed class Assembly : ISignatureTypeProvider<string, object?>
{
    private readonly Assemblies _all;
    private readonly Dictionary<string, TypeDefinitionHandle> _types = new(StringComparer.Ordinal);
    public MetadataReader Reader { get; }
    public string Name { get; }

    public Assembly(Assemblies all, string path)
    {
        _all = all;
        var pe = new PEReader(File.OpenRead(path));
        Reader = pe.GetMetadataReader();
        Name = Reader.GetString(Reader.GetAssemblyDefinition().Name);
        foreach (var handle in Reader.TypeDefinitions) _types[TypeName(handle)] = handle;
    }

    public TypeDefinitionHandle? Lookup(string name) => _types.TryGetValue(name, out var h) ? h : null;

    public string TypeName(TypeDefinitionHandle handle)
    {
        var type = Reader.GetTypeDefinition(handle);
        var name = Reader.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil) return TypeName(declaring) + "+" + name;
        var ns = Names.Normalize(Reader.GetString(type.Namespace));
        return ns.Length > 0 ? ns + "." + name : name;
    }

    private string ReferenceName(TypeReferenceHandle handle)
    {
        var reference = Reader.GetTypeReference(handle);
        var name = Reader.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
            return ReferenceName((TypeReferenceHandle)reference.ResolutionScope) + "+" + name;
        var ns = Names.Normalize(Reader.GetString(reference.Namespace));
        return ns.Length > 0 ? ns + "." + name : name;
    }

    private string AttributeName(CustomAttribute attribute) => attribute.Constructor.Kind switch
    {
        HandleKind.MemberReference => Reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent is var parent
            && parent.Kind == HandleKind.TypeReference
                ? ReferenceName((TypeReferenceHandle)parent)
                : "?",
        HandleKind.MethodDefinition => TypeName(Reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType()),
        _ => "?",
    };

    private IEnumerable<(CustomAttribute Attribute, string Name)> Attributes(CustomAttributeHandleCollection handles) =>
        handles.Select(h => Reader.GetCustomAttribute(h)).Select(a => (a, AttributeName(a)));

    private string? BaseName(TypeDefinition type) => type.BaseType.IsNil ? null : type.BaseType.Kind switch
    {
        HandleKind.TypeReference => ReferenceName((TypeReferenceHandle)type.BaseType),
        HandleKind.TypeDefinition => TypeName((TypeDefinitionHandle)type.BaseType),
        HandleKind.TypeSpecification => Reader.GetTypeSpecification((TypeSpecificationHandle)type.BaseType).DecodeSignature(this, null),
        _ => null,
    };

    public Layout? Layout(TypeDefinitionHandle handle, bool keyed)
    {
        var type = Reader.GetTypeDefinition(handle);
        var baseName = BaseName(type);
        if (baseName == "System.Enum") return Enum(type);
        var attributes = Attributes(type.GetCustomAttributes()).ToList();
        if (keyed)
        {
            var cases = attributes.Where(a => a.Name == "MessagePack.UnionAttribute")
                .Select(a => Union(a.Attribute)).OrderBy(c => c.Tag).ToList();
            if (cases.Count > 0)
                return new Layout(
                    new JsonObject
                    {
                        ["kind"] = "union",
                        ["cases"] = new JsonArray(cases.Select(c => (JsonNode)new JsonArray(c.Tag, c.Type)).ToArray()),
                    },
                    cases.Select(c => c.Type).ToList());
            if (!attributes.Any(a => a.Name == "MessagePack.MessagePackObjectAttribute"))
                throw new InvalidOperationException($"{TypeName(handle)} is neither a union nor a MessagePackObject");
        }
        else if ((type.Attributes & TypeAttributes.Interface) != 0)
        {
            return new Layout(new JsonObject { ["kind"] = "interface" }, []);
        }

        var members = new List<(int? Key, string Name, string Type)>();
        var references = new List<string>();
        for (TypeDefinitionHandle? current = handle; current is { } h;)
        {
            var definition = Reader.GetTypeDefinition(h);
            foreach (var (key, name, memberType) in Members(definition, keyed))
            {
                members.Add((key, name, memberType));
                references.AddRange(Parse(memberType));
            }
            current = Base(definition, keyed);
        }
        members.Sort((a, b) => keyed ? a.Key!.Value.CompareTo(b.Key!.Value) : string.CompareOrdinal(a.Name, b.Name));
        if (keyed && members.Select(m => m.Key).Distinct().Count() != members.Count)
            throw new InvalidOperationException($"{TypeName(handle)} repeats a [Key]");
        var json = new JsonArray(members.Select(m => (JsonNode)(keyed
            ? new JsonArray(m.Key!.Value, m.Name, m.Type)
            : new JsonArray(m.Name, m.Type))).ToArray());
        return new Layout(new JsonObject { ["kind"] = "object", ["members"] = json }, references);
    }

    // The base type, when it is a game type that may declare keyed members.
    private TypeDefinitionHandle? Base(TypeDefinition type, bool keyed)
    {
        var name = BaseName(type);
        if (name is null || Names.IsSystem(name) || name.Contains('<')) return null;
        if (Lookup(name) is { } local) return local;
        var found = keyed ? _all.Find(name) : null;
        return found is null ? null : throw new InvalidOperationException($"Base {name} lives in another assembly");
    }

    private IEnumerable<(int? Key, string Name, string Type)> Members(TypeDefinition type, bool keyed)
    {
        foreach (var fh in type.GetFields())
        {
            var field = Reader.GetFieldDefinition(fh);
            if ((field.Attributes & FieldAttributes.Static) != 0) continue;
            var name = Reader.GetString(field.Name);
            if (keyed)
            {
                if (Key(field.GetCustomAttributes()) is { } key) yield return (key, name, field.DecodeSignature(this, null));
            }
        }
        foreach (var ph in type.GetProperties())
        {
            var property = Reader.GetPropertyDefinition(ph);
            var name = Reader.GetString(property.Name);
            var signature = property.DecodeSignature(this, null);
            if (!signature.Header.IsInstance) continue;
            if (keyed)
            {
                if (Key(property.GetCustomAttributes()) is { } key) yield return (key, name, signature.ReturnType);
            }
            // Il2CppInterop also exposes each auto-property's backing field; records add EqualityContract.
            else if (!name.EndsWith("k__BackingField", StringComparison.Ordinal) && name != "EqualityContract")
            {
                yield return (null, name, signature.ReturnType);
            }
        }
    }

    private int? Key(CustomAttributeHandleCollection handles)
    {
        foreach (var (attribute, name) in Attributes(handles))
        {
            if (name != "MessagePack.KeyAttribute") continue;
            var blob = Reader.GetBlobReader(attribute.Value);
            blob.ReadUInt16();
            var constructor = Reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            var parameters = constructor.DecodeMethodSignature(this, null).ParameterTypes;
            if (parameters.Length != 1 || parameters[0] != "System.Int32")
                throw new InvalidOperationException("Only integer [Key] attributes are supported");
            return blob.ReadInt32();
        }
        return null;
    }

    // UnionAttribute(int key, Type subType): the Type argument is a serialized type name.
    private (int Tag, string Type) Union(CustomAttribute attribute)
    {
        var blob = Reader.GetBlobReader(attribute.Value);
        blob.ReadUInt16();
        var tag = blob.ReadInt32();
        var serialized = blob.ReadSerializedString() ?? throw new InvalidOperationException("Union without a type");
        return (tag, serialized.Split(',')[0].Trim());
    }

    private Layout Enum(TypeDefinition type)
    {
        string? underlying = null;
        var values = new JsonArray();
        foreach (var fh in type.GetFields())
        {
            var field = Reader.GetFieldDefinition(fh);
            if ((field.Attributes & FieldAttributes.Static) == 0)
            {
                underlying = field.DecodeSignature(this, null);
                continue;
            }
            if (field.GetDefaultValue().IsNil) continue;
            var constant = Reader.GetConstant(field.GetDefaultValue());
            var blob = Reader.GetBlobReader(constant.Value);
            // Text keeps a UInt64 or Int64 value exact through JSON.
            var value = constant.TypeCode switch
            {
                ConstantTypeCode.SByte => blob.ReadSByte().ToString(),
                ConstantTypeCode.Byte => blob.ReadByte().ToString(),
                ConstantTypeCode.Int16 => blob.ReadInt16().ToString(),
                ConstantTypeCode.UInt16 => blob.ReadUInt16().ToString(),
                ConstantTypeCode.Int32 => blob.ReadInt32().ToString(),
                ConstantTypeCode.UInt32 => blob.ReadUInt32().ToString(),
                ConstantTypeCode.Int64 => blob.ReadInt64().ToString(),
                ConstantTypeCode.UInt64 => blob.ReadUInt64().ToString(),
                _ => throw new InvalidOperationException($"Enum constant {constant.TypeCode}"),
            };
            values.Add(new JsonArray(Reader.GetString(field.Name), value));
        }
        var flags = Attributes(type.GetCustomAttributes()).Any(a => a.Name == "System.FlagsAttribute");
        return new Layout(
            new JsonObject { ["kind"] = "enum", ["underlying"] = underlying ?? "System.Int32", ["flags"] = flags, ["values"] = values },
            []);
    }

    // Every named type inside a type expression, without System types.
    private static IEnumerable<string> Parse(string expression) =>
        expression.Split(['<', '>', ',', '[', ']'], StringSplitOptions.RemoveEmptyEntries)
            .Where(name => !Names.IsSystem(name) && !name.StartsWith('!'));

    public string GetArrayType(string elementType, ArrayShape shape) => throw new NotSupportedException("Multi-dimensional array");
    public string GetByReferenceType(string elementType) => elementType;
    public string GetFunctionPointerType(MethodSignature<string> signature) => throw new NotSupportedException("Function pointer");
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => Names.Generic(genericType, typeArguments);
    public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? context, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => Names.Primitive(typeCode);
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => TypeName(handle);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
        ReferenceName(handle) switch
        {
            "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray" => "System.String[]",
            var name => name,
        };
    public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, context);
}
