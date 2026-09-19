#nullable enable
using BazaarPlusPlus.GameInterop.StaticCards;
using HarmonyLib;
using Newtonsoft.Json;
using TheBazaar.DataManagement.Json;

namespace BazaarPlusPlus.Patches.Cards;

[HarmonyPatch(typeof(JsonGameDataManager), "CardSerializer", MethodType.Getter)]
internal static class CardTemplateValueCompatibilityPatch
{
    [HarmonyPostfix]
    private static void Postfix(JsonSerializer __result)
    {
        // The getter owns one serializer per thread, including the native PLINQ card-map workers.
        // Install before either bulk or point lookup reads a template on that thread.
        CompatibleCardValueConverter.Install(__result);
    }
}
