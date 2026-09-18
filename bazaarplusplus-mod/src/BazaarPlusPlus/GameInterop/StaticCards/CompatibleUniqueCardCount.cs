#nullable enable
using System.Reflection;
using System.Reflection.Emit;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Values.ReferenceValues;

namespace BazaarPlusPlus.GameInterop.StaticCards;

// Bind the override to the installed game's signature, so the same plugin can
// load on clients using either IEnumerable<ICard> or IReadOnlyList<ICard>.
// Retain the native parent for targeting, modifiers and implicit-card conditionals.
// Keep the generated type unregistered: native discriminator discovery must not collide.
internal static class CompatibleUniqueCardCount
{
    private const BindingFlags InstanceMethods =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Lazy<Type> Implementation = new(BuildImplementation);

    internal static Type RuntimeType => Implementation.Value;

    internal static TReferenceValueWithTargetCard Create() =>
        (TReferenceValueWithTargetCard)Activator.CreateInstance(RuntimeType)!;

    internal static bool IsInstance(object value) =>
        Implementation.IsValueCreated && value.GetType() == Implementation.Value;

    private static Type BuildImplementation()
    {
        var parent = typeof(TReferenceValueWithTargetCard);
        var countMethod =
            parent.GetMethod("GetValueFromTargets", InstanceMethods)
            ?? throw new NotSupportedException("The game has no target-card value override.");
        var parameters = countMethod.GetParameters();
        if (
            !countMethod.IsAbstract
            || countMethod.ReturnType != typeof(float?)
            || parameters.Length != 1
            || (
                parameters[0].ParameterType != typeof(IEnumerable<ICard>)
                && parameters[0].ParameterType != typeof(IReadOnlyList<ICard>)
            )
        )
            throw new NotSupportedException(
                $"Unsupported target-card value signature: {countMethod}"
            );

        var module = AssemblyBuilder
            .DefineDynamicAssembly(
                new AssemblyName("BazaarPlusPlus.UniqueCardCountCompatibility"),
                AssemblyBuilderAccess.Run
            )
            .DefineDynamicModule("Main");
        var type = module.DefineType(
            "BazaarPlusPlus.GameInterop.StaticCards.Runtime.UniqueCardCount",
            TypeAttributes.Public | TypeAttributes.Sealed,
            parent
        );
        type.DefineDefaultConstructor(MethodAttributes.Public);

        // A delegate keeps the counting logic in ordinary C# without exposing a
        // public mod API solely for calls from the generated assembly.
        var counterType = typeof(Func<IEnumerable<ICard>, float?>);
        var counter = type.DefineField(
            "CountTargets",
            counterType,
            FieldAttributes.Private | FieldAttributes.Static
        );
        var il = Override(type, countMethod).GetILGenerator();
        il.Emit(OpCodes.Ldsfld, counter);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, counterType.GetMethod("Invoke")!);
        il.Emit(OpCodes.Ret);

        // Preserve the native record contract, including with-expressions and
        // equality. All instance state remains in the native parent.
        var clone = parent
            .GetMethods(InstanceMethods)
            .Single(method => method.Name == "<Clone>$" && method.IsAbstract);
        il = Override(type, clone).GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(object).GetMethod("MemberwiseClone", InstanceMethods)!);
        il.Emit(OpCodes.Castclass, clone.ReturnType);
        il.Emit(OpCodes.Ret);

        var equalityContract = parent.GetProperty("EqualityContract", InstanceMethods)!.GetMethod!;
        il = Override(type, equalityContract).GetILGenerator();
        il.Emit(OpCodes.Ldtoken, type);
        il.Emit(OpCodes.Call, typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!);
        il.Emit(OpCodes.Ret);

        var implementation = type.CreateTypeInfo()!.AsType();
        implementation
            .GetField(counter.Name, BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(
                null,
                (Func<IEnumerable<ICard>, float?>)(
                    targets => targets.Select(card => card.TemplateId).Distinct().Count()
                )
            );
        return implementation;
    }

    private static MethodBuilder Override(TypeBuilder type, MethodInfo method)
    {
        var attributes =
            (method.Attributes & (MethodAttributes.MemberAccessMask | MethodAttributes.SpecialName))
            | MethodAttributes.Virtual
            | MethodAttributes.Final
            | MethodAttributes.HideBySig;
        var implementation = type.DefineMethod(
            method.Name,
            attributes,
            method.ReturnType,
            method.GetParameters().Select(parameter => parameter.ParameterType).ToArray()
        );
        type.DefineMethodOverride(implementation, method);
        return implementation;
    }
}
