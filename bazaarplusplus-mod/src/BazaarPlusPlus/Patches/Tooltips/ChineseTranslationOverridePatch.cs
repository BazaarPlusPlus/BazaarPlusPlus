#nullable enable
using BazaarPlusPlus.Game.ChineseTranslations;
using BazaarPlusPlus.Localization;
using HarmonyLib;
using TheBazaar.Localization;

namespace BazaarPlusPlus.Patches.Tooltips;

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
        if (
            ChineseTranslationOverrides.TryResolve(
                BppPatchHost.Services.Config.EnableChineseTranslationImprovementsConfig.Value,
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
