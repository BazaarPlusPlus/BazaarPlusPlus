#nullable enable
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using BazaarPlusPlus.Android.GameInterop;
using Xunit;

namespace AndroidHost.Tests;

// The Run Message Stream keys a command's arguments by the HttpGameClient method's parameter
// names. The desktop reads them from the method it patches; the Android Host writes them by hand,
// so the Interop Snapshot's metadata checks them.
public sealed class AndroidRunCommandsTests
{
    // SendCommand is the seam every command passes; the desktop does not record SendCheat either.
    private static readonly string[] Unrecorded = ["SendCommand", "SendCheat"];

    [Fact]
    public void Each_command_names_a_game_method_and_its_parameters()
    {
        var methods = GameCommandMethods();

        foreach (var shape in AndroidRunCommands.All)
        {
            Assert.True(
                methods.TryGetValue(shape.Method, out var parameters),
                $"HttpGameClient has no {shape.Method}."
            );
            Assert.Equal(parameters, shape.Parameters);
        }
    }

    [Fact]
    public void Every_game_command_method_is_recorded()
    {
        var recorded = AndroidRunCommands.All.Select(shape => shape.Method).ToArray();
        var missing = GameCommandMethods()
            .Keys.Where(name => !recorded.Contains(name) && !Unrecorded.Contains(name))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"HttpGameClient sends {string.Join(", ", missing)}, which the stream does not record."
        );
    }

    // The methods that return the command's response: Task<Result<INetMessage>>.
    private static Dictionary<string, string[]> GameCommandMethods()
    {
        var interop = typeof(AndroidRunCommandsTests)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "BppInteropPath")
            .Value!;
        using var stream = File.OpenRead(Path.Combine(interop, "Il2CppTheBazaarRuntime.dll"));
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();
        var client = metadata
            .TypeDefinitions.Select(metadata.GetTypeDefinition)
            .Single(definition =>
                metadata.GetString(definition.Namespace) == "Il2CppNetworking"
                && metadata.GetString(definition.Name) == "HttpGameClient"
            );
        var methods = new Dictionary<string, string[]>();
        foreach (var handle in client.GetMethods())
        {
            var method = metadata.GetMethodDefinition(handle);
            var returns = method.DecodeSignature(new TypeNames(), null).ReturnType;
            if (returns != "Task`1<Result`1<INetMessage>>")
                continue;
            methods.Add(
                metadata.GetString(method.Name),
                method
                    .GetParameters()
                    .Select(metadata.GetParameter)
                    .Where(parameter => parameter.SequenceNumber > 0)
                    .OrderBy(parameter => parameter.SequenceNumber)
                    .Select(parameter => metadata.GetString(parameter.Name))
                    .ToArray()
            );
        }
        Assert.NotEmpty(methods);
        return methods;
    }

    private sealed class TypeNames : ISignatureTypeProvider<string, object?>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetTypeFromDefinition(
            MetadataReader metadata,
            TypeDefinitionHandle handle,
            byte rawTypeKind
        ) => metadata.GetString(metadata.GetTypeDefinition(handle).Name);

        public string GetTypeFromReference(
            MetadataReader metadata,
            TypeReferenceHandle handle,
            byte rawTypeKind
        ) => metadata.GetString(metadata.GetTypeReference(handle).Name);

        public string GetTypeFromSpecification(
            MetadataReader metadata,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind
        ) => metadata.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetGenericInstantiation(
            string genericType,
            System.Collections.Immutable.ImmutableArray<string> typeArguments
        ) => $"{genericType}<{string.Join(",", typeArguments)}>";

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
