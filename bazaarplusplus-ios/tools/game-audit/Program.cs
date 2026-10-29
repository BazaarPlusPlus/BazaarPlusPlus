// Metadata-only inventory: never load or execute game assemblies.
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
var imports = new List<object>();
var icalls = new List<object>();
var refs = new HashSet<string>();
foreach (string path in Directory.EnumerateFiles(args[0], "*.dll").Order()) {
    using var file = File.OpenRead(path); using var pe = new PEReader(file);
    if (!pe.HasMetadata) continue;
    var md = pe.GetMetadataReader();
    string TypeName(TypeDefinitionHandle handle) {
        var type = md.GetTypeDefinition(handle);
        var parent = type.GetDeclaringType();
        return parent.IsNil ? (md.GetString(type.Namespace) + "." + md.GetString(type.Name)).TrimStart('.') : TypeName(parent) + "/" + md.GetString(type.Name);
    }
    foreach (var handle in md.TypeDefinitions) {
        var type = md.GetTypeDefinition(handle); string full = TypeName(handle);
        foreach (var methodHandle in type.GetMethods()) {
            var method = md.GetMethodDefinition(methodHandle); string name = md.GetString(method.Name);
            if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0) {
                var import = method.GetImport();
                imports.Add(new { assembly = Path.GetFileName(path), type = full, method = name,
                    library = md.GetString(md.GetModuleReference(import.Module).Name), entry = md.GetString(import.Name) });
            }
            if ((method.ImplAttributes & MethodImplAttributes.InternalCall) != 0)
                icalls.Add(new { assembly = Path.GetFileName(path), type = full, method = name,
                    registration = "Register_" + full.Replace('.', '_').Replace('/', '_') + "_" + name });
        }
    }
    foreach (var handle in md.MemberReferences) {
        var member = md.GetMemberReference(handle);
        if (member.Parent.Kind != HandleKind.TypeReference) continue;
        var type = md.GetTypeReference((TypeReferenceHandle)member.Parent);
        if (md.GetString(type.Namespace).StartsWith("Unity"))
            refs.Add(md.GetString(type.Namespace) + "." + md.GetString(type.Name) + "::" + md.GetString(member.Name));
    }
}
Console.WriteLine(JsonSerializer.Serialize(new { imports, icalls, unityMemberReferences = refs.Order().ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
