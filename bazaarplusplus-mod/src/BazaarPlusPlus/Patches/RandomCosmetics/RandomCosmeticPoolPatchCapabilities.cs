#nullable enable
using System.Reflection;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches.RandomCosmetics;

/// <summary>
/// Arms the Random Cosmetic Pool only when every one of its patches is installed and every
/// game field it reads exists. Patches apply per class, so a game update can drop one of them;
/// then the whole pool stays off, the run-start pick included, and the loadout is native.
/// </summary>
internal static class RandomCosmeticPoolPatchCapabilities
{
    private static volatile bool _armed;

    internal static bool Armed => _armed;

    internal static void Capture()
    {
        bool armed;
        try
        {
            armed = Probe();
        }
        catch (Exception)
        {
            armed = false;
        }

        _armed = armed;
        if (!armed)
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Lobby,
                    "lobby.random_cosmetic_pool.unavailable",
                    storm: []
                )
            );
    }

    internal static void Clear() => _armed = false;

    private static bool Probe() =>
        Installed(
            typeof(CollectionManager),
            nameof(CollectionManager.GetRandomizedLoadout),
            typeof(RandomCosmeticPoolLoadoutPatch),
            info => info.Postfixes
        )
        && Installed(
            typeof(CosmeticsPanelController),
            "OnCosmeticPressed",
            typeof(RandomCosmeticPoolCategoryPatch),
            info => info.Prefixes
        )
        && Installed(
            typeof(CosmeticsPanelController),
            "OnCosmeticPressed",
            typeof(RandomCosmeticPoolCategoryPatch),
            info => info.Finalizers
        )
        && Installed(
            typeof(LoadoutPopupController),
            "OnRandomizeToggled",
            typeof(RandomCosmeticPoolKeepListPatch),
            info => info.Prefixes
        )
        && Installed(
            typeof(CosmeticsPanelController),
            "OnRandomizeToggleChanged",
            typeof(RandomCosmeticPoolTogglePatch),
            info => info.Postfixes
        )
        && Installed(
            typeof(CosmeticsPanelController),
            "HandleButtonDimming",
            typeof(RandomCosmeticPoolDimmingPatch),
            info => info.Prefixes
        )
        && Installed(
            typeof(CosmeticsListManager),
            "EquipItem",
            typeof(RandomCosmeticPoolClickPatch),
            info => info.Prefixes
        )
        && Installed(
            typeof(CosmeticsListManager),
            "FetchCosmetics",
            typeof(RandomCosmeticPoolListPatch),
            info => info.Postfixes
        )
        && Installed(
            typeof(CosmeticItem),
            nameof(CosmeticItem.SetEquipState),
            typeof(RandomCosmeticPoolSelectedPatch),
            info => info.Prefixes
        )
        && Installed(
            typeof(CosmeticButtonBase),
            "HandleButtonHovered",
            typeof(RandomCosmeticPoolHoverPatch),
            info => info.Prefixes
        )
        && HasField(typeof(CosmeticsPanelController), "_randomizeToggle")
        && HasField(typeof(CosmeticsPanelController), "_cosmeticsListManager")
        && HasField(typeof(CosmeticsListManager), "_cosmeticsContent")
        && HasField(typeof(CosmeticsListManager), "_lastEquippedItem")
        && HasField(typeof(CosmeticItem), "_equipableItem");

    private static bool Installed(
        Type type,
        string method,
        Type patchClass,
        Func<HarmonyLib.Patches, IEnumerable<Patch>> kind
    )
    {
        var original = AccessTools.DeclaredMethod(type, method);
        var info = original == null ? null : Harmony.GetPatchInfo(original);
        return info != null
            && kind(info)
                .Any(patch =>
                    patch.owner == BppPluginMetadata.Guid
                    && patch.PatchMethod?.DeclaringType == patchClass
                );
    }

    private static bool HasField(Type type, string name) =>
        AccessTools.DeclaredField(type, name) is FieldInfo;
}
