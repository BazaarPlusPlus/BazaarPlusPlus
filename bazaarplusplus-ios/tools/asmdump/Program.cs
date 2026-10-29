// asmdump <assembly> <Namespace.Type>: prints layout flags and declared instance fields of a type.
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using var fs = File.OpenRead(args[0]);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();
foreach (var h in md.TypeDefinitions)
{
    var t = md.GetTypeDefinition(h);
    var full = $"{md.GetString(t.Namespace)}.{md.GetString(t.Name)}";
    if (full != args[1]) continue;
    Console.WriteLine($"{full} layout={t.Attributes & TypeAttributes.LayoutMask} pack={t.GetLayout().PackingSize} size={t.GetLayout().Size}");
    foreach (var fh in t.GetFields())
    {
        var f = md.GetFieldDefinition(fh);
        if ((f.Attributes & FieldAttributes.Static) != 0) continue;
        var sig = md.GetBlobReader(f.Signature);
        sig.ReadSignatureHeader();
        var code = sig.ReadSignatureTypeCode();
        Console.WriteLine($"  {md.GetString(f.Name)} : {code}");
    }
    var provider = new SigProvider();
    foreach (var mh in t.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        if (args.Length > 2 && !md.GetString(m.Name).Contains(args[2])) continue;
        var sig = m.DecodeSignature(provider, null);
        Console.WriteLine($"  method {sig.ReturnType} {md.GetString(m.Name)}({string.Join(", ", sig.ParameterTypes)}) impl={m.ImplAttributes} attrs={m.Attributes}");
    }
}

sealed class SigProvider : ISignatureTypeProvider<string, object>
{
    public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString();
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => r.GetString(r.GetTypeDefinition(h).Name);
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) => r.GetString(r.GetTypeReference(h).Name);
    public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => "spec";
    public string GetSZArrayType(string e) => e + "[]";
    public string GetArrayType(string e, ArrayShape s) => e + "[,]";
    public string GetByReferenceType(string e) => "ref " + e;
    public string GetPointerType(string e) => e + "*";
    public string GetGenericInstantiation(string g, System.Collections.Immutable.ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
    public string GetGenericTypeParameter(object c, int i) => "!" + i;
    public string GetGenericMethodParameter(object c, int i) => "!!" + i;
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
    public string GetModifiedType(string m, string u, bool r) => u;
    public string GetPinnedType(string e) => e;
}
