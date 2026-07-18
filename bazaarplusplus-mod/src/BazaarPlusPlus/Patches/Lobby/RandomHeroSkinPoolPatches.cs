#pragma warning disable CS0436
#nullable enable
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches.Lobby;

[HarmonyPatch(typeof(CosmeticsListManager), "FetchCosmetics")]
internal static class RandomHeroSkinPoolFetchPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        CosmeticsListManager __instance,
        BazaarInventoryTypes.ECollectionType cosmeticType,
        EHero hero,
        out RandomHeroSkinPoolNativeController.FetchScope __state
    )
    {
        try
        {
            __state = RandomHeroSkinPoolNativeController.BeginFetch(__instance, cosmeticType, hero);
        }
        catch (Exception ex)
        {
            __state = new RandomHeroSkinPoolNativeController.FetchScope(
                null,
                null,
                LobbyLogWriter.CollectionKind(cosmeticType)
            );
            LobbyLogWriter.ReportCollectiblePoolDegraded(
                CollectiblePoolOperation.BeginFetch,
                __state.CollectionKind,
                ex
            );
        }
    }

    [HarmonyPostfix]
    private static void Postfix(RandomHeroSkinPoolNativeController.FetchScope __state)
    {
        try
        {
            RandomHeroSkinPoolNativeController.CompleteFetch(__state);
        }
        catch (Exception ex)
        {
            var collectionKind = RandomHeroSkinPoolPatchLogContext.CollectionKindOf(__state);
            LobbyLogWriter.ReportCollectiblePoolDegraded(
                CollectiblePoolOperation.ProjectFetch,
                collectionKind,
                ex
            );
        }
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(
        RandomHeroSkinPoolNativeController.FetchScope __state,
        Exception? __exception
    )
    {
        try
        {
            RandomHeroSkinPoolNativeController.RestoreFetchScope(__state);
        }
        catch (Exception ex)
        {
            var collectionKind = RandomHeroSkinPoolPatchLogContext.CollectionKindOf(__state);
            LobbyLogWriter.ReportCollectiblePoolDegraded(
                CollectiblePoolOperation.EndFetch,
                collectionKind,
                ex
            );
        }
        return __exception;
    }
}

[HarmonyPatch(typeof(CosmeticItem), nameof(CosmeticItem.SetData))]
internal static class RandomHeroSkinPoolSetDataPatch
{
    [HarmonyPostfix]
    private static void Postfix(CosmeticItem __instance, BazaarSaleItem data, EHero hero)
    {
        try
        {
            RandomHeroSkinPoolNativeController.RegisterActiveFetchItem(__instance, data, hero);
        }
        catch (Exception ex)
        {
            LobbyLogWriter.ReportCollectiblePoolDegraded(
                CollectiblePoolOperation.RegisterCard,
                RandomHeroSkinPoolPatchLogContext.CollectionKindOf(data),
                ex
            );
        }
    }
}

internal static class RandomHeroSkinPoolPatchLogContext
{
    internal static CollectiblePoolKind CollectionKindOf(
        RandomHeroSkinPoolNativeController.FetchScope scope
    ) => scope.CollectionKind;

    internal static CollectiblePoolKind CollectionKindOf(BazaarSaleItem? data) =>
        data == null
            ? CollectiblePoolKind.Unknown
            : LobbyLogWriter.CollectionKind(data.Value.CollectionType);

    internal static CollectiblePoolKind CollectionKindOf(EquipableItem item) =>
        CollectionKindOf(item.itemData);

    internal static CollectiblePoolKind CollectionKindOf(CosmeticItem? item) =>
        item == null ? CollectiblePoolKind.Unknown : CollectionKindOf(item.EquipableItem);
}

[HarmonyPatch(typeof(CosmeticItem), "SetEquipState")]
internal static class RandomHeroSkinPoolSetEquipStatePatch
{
    [HarmonyPrefix]
    private static void Prefix(CosmeticItem __instance, ref bool state)
    {
        try
        {
            RandomHeroSkinPoolNativeController.OverrideEquipVisual(__instance, ref state);
        }
        catch (Exception ex)
        {
            LobbyLogWriter.ReportCollectiblePoolDegraded(
                CollectiblePoolOperation.ProjectVisual,
                RandomHeroSkinPoolPatchLogContext.CollectionKindOf(__instance),
                ex
            );
        }
    }
}

[HarmonyPatch(typeof(CosmeticsListManager), "EquipItem")]
internal static class RandomHeroSkinPoolEquipItemPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CosmeticsListManager __instance, object[] __args, out bool __state)
    {
        __state = false;
        try
        {
            if (__args.Length == 0 || __args[0] is not EquipableItem item)
                return true;

            var route = RandomHeroSkinPoolNativeController.RouteClick(__instance, item);
            if (!NativePoolInteractionRouting.ShouldRunNativeAction(route))
                return false;

            __state = RandomHeroSkinPoolRuntime.IsSupported(item.itemData.CollectionType);
            return true;
        }
        catch (Exception ex)
        {
            var kind =
                __args.Length > 0 && __args[0] is EquipableItem failedItem
                    ? RandomHeroSkinPoolPatchLogContext.CollectionKindOf(failedItem)
                    : CollectiblePoolKind.Unknown;
            LobbyLogWriter.ReportCollectiblePoolDegraded(
                CollectiblePoolOperation.RouteClick,
                kind,
                ex
            );
            return true;
        }
    }

    [HarmonyPostfix]
    private static void Postfix(object[] __args, bool __state)
    {
        if (!__state || __args.Length == 0 || __args[0] is not EquipableItem item)
            return;

        try
        {
            var collectionManager = TheBazaar.AppFramework.Services.Get<CollectionManager>();
            if (collectionManager == null)
                return;

            RandomHeroSkinPoolRuntime.EnsureSelected(
                item.hero,
                item.itemData.CollectionType,
                item.itemData.CollectionItemID,
                collectionManager
            );
        }
        catch (Exception ex)
        {
            LobbyLogWriter.ReportCollectiblePoolDegraded(
                CollectiblePoolOperation.PreserveEquipped,
                RandomHeroSkinPoolPatchLogContext.CollectionKindOf(item),
                ex
            );
        }
    }
}

[HarmonyPatch(
    typeof(CollectionManager),
    nameof(CollectionManager.SetRandomizeLoadout),
    [typeof(EHero), typeof(bool)]
)]
internal static class RandomHeroSkinPoolTogglePatch
{
    [HarmonyPostfix]
    private static void Postfix(EHero hero)
    {
        try
        {
            RandomHeroSkinPoolNativeController.NotifyRandomizeChanged(hero);
        }
        catch (Exception ex)
        {
            LobbyLogWriter.ReportCollectiblePoolDegraded(
                CollectiblePoolOperation.RestoreVisuals,
                CollectiblePoolKind.All,
                ex
            );
        }
    }
}

[HarmonyPatch(typeof(CollectionManager), "GetRandomizedLoadout")]
internal static class RandomHeroSkinPoolGetRandomizedLoadoutPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        CollectionManager __instance,
        EHero hero,
        ref EquipLoadoutRequest __result
    )
    {
        try
        {
            __result ??= new EquipLoadoutRequest();
            RandomHeroSkinPoolRuntime.ApplyToRandomizedLoadout(hero, __instance, __result);
        }
        catch (Exception ex)
        {
            LobbyLogWriter.ReportCollectiblePoolDegraded(
                CollectiblePoolOperation.ApplyRandomizedLoadout,
                CollectiblePoolKind.All,
                ex
            );
        }
    }
}
