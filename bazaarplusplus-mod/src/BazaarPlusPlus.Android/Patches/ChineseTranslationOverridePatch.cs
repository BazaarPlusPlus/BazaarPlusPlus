using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Game.ChineseTranslations;
using BazaarPlusPlus.Localization;
using HarmonyLib;
using Il2CppTheBazaar.Localization;

namespace BazaarPlusPlus.Android.Patches;

[HarmonyPatch(
    typeof(LocalizationService),
    nameof(LocalizationService.TryGetText),
    new[] { typeof(string), typeof(string), typeof(string) },
    new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out }
)]
internal static class ChineseTranslationOverridePatch
{
    [HarmonyPostfix]
    private static void Postfix(string __0, ref string __2, ref bool __result)
    {
        if (AndroidPatchHost.Mode != AndroidPatchMode.Enabled)
            return;
        if (
            ChineseTranslationOverrides.TryResolve(
                AndroidPatchHost
                    .Composition
                    ?.Settings
                    .EnableChineseTranslationImprovementsConfig
                    .Value == true,
                L.CurrentLanguageCode,
                __0,
                out var replacement
            )
        )
        {
            __2 = replacement;
            __result = true;
        }
    }
}
