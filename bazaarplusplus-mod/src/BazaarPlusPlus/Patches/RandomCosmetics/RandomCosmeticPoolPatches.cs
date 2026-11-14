#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.Game.RandomCosmetics;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches.RandomCosmetics;

// The Random Cosmetic Pool needs every patch below: without one, the loadout shows a half
// state (a mark the click cannot change, or a pool nobody can see). The bodies act only
// while RandomCosmeticPoolPatchCapabilities.Armed and reach game members only inside the
// service they call within their try, so a missing member degrades instead of throwing out
// of the native method.

// The game draws the run-start loadout here; the pool replaces the pick per category.
[HarmonyPatch(typeof(CollectionManager), nameof(CollectionManager.GetRandomizedLoadout))]
internal static class RandomCosmeticPoolLoadoutPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        CollectionManager __instance,
        EHero hero,
        EquipLoadoutRequest __result
    )
    {
        if (!RandomCosmeticPoolPatchCapabilities.Armed || __result == null)
            return;
        try
        {
            BppPatchHost.Features.RandomCosmeticPool.ApplyTo(__instance, hero, __result);
        }
        catch (Exception ex)
        {
            BppPatchHost.Features.RandomCosmeticPool.Report(
                RandomCosmeticPoolOperation.ApplyLoadout,
                ex
            );
        }
    }
}

// Opening a category keeps Pool Mode: see RandomCosmeticPoolService.HoldRandomize.
[HarmonyPatch(typeof(CosmeticsPanelController), "OnCosmeticPressed")]
internal static class RandomCosmeticPoolCategoryPatch
{
    [HarmonyPrefix]
    private static void Prefix(CosmeticsPanelController __instance, out bool __state)
    {
        __state = false;
        if (!RandomCosmeticPoolPatchCapabilities.Armed)
            return;
        try
        {
            __state = BppPatchHost.Features.RandomCosmeticPool.HoldRandomize(__instance);
        }
        catch (Exception ex)
        {
            BppPatchHost.Features.RandomCosmeticPool.Report(
                RandomCosmeticPoolOperation.KeepRandomize,
                ex
            );
        }
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(
        CosmeticsPanelController __instance,
        bool __state,
        Exception? __exception
    )
    {
        if (!__state)
            return __exception;
        try
        {
            BppPatchHost.Features.RandomCosmeticPool.ReleaseRandomize(__instance);
        }
        catch (Exception ex)
        {
            BppPatchHost.Features.RandomCosmeticPool.Report(
                RandomCosmeticPoolOperation.KeepRandomize,
                ex
            );
        }
        return __exception;
    }
}

// The native toggle closes the cosmetic list when Randomize turns on; the list stays open
// and its marks follow the toggle instead.
[HarmonyPatch(typeof(LoadoutPopupController), "OnRandomizeToggled")]
internal static class RandomCosmeticPoolKeepListPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !RandomCosmeticPoolPatchCapabilities.Armed;
}

[HarmonyPatch(typeof(CosmeticsPanelController), "OnRandomizeToggleChanged")]
internal static class RandomCosmeticPoolTogglePatch
{
    [HarmonyPostfix]
    private static void Postfix(CosmeticsPanelController __instance)
    {
        if (!RandomCosmeticPoolPatchCapabilities.Armed)
            return;
        try
        {
            BppPatchHost.Features.RandomCosmeticPool.RefreshPanel(__instance);
        }
        catch (Exception ex)
        {
            BppPatchHost.Features.RandomCosmeticPool.Report(
                RandomCosmeticPoolOperation.RefreshList,
                ex
            );
        }
    }
}

// Randomize dims the category buttons; in Pool Mode they open the pool, so they stay lit.
[HarmonyPatch(typeof(CosmeticsPanelController), "HandleButtonDimming")]
internal static class RandomCosmeticPoolDimmingPatch
{
    [HarmonyPrefix]
    private static void Prefix(ref bool isRandomized)
    {
        if (RandomCosmeticPoolPatchCapabilities.Armed)
            isRandomized = false;
    }
}

// In Pool Mode a click on a loadout cosmetic edits the pool instead of equipping it.
[HarmonyPatch(typeof(CosmeticsListManager), "EquipItem")]
internal static class RandomCosmeticPoolClickPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CosmeticsListManager __instance, EquipableItem item)
    {
        if (!RandomCosmeticPoolPatchCapabilities.Armed)
            return true;
        try
        {
            return !BppPatchHost.Features.RandomCosmeticPool.EditPool(__instance, item);
        }
        catch (Exception ex)
        {
            BppPatchHost.Features.RandomCosmeticPool.Report(
                RandomCosmeticPoolOperation.EditPool,
                ex
            );
            return true;
        }
    }
}

// A built or extended loadout list shows each cosmetic's pool state.
[HarmonyPatch(typeof(CosmeticsListManager), "FetchCosmetics")]
internal static class RandomCosmeticPoolListPatch
{
    [HarmonyPostfix]
    private static void Postfix(CosmeticsListManager __instance)
    {
        if (!RandomCosmeticPoolPatchCapabilities.Armed)
            return;
        try
        {
            BppPatchHost.Features.RandomCosmeticPool.RefreshList(__instance);
        }
        catch (Exception ex)
        {
            BppPatchHost.Features.RandomCosmeticPool.Report(
                RandomCosmeticPoolOperation.RefreshList,
                ex
            );
        }
    }
}

// In Pool Mode the native selected look (glowing frame, full brightness) marks the pool.
[HarmonyPatch(typeof(CosmeticItem), nameof(CosmeticItem.SetEquipState))]
internal static class RandomCosmeticPoolSelectedPatch
{
    [HarmonyPrefix]
    private static void Prefix(CosmeticItem __instance, ref bool state)
    {
        if (!RandomCosmeticPoolPatchCapabilities.Armed)
            return;
        try
        {
            BppPatchHost.Features.RandomCosmeticPool.ResolveSelected(__instance, ref state);
        }
        catch (Exception ex)
        {
            BppPatchHost.Features.RandomCosmeticPool.Report(
                RandomCosmeticPoolOperation.ShowPool,
                ex
            );
        }
    }
}

[HarmonyPatch(typeof(CosmeticButtonBase), "HandleButtonHovered")]
internal static class RandomCosmeticPoolHoverPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CosmeticButtonBase __instance, CosmeticButtonBase selectedButton)
    {
        if (!RandomCosmeticPoolPatchCapabilities.Armed)
            return true;
        try
        {
            return !BppPatchHost.Features.RandomCosmeticPool.KeepPoolLook(
                __instance,
                selectedButton
            );
        }
        catch (Exception ex)
        {
            BppPatchHost.Features.RandomCosmeticPool.Report(
                RandomCosmeticPoolOperation.ShowPool,
                ex
            );
            return true;
        }
    }
}
