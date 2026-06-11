#nullable enable
#pragma warning disable CS0436
using System;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using UnityEngine;

namespace BazaarPlusPlus.Patches.CardArtReplacement;

[HarmonyPatch(typeof(global::RewardController), "Setup", new[] { typeof(string), typeof(Card) })]
internal static class RewardControllerArtReplacePatch
{
    private const string LogCategory = "CardArtReplacement";

    private static readonly int MainTexPropertyId = Shader.PropertyToID("_MainTex");
    private static readonly FieldInfo? InstancedMaterialField = AccessTools.Field(
        typeof(global::RewardController),
        "instancedMaterial"
    );

    [HarmonyPostfix]
    private static void Postfix(global::RewardController __instance, Card card, ref Task __result)
    {
        if (__result == null)
            return;

        __result = ApplyAfterSetup(__instance, card, __result);
    }

    private static async Task ApplyAfterSetup(
        global::RewardController instance,
        Card card,
        Task setupTask
    )
    {
        await setupTask;

        try
        {
            if (instance == null)
                return;

            if (!PackageCardArtPatchGate.TryGetReplacementTexture(card, out var texture))
                return;

            if (InstancedMaterialField?.GetValue(instance) is not Material material)
                return;

            // Native reward setup samples _MainTex on this material; write the same
            // property instead of the _BaseMap/mainTexture heuristics in CardArtInjector.
            material.SetTexture(MainTexPropertyId, texture);
        }
        catch (Exception ex)
        {
            BppLog.Warn(LogCategory, $"Reward postfix failed: {ex.Message}");
        }
    }
}
