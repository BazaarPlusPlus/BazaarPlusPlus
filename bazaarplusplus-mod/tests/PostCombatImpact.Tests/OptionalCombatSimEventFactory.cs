#nullable enable
using System.Reflection;
using System.Reflection.Emit;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarPlusPlus.GameInterop.CombatSimulation;

namespace PostCombatImpact.Tests;

internal static class OptionalCombatSimEventFactory
{
    private static readonly Lazy<Type> CardActionCostSpentType = new(
        ResolveCardActionCostSpentType
    );

    internal static ICombatSimEvent CardActionCostSpent(
        string sourceId,
        EPlayerAttributeType? playerAttributeSpent,
        ECardAttributeType? cardAttributeSpent
    )
    {
        var instance =
            Activator.CreateInstance(CardActionCostSpentType.Value)
            ?? throw new InvalidOperationException("Could not create card-action-cost event.");

        SetProperty(instance, "ExecutingCard", InstanceId.TryParse(sourceId));
        SetProperty(instance, "PlayerAttributeSpent", playerAttributeSpent);
        SetProperty(instance, "CardAttributeSpent", cardAttributeSpent);
        return (ICombatSimEvent)instance;
    }

    private static Type ResolveCardActionCostSpentType() =>
        typeof(ICombatSimEvent).Assembly.GetType(
            CardActionCostSpentEventReader.RuntimeTypeName,
            throwOnError: false
        ) ?? CreateSurrogateType();

    private static Type CreateSurrogateType()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("PostCombatImpact.OptionalCombatSimEvents"),
            AssemblyBuilderAccess.Run
        );
        var module = assembly.DefineDynamicModule("OptionalCombatSimEvents");
        var type = module.DefineType(
            CardActionCostSpentEventReader.RuntimeTypeName,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed
        );
        type.AddInterfaceImplementation(typeof(ICombatSimEvent));
        type.DefineDefaultConstructor(MethodAttributes.Public);
        DefineAutoProperty<InstanceId>(type, "ExecutingCard");
        DefineAutoProperty<EPlayerAttributeType?>(type, "PlayerAttributeSpent");
        DefineAutoProperty<ECardAttributeType?>(type, "CardAttributeSpent");
        return type.CreateType()
            ?? throw new InvalidOperationException("Could not create optional event surrogate.");
    }

    private static void DefineAutoProperty<T>(TypeBuilder type, string name)
    {
        var field = type.DefineField($"_{name}", typeof(T), FieldAttributes.Private);
        var property = type.DefineProperty(name, PropertyAttributes.None, typeof(T), null);

        var getter = type.DefineMethod(
            $"get_{name}",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeof(T),
            Type.EmptyTypes
        );
        var getterIl = getter.GetILGenerator();
        getterIl.Emit(OpCodes.Ldarg_0);
        getterIl.Emit(OpCodes.Ldfld, field);
        getterIl.Emit(OpCodes.Ret);

        var setter = type.DefineMethod(
            $"set_{name}",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeof(void),
            new[] { typeof(T) }
        );
        var setterIl = setter.GetILGenerator();
        setterIl.Emit(OpCodes.Ldarg_0);
        setterIl.Emit(OpCodes.Ldarg_1);
        setterIl.Emit(OpCodes.Stfld, field);
        setterIl.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
        property.SetSetMethod(setter);
    }

    private static void SetProperty(object instance, string name, object? value)
    {
        var property =
            instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Optional event property '{name}' is missing.");
        property.SetValue(instance, value);
    }
}
