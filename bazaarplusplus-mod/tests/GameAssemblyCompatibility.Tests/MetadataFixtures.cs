using System.Reflection;
using System.Reflection.Emit;

namespace GameAssemblyCompatibility.Tests;

internal sealed class MetadataFixtures : IDisposable
{
    public string Root { get; } =
        Path.Combine(Path.GetTempPath(), "bpp-member-tests-" + Guid.NewGuid().ToString("N"));

    public MetadataFixtures() => Directory.CreateDirectory(Root);

    public string FilePath(string name) => Path.Combine(Root, name);

    public void Dispose() => Directory.Delete(Root, true);

    public string Library(string name, string shape = "params")
    {
        var api = Api(shape);
        return Save(api.Assembly, name);
    }

    public string Consumer(string name, string shape = "params", bool typeOnly = false)
    {
        var api = Api(shape);
        var assembly = Assembly("Consumer");
        var type = assembly
            .DefineDynamicModule("Consumer")
            .DefineType("Consumer", TypeAttributes.Public);
        var method = type.DefineMethod(
            "Use",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes
        );
        var il = method.GetILGenerator();
        if (typeOnly)
            il.Emit(OpCodes.Ldtoken, api.Type);
        else if (api.Member is FieldInfo field)
            il.Emit(OpCodes.Ldtoken, field);
        else if (api.Member is ConstructorInfo constructor)
        {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newobj, constructor);
        }
        else
            il.Emit(OpCodes.Ldtoken, (MethodInfo)api.Member);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        type.CreateType();
        return Save(assembly, name);
    }

    public (string Runtime, string Consumer) Attribute(string shape, bool field = false)
    {
        var runtime = AttributeApi(shape, field);
        var reference = AttributeApi("valid", field);
        var assembly = Assembly("Consumer");
        var type = assembly
            .DefineDynamicModule("Consumer")
            .DefineType("Consumer", TypeAttributes.Public);
        type.SetCustomAttribute(
            field
                ? new CustomAttributeBuilder(
                    reference.Constructor,
                    [],
                    [(FieldInfo)reference.NamedMember!],
                    [42]
                )
                : new CustomAttributeBuilder(
                    reference.Constructor,
                    [],
                    [(PropertyInfo)reference.NamedMember!],
                    [42]
                )
        );
        type.CreateType();
        return (Save(runtime.Assembly, "runtime.dll"), Save(assembly, "consumer.dll"));
    }

    private static (
        PersistedAssemblyBuilder Assembly,
        ConstructorInfo Constructor,
        MemberInfo? NamedMember
    ) AttributeApi(string shape, bool field)
    {
        var assembly = Assembly("Fixture.Json");
        var type = assembly
            .DefineDynamicModule("API")
            .DefineType("Fixture.DisplayAttribute", TypeAttributes.Public, typeof(Attribute));
        var constructor = type.DefineDefaultConstructor(MethodAttributes.Public);
        MemberInfo? namedMember = null;
        if (shape != "missing")
        {
            var valueType = shape == "type" ? typeof(long) : typeof(int);
            if (field)
                namedMember = type.DefineField(
                    "Order",
                    valueType,
                    FieldAttributes.Public | (shape == "readonly" ? FieldAttributes.InitOnly : 0)
                );
            else
            {
                var property = type.DefineProperty(
                    "Order",
                    PropertyAttributes.None,
                    valueType,
                    Type.EmptyTypes
                );
                namedMember = property;
                if (shape != "readonly")
                {
                    var setter = type.DefineMethod(
                        "set_Order",
                        MethodAttributes.Public | MethodAttributes.SpecialName,
                        typeof(void),
                        [valueType]
                    );
                    setter.GetILGenerator().Emit(OpCodes.Ret);
                    property.SetSetMethod(setter);
                }
            }
        }
        type.CreateType();
        return (assembly, constructor, namedMember);
    }

    private static (PersistedAssemblyBuilder Assembly, Type Type, MemberInfo Member) Api(
        string shape
    )
    {
        var assembly = Assembly("Fixture.Json");
        var module = assembly.DefineDynamicModule("API");
        var type = module.DefineType(
            shape == "missing_type" ? "Fixture.Other" : "Fixture.Token",
            TypeAttributes.Public
        );
        MemberInfo member;
        if (shape is "field_generic" or "field_generic_concrete")
        {
            var parameter = type.DefineGenericParameters("T")[0];
            var field = type.DefineField(
                "Value",
                shape == "field_generic" ? parameter : typeof(int),
                FieldAttributes.Public
            );
            type.CreateType();
            var constructed = type.MakeGenericType(typeof(int));
            return (assembly, constructed, TypeBuilder.GetField(constructed, field));
        }
        if (shape.StartsWith("field", StringComparison.Ordinal))
            member = type.DefineField(
                "Value",
                shape == "field_type" ? typeof(long) : typeof(int),
                FieldAttributes.Public
            );
        else if (shape is "generic" or "generic_concrete" or "generic_base" or "derived_generic")
        {
            if (shape is "generic" or "generic_concrete")
            {
                var parameter = type.DefineGenericParameters("T")[0];
                Type signatureType = shape == "generic_concrete" ? typeof(int) : parameter;
                var method = type.DefineMethod(
                    "Call",
                    MethodAttributes.Public | MethodAttributes.Static,
                    signatureType,
                    [signatureType]
                );
                method.GetILGenerator().Emit(OpCodes.Ldarg_0);
                method.GetILGenerator().Emit(OpCodes.Ret);
                type.CreateType();
                var constructed = type.MakeGenericType(typeof(int));
                return (assembly, constructed, TypeBuilder.GetMethod(constructed, method));
            }
            var baseType = module.DefineType("Fixture.Base`1", TypeAttributes.Public);
            var baseParameter = baseType.DefineGenericParameters("T")[0];
            baseType.DefineDefaultConstructor(MethodAttributes.Public);
            if (shape == "generic_base")
            {
                var baseMethod = baseType.DefineMethod(
                    "Call",
                    MethodAttributes.Public,
                    baseParameter,
                    [baseParameter]
                );
                baseMethod.GetILGenerator().Emit(OpCodes.Ldarg_1);
                baseMethod.GetILGenerator().Emit(OpCodes.Ret);
            }
            baseType.CreateType();
            type.SetParent(baseType.MakeGenericType(typeof(int)));
            var derivedMethod = type.DefineMethod(
                shape == "generic_base" ? "Unrelated" : "Call",
                MethodAttributes.Public,
                typeof(int),
                [typeof(int)]
            );
            derivedMethod.GetILGenerator().Emit(OpCodes.Ldarg_1);
            derivedMethod.GetILGenerator().Emit(OpCodes.Ret);
            member = derivedMethod;
        }
        else if (shape is "base_ctor" or "derived_ctor")
        {
            var baseType = module.DefineType("Fixture.Base", TypeAttributes.Public);
            baseType.DefineDefaultConstructor(MethodAttributes.Public);
            var baseConstructor = baseType.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                [typeof(int)]
            );
            baseConstructor.GetILGenerator().Emit(OpCodes.Ret);
            baseType.CreateType();
            type.SetParent(baseType);
            var constructor = type.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                shape == "base_ctor" ? Type.EmptyTypes : [typeof(int)]
            );
            constructor.GetILGenerator().Emit(OpCodes.Ret);
            member = constructor;
        }
        else
        {
            var parameters = shape switch
            {
                "single" => new[] { typeof(int) },
                "parameter" => [typeof(long), typeof(object[])],
                "byref" => [typeof(int).MakeByRefType(), typeof(object[])],
                "array_rank" => [typeof(int), typeof(object[,])],
                _ => [typeof(int), typeof(object[])],
            };
            var method = type.DefineMethod(
                "Call",
                MethodAttributes.Public | (shape == "static" ? MethodAttributes.Static : 0),
                shape == "return" ? typeof(object) : typeof(string),
                parameters
            );
            if (shape == "generic_arity")
                method.DefineGenericParameters("T");
            method.GetILGenerator().Emit(OpCodes.Ldnull);
            method.GetILGenerator().Emit(OpCodes.Ret);
            member = method;
        }
        type.CreateType();
        return (assembly, type, member);
    }

    private static PersistedAssemblyBuilder Assembly(string name) =>
        new(new AssemblyName(name), typeof(object).Assembly);

    private string Save(PersistedAssemblyBuilder assembly, string name)
    {
        var path = FilePath(name);
        using var stream = File.Create(path);
        assembly.Save(stream);
        return path;
    }
}
