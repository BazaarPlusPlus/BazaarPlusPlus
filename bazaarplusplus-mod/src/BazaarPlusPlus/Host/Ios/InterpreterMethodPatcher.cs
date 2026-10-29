#nullable enable
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using HarmonyLib.Public.Patching;
using MonoMod.Cil;
using MonoMod.Utils;

namespace BazaarPlusPlus.Host.Ios;

/// <summary>
/// Harmony backend for the iOS port, whose Mono runs game code in the interpreter and cannot
/// write native code. Harmony's own manipulator builds the patched method; the interpreter then
/// redirects the original to it (<c>mono_interp_redirect_method</c>, Mono patch 0008 in
/// <c>bazaarplusplus-ios/runtime/mono/patches/</c>). Prefixes, postfixes, finalizers and
/// transpilers all run unchanged.
/// </summary>
internal sealed class InterpreterMethodPatcher : MethodPatcher
{
    // A collected DynamicMethod frees its MonoMethod, which the redirect still points at.
    private static readonly List<MethodInfo> Replacements = [];
    private static readonly FieldInfo DynamicMethodHandle = typeof(DynamicMethod).GetField(
        "mhandle",
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;
    private static readonly MethodInfo CreateDynamicMethod = typeof(DynamicMethod).GetMethod(
        "CreateDynMethod",
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;

    private InterpreterMethodPatcher(MethodBase original)
        : base(original) { }

    /// <summary>Replaces Harmony's patcher resolvers; call before the first patch.</summary>
    public static void Install()
    {
        if (DynamicMethodHandle == null || CreateDynamicMethod == null)
            throw new InvalidOperationException(
                "This Mono has no DynamicMethod handle to redirect to."
            );
        PatchManager.ClearAllPatcherResolvers();
        // Through reflection: the publicized Harmony reference exposes the event's backing field
        // under the same name, which makes the event ambiguous to the compiler.
        EventHandler<PatchManager.PatcherResolverEventArgs> resolve = (_, args) =>
        {
            if (args.Original.GetMethodBody() != null)
                args.MethodPatcher = new InterpreterMethodPatcher(args.Original);
        };
        typeof(PatchManager).GetEvent("ResolvePatcher")!.AddEventHandler(null, resolve);
    }

    public override DynamicMethodDefinition? PrepareOriginal() => null;

    public override MethodBase DetourTo(MethodBase replacement)
    {
        using var definition = new DynamicMethodDefinition(Original);
        using (var context = new ILContext(definition.Definition))
            HarmonyManipulator.Manipulate(Original, Original.GetPatchInfo(), context);
        var patched = definition.Generate();
        lock (Replacements)
            Replacements.Add(patched);
        mono_interp_redirect_method(Original.MethodHandle.Value, HandleOf(patched));
        return patched;
    }

    public override DynamicMethodDefinition CopyOriginal() => new(Original);

    private static IntPtr HandleOf(MethodInfo method)
    {
        if (method is not DynamicMethod dynamicMethod)
            return method.MethodHandle.Value;
        CreateDynamicMethod.Invoke(dynamicMethod, null);
        return ((RuntimeMethodHandle)DynamicMethodHandle.GetValue(dynamicMethod)).Value;
    }

    [DllImport("__Internal")]
    private static extern void mono_interp_redirect_method(IntPtr original, IntPtr target);
}
