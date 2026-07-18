#nullable enable
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;

internal sealed class RandomHeroSkinPoolNativeController : MonoBehaviour
{
    private static readonly List<WeakReference<RandomHeroSkinPoolNativeController>> Controllers =
        new();
    private static RandomHeroSkinPoolNativeController? _activeFetchController;

    private readonly Dictionary<string, CosmeticItem> _itemsById = new(StringComparer.Ordinal);
    private CollectionManager? _collectionManager;
    private BazaarInventoryTypes.ECollectionType _collectionType = BazaarInventoryTypes
        .ECollectionType
        .Invalid;
    private EHero _hero = EHero.Common;
    private NativePoolInteractionCoordinator<RandomHeroSkinPoolState>? _coordinator;
    private bool _registered;

    internal readonly struct FetchScope
    {
        public FetchScope(
            RandomHeroSkinPoolNativeController? controller,
            RandomHeroSkinPoolNativeController? previousController,
            CollectiblePoolKind collectionKind
        )
        {
            Controller = controller;
            PreviousController = previousController;
            CollectionKind = collectionKind;
        }

        public RandomHeroSkinPoolNativeController? Controller { get; }

        public RandomHeroSkinPoolNativeController? PreviousController { get; }

        public CollectiblePoolKind CollectionKind { get; }
    }

    internal static FetchScope BeginFetch(
        CosmeticsListManager view,
        BazaarInventoryTypes.ECollectionType collectionType,
        EHero hero
    )
    {
        var controller = view.GetComponent<RandomHeroSkinPoolNativeController>();
        if (controller == null)
            controller = view.gameObject.AddComponent<RandomHeroSkinPoolNativeController>();

        controller.Register();
        controller.BeginSession(collectionType, hero);
        var scope = new FetchScope(
            controller,
            _activeFetchController,
            LobbyLogWriter.CollectionKind(collectionType)
        );
        _activeFetchController = controller;
        return scope;
    }

    internal static void CompleteFetch(FetchScope scope)
    {
        scope.Controller?.ApplyVisuals();
    }

    internal static void RestoreFetchScope(FetchScope scope)
    {
        if (scope.Controller != null && ReferenceEquals(_activeFetchController, scope.Controller))
            _activeFetchController = scope.PreviousController;
    }

    internal static void RegisterActiveFetchItem(CosmeticItem item, BazaarSaleItem data, EHero hero)
    {
        _activeFetchController?.RegisterItem(item, data, hero);
    }

    internal static NativePoolInteractionRoute RouteClick(
        CosmeticsListManager view,
        EquipableItem item
    )
    {
        var controller = view.GetComponent<RandomHeroSkinPoolNativeController>();
        if (controller == null || controller._coordinator == null)
            return NativePoolInteractionRoute.NativeAction;

        var collectionType = item.itemData.CollectionType;
        var collectionManager = controller._collectionManager;
        if (
            collectionManager == null
            || controller._hero != item.hero
            || controller._collectionType != collectionType
        )
        {
            return NativePoolInteractionRoute.NativeAction;
        }

        var route = controller._coordinator.HandleClick(
            collectionManager.GetRandomizeLoadout(item.hero),
            RandomHeroSkinPoolRuntime.IsSupported(collectionType),
            NativePoolInteractionOrigin.User,
            item.itemData.CollectionItemID
        );
        if (route == NativePoolInteractionRoute.PoolEdit)
            controller.ApplyVisuals();
        return route;
    }

    internal static void OverrideEquipVisual(CosmeticItem item, ref bool state)
    {
        var controller = FindController(item);
        if (
            controller?._coordinator == null
            || controller._collectionManager == null
            || !controller._collectionManager.GetRandomizeLoadout(controller._hero)
        )
        {
            return;
        }

        var equipableItem = item.EquipableItem;
        if (
            equipableItem.hero != controller._hero
            || equipableItem.itemData.CollectionType != controller._collectionType
        )
        {
            return;
        }

        state = controller._coordinator.IsVisuallySelected(
            poolModeEnabled: true,
            equipableItem.itemData.CollectionItemID,
            nativeSelected: state
        );
    }

    internal static void NotifyRandomizeChanged(EHero hero)
    {
        for (var index = Controllers.Count - 1; index >= 0; index--)
        {
            if (
                !Controllers[index].TryGetTarget(out var controller)
                || controller == null
                || !controller._registered
            )
            {
                Controllers.RemoveAt(index);
                continue;
            }

            if (controller._hero == hero)
                controller.ApplyVisuals();
        }
    }

    private void OnDestroy()
    {
        _registered = false;
        _itemsById.Clear();
        CompactControllers();
    }

    private void Register()
    {
        if (_registered)
            return;

        Controllers.Add(new WeakReference<RandomHeroSkinPoolNativeController>(this));
        _registered = true;
        CompactControllers();
    }

    private void BeginSession(BazaarInventoryTypes.ECollectionType collectionType, EHero hero)
    {
        _collectionType = collectionType;
        _hero = hero;
        _itemsById.Clear();
        _collectionManager = Services.Get<CollectionManager>();
        if (_collectionManager == null || !RandomHeroSkinPoolRuntime.IsSupported(collectionType))
        {
            _coordinator = null;
            return;
        }

        var availableItems = RandomHeroSkinPoolRuntime.GetAvailableCollectibles(
            hero,
            collectionType,
            _collectionManager
        );
        if (availableItems.Length == 0)
        {
            _coordinator = null;
            return;
        }

        var state = RandomHeroSkinPoolRuntime.ResolveState(hero, collectionType, availableItems);
        _coordinator = new NativePoolInteractionCoordinator<RandomHeroSkinPoolState>(
            state,
            (current, id) => current.IsSelected(id),
            (current, id, isSelected) => current.SetSelected(id, isSelected),
            current =>
                RandomHeroSkinPoolPlayerPrefs.SaveSelectedIds(
                    hero,
                    collectionType,
                    current.SelectedSkinIds
                )
        );
    }

    private void RegisterItem(CosmeticItem item, BazaarSaleItem data, EHero hero)
    {
        if (
            item == null
            || hero != _hero
            || data.CollectionType != _collectionType
            || string.IsNullOrWhiteSpace(data.CollectionItemID)
        )
        {
            return;
        }

        _itemsById[data.CollectionItemID] = item;
    }

    private void ApplyVisuals()
    {
        if (_collectionManager == null)
            return;

        var poolModeEnabled = _collectionManager.GetRandomizeLoadout(_hero);
        foreach (var pair in _itemsById)
        {
            var item = pair.Value;
            if (item == null)
                continue;

            var equipableItem = item.EquipableItem;
            if (
                equipableItem.hero != _hero
                || equipableItem.itemData.CollectionType != _collectionType
                || !string.Equals(
                    equipableItem.itemData.CollectionItemID,
                    pair.Key,
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            var nativeSelected = _collectionManager.IsCollectibleOfIDEquipped(
                equipableItem.itemData,
                _hero
            );
            var selected =
                _coordinator?.IsVisuallySelected(poolModeEnabled, pair.Key, nativeSelected)
                ?? nativeSelected;
            item.SetEquipState(selected);
        }
    }

    private bool Owns(CosmeticItem item)
    {
        if (ReferenceEquals(_activeFetchController, this))
        {
            var equipableItem = item.EquipableItem;
            if (
                equipableItem.hero == _hero
                && equipableItem.itemData.CollectionType == _collectionType
            )
            {
                return true;
            }
        }

        foreach (var registered in _itemsById.Values)
        {
            if (ReferenceEquals(registered, item))
                return true;
        }

        return false;
    }

    private static RandomHeroSkinPoolNativeController? FindController(CosmeticItem item)
    {
        if (_activeFetchController != null && _activeFetchController.Owns(item))
            return _activeFetchController;

        for (var index = Controllers.Count - 1; index >= 0; index--)
        {
            if (
                !Controllers[index].TryGetTarget(out var controller)
                || controller == null
                || !controller._registered
            )
            {
                Controllers.RemoveAt(index);
                continue;
            }

            if (controller.Owns(item))
                return controller;
        }

        return null;
    }

    private static void CompactControllers()
    {
        for (var index = Controllers.Count - 1; index >= 0; index--)
        {
            if (
                !Controllers[index].TryGetTarget(out var controller)
                || controller == null
                || !controller._registered
            )
            {
                Controllers.RemoveAt(index);
            }
        }
    }
}
