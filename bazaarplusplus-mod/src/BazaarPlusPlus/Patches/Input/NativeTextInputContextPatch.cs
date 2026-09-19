using BazaarPlusPlus.GameInterop.Input;
using HarmonyLib;
using TheBazaar.Inputs;

namespace BazaarPlusPlus.Patches.Input;

[HarmonyPatch(typeof(InputContextStack), "Apply")]
internal static class NativeTextInputContextPatch
{
    [HarmonyPostfix]
    private static void Postfix(InputContextStack __instance) =>
        NativeTextInputLease.Reapply(__instance);
}
