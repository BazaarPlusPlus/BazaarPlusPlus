#nullable enable
using System;
using System.Linq;
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using TheBazaar;
using TheBazaar.AppFramework;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;

internal static class RandomHeroSkinPoolRuntime
{
    private static readonly BazaarInventoryTypes.ECollectionType[] SupportedCollectionTypes =
    [
        BazaarInventoryTypes.ECollectionType.HeroSkins,
        BazaarInventoryTypes.ECollectionType.Toys,
        BazaarInventoryTypes.ECollectionType.Boards,
        BazaarInventoryTypes.ECollectionType.Carpets,
        BazaarInventoryTypes.ECollectionType.CardBacks,
        BazaarInventoryTypes.ECollectionType.Album,
        BazaarInventoryTypes.ECollectionType.Stash,
        BazaarInventoryTypes.ECollectionType.Bank,
    ];

    public static bool IsSupported(BazaarInventoryTypes.ECollectionType collectionType)
    {
        return SupportedCollectionTypes.Contains(collectionType);
    }

    public static BazaarSaleItem[] GetAvailableCollectibles(
        EHero hero,
        BazaarInventoryTypes.ECollectionType collectionType,
        CollectionManager collectionManager
    )
    {
        if (collectionManager == null)
            throw new ArgumentNullException(nameof(collectionManager));

        if (!IsSupported(collectionType))
            return Array.Empty<BazaarSaleItem>();

        return collectionType == BazaarInventoryTypes.ECollectionType.HeroSkins
            ? collectionManager.GetPlayerHeroSkins(hero, includeDefault: true)
                ?? Array.Empty<BazaarSaleItem>()
            : collectionManager.GetPlayerCollectables(collectionType, includeDefault: true)
                ?? Array.Empty<BazaarSaleItem>();
    }

    public static RandomHeroSkinPoolState ResolveState(
        EHero hero,
        BazaarInventoryTypes.ECollectionType collectionType,
        BazaarSaleItem[] availableItems
    )
    {
        if (availableItems == null)
            throw new ArgumentNullException(nameof(availableItems));

        var state = RandomHeroSkinPoolStateFactory.Create(
            availableItems.Select(item => item.CollectionItemID),
            RandomHeroSkinPoolPlayerPrefs.LoadSelectedIds(hero, collectionType)
        );
        RandomHeroSkinPoolPlayerPrefs.SaveSelectedIds(hero, collectionType, state.SelectedSkinIds);
        return state;
    }

    public static void ApplyToRandomizedLoadout(
        EHero hero,
        CollectionManager collectionManager,
        BazaarGameShared.TempoNet.Models.EquipLoadoutRequest request
    )
    {
        if (collectionManager == null)
            throw new ArgumentNullException(nameof(collectionManager));
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        foreach (var collectionType in SupportedCollectionTypes)
        {
            var availableItems = GetAvailableCollectibles(hero, collectionType, collectionManager);
            if (availableItems.Length == 0)
                continue;

            var state = ResolveState(hero, collectionType, availableItems);
            var selectedItems = availableItems
                .Where(item => state.IsSelected(item.CollectionItemID))
                .ToArray();
            if (selectedItems.Length == 0)
                continue;

            var randomItem = selectedItems[UnityEngine.Random.Range(0, selectedItems.Length)];
            var selectedId = randomItem.IsDefault ? null : randomItem.CollectionItemID;
            ApplySelection(request, collectionType, selectedId);
        }
    }

    public static void EnsureSelected(
        EHero hero,
        BazaarInventoryTypes.ECollectionType collectionType,
        string? collectionItemId,
        CollectionManager collectionManager
    )
    {
        if (collectionManager == null)
            throw new ArgumentNullException(nameof(collectionManager));
        if (!IsSupported(collectionType) || string.IsNullOrWhiteSpace(collectionItemId))
            return;

        var availableItems = GetAvailableCollectibles(hero, collectionType, collectionManager);
        if (availableItems.Length == 0)
            return;

        var state = ResolveState(hero, collectionType, availableItems);
        if (state.IsSelected(collectionItemId))
            return;

        var nextState = state.SetSelected(collectionItemId, isSelected: true);
        RandomHeroSkinPoolPlayerPrefs.SaveSelectedIds(
            hero,
            collectionType,
            nextState.SelectedSkinIds
        );
    }

    private static void ApplySelection(
        BazaarGameShared.TempoNet.Models.EquipLoadoutRequest request,
        BazaarInventoryTypes.ECollectionType collectionType,
        string? selectedId
    )
    {
        switch (collectionType)
        {
            case BazaarInventoryTypes.ECollectionType.HeroSkins:
                request.heroSkinId = selectedId;
                break;
            case BazaarInventoryTypes.ECollectionType.Toys:
                request.toyId = selectedId;
                break;
            case BazaarInventoryTypes.ECollectionType.Boards:
                request.boardId = selectedId;
                break;
            case BazaarInventoryTypes.ECollectionType.Carpets:
                request.carpetId = selectedId;
                break;
            case BazaarInventoryTypes.ECollectionType.CardBacks:
                request.cardBackId = selectedId;
                break;
            case BazaarInventoryTypes.ECollectionType.Album:
                request.albumId = selectedId;
                break;
            case BazaarInventoryTypes.ECollectionType.Stash:
                request.stashId = selectedId;
                break;
            case BazaarInventoryTypes.ECollectionType.Bank:
                request.bankId = selectedId;
                break;
        }
    }
}
