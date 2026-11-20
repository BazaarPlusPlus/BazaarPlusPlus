using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using BazaarPlusPlus.Patches.Settings;
using HarmonyLib;
using UnityEngine;
using Xunit;

namespace RuntimeIntegration.Tests;

public sealed class FullscreenResolutionPatchTests
{
    static FullscreenResolutionPatchTests()
    {
        var managed = typeof(FullscreenResolutionPatchTests)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "BppManagedPath")
            .Value!;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = Path.Combine(managed, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
    }

    [Fact]
    public void Applying_display_preferences_replaces_the_single_native_resolution_call()
    {
        var target = typeof(FullscreenResolutionPatch).GetCustomAttribute<HarmonyPatch>()!.info;
        var original = ReadInstructions(
            AccessTools.DeclaredMethod(target.declaringType, target.methodName)
        );
        var native = AccessTools.Method(
            typeof(Screen),
            nameof(Screen.SetResolution),
            [typeof(int), typeof(int), typeof(FullScreenMode)]
        );
        var call = Assert.Single(original, code => code.Calls(native));
        var index = original.IndexOf(call);
        var patched = FullscreenResolutionPatch.Transpiler(original).ToList();

        Assert.Equal(original.Count, patched.Count);
        Assert.True(
            patched[index]
                .Calls(AccessTools.Method(typeof(FullscreenResolutionPatch), "SetResolution"))
        );
        Assert.DoesNotContain(patched, code => code.Calls(native));
    }

    [Fact]
    public void Dialog_awake_reads_the_resolution_index_from_the_prefixed_method()
    {
        var target = typeof(FullscreenResolutionIndexPatch)
            .GetCustomAttribute<HarmonyPatch>()!
            .info;
        var lookup = AccessTools.DeclaredMethod(
            target.declaringType,
            target.methodName,
            Type.EmptyTypes
        );
        Assert.Equal(typeof(int), lookup.ReturnType);

        var controller = Assembly.Load("TheBazaarRuntime").GetType("OptionsDialogController", true);
        var awake = ReadInstructions(AccessTools.DeclaredMethod(controller, "Awake"));
        Assert.Single(awake, code => code.Calls(lookup));
    }

    private static List<CodeInstruction> ReadInstructions(MethodInfo method)
    {
        var generator = new DynamicMethod(
            "ReadDisplaySettings",
            typeof(void),
            Type.EmptyTypes
        ).GetILGenerator();
        return PatchProcessor.GetOriginalInstructions(method, generator);
    }
}
