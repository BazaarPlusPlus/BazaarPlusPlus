#nullable enable
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using TheBazaar.Utilities;
using UnityEngine;

namespace BazaarPlusPlus.Patches;

[HarmonyPatch(typeof(SkinAssetDataSO), nameof(SkinAssetDataSO.GenerateEncounterData))]
internal static class HeroPortraitGeneratedPatch
{
    [HarmonyPrefix]
    private static void Prefix(SkinAssetDataSO __instance, out NativeHeroPortraitSkin __state) =>
        __state = NativeHeroPortraitSkin.Capture(__instance);

    [HarmonyPostfix]
    private static void Postfix(
        NativeHeroPortraitSkin __state,
        ref Task<EncounterAssetDataSO> __result
    )
    {
        if (BppPatchHost.TryGetFeatures(out var features))
            __result = ObserveAsync(__result, __state, features!.HeroPortraits);
    }

    private static async Task<EncounterAssetDataSO> ObserveAsync(
        Task<EncounterAssetDataSO> task,
        NativeHeroPortraitSkin skin,
        GameInterop.HeroPortraits.NativeHeroPortraitCatalog catalog
    )
    {
        var result = await task;
        if (result != null)
            catalog.Record(skin, result.portraitTextureReference);
        return result!;
    }
}

[HarmonyPatch(typeof(SkinAssetDataSO), nameof(SkinAssetDataSO.LoadPortraitSpriteAsync))]
internal static class HeroPortraitSpriteLoadedPatch
{
    [HarmonyPrefix]
    private static void Prefix(SkinAssetDataSO __instance, out NativeHeroPortraitSkin __state) =>
        __state = NativeHeroPortraitSkin.Capture(__instance);

    [HarmonyPostfix]
    private static void Postfix(NativeHeroPortraitSkin __state, ref Task<Sprite> __result)
    {
        if (BppPatchHost.TryGetFeatures(out var features))
            __result = ObserveAsync(__result, __state, features!.HeroPortraits);
    }

    private static async Task<Sprite> ObserveAsync(
        Task<Sprite> task,
        NativeHeroPortraitSkin skin,
        GameInterop.HeroPortraits.NativeHeroPortraitCatalog catalog
    )
    {
        var result = await task;
        catalog.Record(skin, result);
        return result;
    }
}

[HarmonyPatch(typeof(EncounterController), nameof(EncounterController.SetupEncounterController))]
internal static class HeroPortraitControllerObservedPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        EncounterController __instance,
        EncounterAssetDataSO encounterData,
        out long __state
    )
    {
        __state = 0;
        if (!BppPatchHost.TryGetFeatures(out var features) || features!.PortraitAnimation == null)
            return;
        try
        {
            __state = features.PortraitAnimation.BeforeSetup(__instance, encounterData);
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Plugin,
                    "plugin.hero_portrait.cached_setup_failed",
                    storm: []
                ),
                ex,
                static () => []
            );
        }
    }

    [HarmonyPostfix]
    private static void Postfix(EncounterController __instance, long __state, ref Task __result)
    {
        if (BppPatchHost.TryGetFeatures(out var features))
        {
            features!.HeroPortraits.Observe(__instance);
            if (features.PortraitAnimation != null && __state != 0)
                __result = CompleteAsync(__result, features.PortraitAnimation, __instance, __state);
        }
    }

    private static async Task CompleteAsync(
        Task native,
        Game.HeroPortraits.AnimatedHeroPortraitController controller,
        EncounterController encounter,
        long generation
    )
    {
        try
        {
            await native;
        }
        finally
        {
            try
            {
                if (controller != null)
                    controller.SetupFinished(encounter, generation);
            }
            catch (Exception ex)
            {
                BppLog.DebugEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Plugin,
                        "plugin.hero_portrait.cached_cleanup_failed",
                        storm: []
                    ),
                    ex,
                    static () => []
                );
            }
        }
    }
}

[HarmonyPatch(typeof(PortraitApplyHelper), nameof(PortraitApplyHelper.ApplyToSpriteRenderer))]
internal static class HeroPortraitAppliedPatch
{
    [HarmonyPostfix]
    private static void Postfix(SpriteRenderer portraitRenderer)
    {
        if (BppPatchHost.TryGetFeatures(out var features) && features!.PortraitAnimation != null)
            features.PortraitAnimation.PortraitApplied(portraitRenderer);
    }
}
