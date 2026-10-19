using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using BazaarPlusPlus.Patches.CollectionPanel;
using HarmonyLib;
using Xunit;

namespace RuntimeIntegration.Tests;

public sealed class CollectionLockVariantPanelPatchTests
{
    static CollectionLockVariantPanelPatchTests()
    {
        var managed = typeof(CollectionLockVariantPanelPatchTests)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "BppManagedPath")
            .Value!;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = Path.Combine(managed, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
    }

    [Fact]
    public void Native_variant_setup_retains_its_body_and_broadens_only_the_upgradeable_guard()
    {
        var runtime = Assembly.Load("TheBazaarRuntime");
        var target = AccessTools.Method(
            runtime.GetType("TheBazaar.UI.Tooltips.CardTooltipController", true),
            "SetupLockVariantPanel"
        );
        var targets =
            (IEnumerable<MethodBase>)
                typeof(CollectionLockVariantPanelPatch)
                    .GetMethod("TargetMethods", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, null)!;
        if (target == null)
        {
            Assert.Empty(targets);
            return;
        }

        Assert.Equal(target, Assert.Single(targets));
        var generator = new DynamicMethod(
            "ReadNativeVariantSetup",
            typeof(void),
            Type.EmptyTypes
        ).GetILGenerator();
        var original = PatchProcessor.GetOriginalInstructions(target, generator);
        var patched = CollectionLockVariantPanelPatch.Transpiler(original).ToList();
        var guard = original.FindIndex(code =>
            code.opcode == OpCodes.Isinst
            && code.operand is Type { FullName: "TheBazaar.IUpgradeableCard" }
        );
        Assert.True(guard >= 0);
        Assert.Equal(original.Count + 3, patched.Count);
        Assert.Equal(original.Take(guard + 1), patched.Take(guard + 1));
        Assert.Equal(original.Skip(guard + 1), patched.Skip(guard + 4));
        Assert.Equal(OpCodes.Ldarg_0, patched[guard + 1].opcode);
        Assert.Equal("get_CurrentTooltipData", ((MethodInfo)patched[guard + 2].operand).Name);
        Assert.Equal("AllowCollection", ((MethodInfo)patched[guard + 3].operand).Name);
    }

    [Fact]
    public void Eligibility_preserves_native_cards_and_accepts_only_registered_collection_cards()
    {
        var runtime = Assembly.Load("TheBazaarRuntime");
        var tooltipType = runtime.GetType("TheBazaar.Tooltips.CardTooltipData", true)!;
        var cardField = tooltipType.GetField(
            "_cardInstance",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var cardType = cardField.FieldType.Assembly.GetType(
            "BazaarGameClient.Domain.Models.Cards.ItemCard",
            true
        )!;
        var card = RuntimeHelpers.GetUninitializedObject(cardType);
        var tooltip = RuntimeHelpers.GetUninitializedObject(tooltipType);
        cardField.SetValue(tooltip, card);
        var allow = typeof(CollectionLockVariantPanelPatch).GetMethod(
            "AllowCollection",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;
        var registry = Assembly
            .Load("BazaarPlusPlus")
            .GetType(
                "BazaarPlusPlus.Game.CollectionPanel.Tooltips.CollectionTierTooltipRegistry",
                true
            )!;
        var register = registry.GetMethod("Register")!;
        var unregister = registry.GetMethod("Unregister")!;
        var native = new object();

        Assert.Same(native, allow.Invoke(null, [native, null]));
        Assert.Null(allow.Invoke(null, [null, null]));
        Assert.Null(allow.Invoke(null, [null, tooltip]));
        register.Invoke(null, [card]);
        try
        {
            Assert.Same(tooltip, allow.Invoke(null, [null, tooltip]));
            Assert.Same(native, allow.Invoke(null, [native, tooltip]));
        }
        finally
        {
            unregister.Invoke(null, [card]);
        }
        Assert.Null(allow.Invoke(null, [null, tooltip]));
    }

    [Fact]
    public void Changed_native_guard_fails_instead_of_patching_an_unrelated_branch()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CollectionLockVariantPanelPatch.Transpiler([new CodeInstruction(OpCodes.Ret)]).ToList()
        );
    }
}
