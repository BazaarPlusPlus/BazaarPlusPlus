#nullable enable
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.Game.RandomCosmetics;

internal enum RandomCosmeticPoolOperation
{
    ApplyLoadout,
    KeepRandomize,
    EditPool,
    RefreshList,
    ShowPool,
    LoadPool,
}

// The game's per-hero Randomize preference is authoritative because the category UI can reset the toggle.
internal sealed class RandomCosmeticPoolService
{
    // The categories CollectionManager.GetRandomizedLoadout draws. It never sets a toy, and
    // the loadout has no toy category.
    private static readonly BazaarInventoryTypes.ECollectionType[] PoolCategories =
    [
        BazaarInventoryTypes.ECollectionType.HeroSkins,
        BazaarInventoryTypes.ECollectionType.Boards,
        BazaarInventoryTypes.ECollectionType.Carpets,
        BazaarInventoryTypes.ECollectionType.CardBacks,
        BazaarInventoryTypes.ECollectionType.Album,
        BazaarInventoryTypes.ECollectionType.Stash,
        BazaarInventoryTypes.ECollectionType.Bank,
    ];

    private readonly Dictionary<string, IReadOnlyList<string>?> _saved = new(
        StringComparer.Ordinal
    );
    private readonly System.Random _random = new();

    private static bool IsPoolCategory(BazaarInventoryTypes.ECollectionType category) =>
        Array.IndexOf(PoolCategories, category) >= 0;

    // Opening a category resets the native toggle, so move it off silently before that assignment can change the preference.
    internal bool HoldRandomize(CosmeticsPanelController panel)
    {
        var toggle = panel._randomizeToggle;
        if (toggle == null || !toggle.isOn)
            return false;
        toggle.SetIsOnWithoutNotify(false);
        return true;
    }

    internal void ReleaseRandomize(CosmeticsPanelController panel) =>
        panel._randomizeToggle?.SetIsOnWithoutNotify(true);

    internal void RefreshPanel(CosmeticsPanelController panel) =>
        RefreshList(panel._cosmeticsListManager);

    // The Profile skin list reuses the loadout list type but must keep its native click behavior.
    internal bool EditPool(CosmeticsListManager list, EquipableItem item)
    {
        var category = item.itemData.CollectionType;
        var collection = Services.Get<CollectionManager>();
        if (
            collection == null
            || !IsPoolCategory(category)
            || !IsLoadoutList(list)
            || !collection.GetRandomizeLoadout(item.hero)
        )
            return false;

        if (!item.itemData.IsDefault)
            Toggle(collection, category, item.hero, item.itemData.CollectionItemID);
        RefreshList(list);
        return true;
    }

    internal void RefreshList(CosmeticsListManager? list)
    {
        var collection = Services.Get<CollectionManager>();
        if (
            list == null
            || collection == null
            || list._cosmeticsContent == null
            || !IsLoadoutList(list)
        )
            return;

        var pools =
            new Dictionary<(BazaarInventoryTypes.ECollectionType, EHero), RandomCosmeticPool>();
        foreach (var item in list._cosmeticsContent.GetComponentsInChildren<CosmeticItem>(true))
        {
            var cosmetic = item._equipableItem;
            var category = cosmetic.itemData.CollectionType;
            if (!IsPoolCategory(category))
                continue;

            var key = (category, cosmetic.hero);
            if (!pools.TryGetValue(key, out var pool))
                pools[key] = pool = Resolve(collection, category, cosmetic.hero);
            var poolMode = collection.GetRandomizeLoadout(cosmetic.hero);
            var state =
                item.GetComponent<RandomCosmeticPoolItem>()
                ?? item.gameObject.AddComponent<RandomCosmeticPoolItem>();
            state.Set(
                poolMode,
                !cosmetic.itemData.IsDefault && pool.Contains(cosmetic.itemData.CollectionItemID)
            );
            item.SetEquipState(poolMode ? state.IsMember : item == list._lastEquippedItem);
        }
    }

    internal void ResolveSelected(CosmeticItem item, ref bool selected)
    {
        var state = item.GetComponent<RandomCosmeticPoolItem>();
        if (state != null && state.PoolMode)
            selected = state.IsMember;
    }

    // Native hover drops every other item's selected look until pointer exit, which hides pool membership.
    internal bool KeepPoolLook(CosmeticButtonBase button, CosmeticButtonBase hovered) =>
        button is CosmeticItem
        && button != hovered
        && button.GetComponent<RandomCosmeticPoolItem>() is { PoolMode: true, IsMember: true };

    private static bool IsLoadoutList(Component component) =>
        component.GetComponentInParent<LoadoutPopupController>(includeInactive: true) != null;

    internal RandomCosmeticPool Resolve(
        CollectionManager collection,
        BazaarInventoryTypes.ECollectionType category,
        EHero hero
    )
    {
        var displayItems =
            DisplayItems
            ?? throw new MissingMethodException(nameof(CollectionManager), DisplayItemsMethod);
        return RandomCosmeticPool.Create(
            displayItems(collection, category, false, hero)
                .Where(item => !item.IsDefault)
                .Select(item => item.CollectionItemID),
            Load(category, hero)
        );
    }

    internal bool CanReadCollection => DisplayItems != null;

    // Some game builds ship a bool-only GetCollectionDisplayItems signature that cannot filter by category or hero.
    private const string DisplayItemsMethod = "GetCollectionDisplayItems";

    private static readonly Func<
        CollectionManager,
        BazaarInventoryTypes.ECollectionType,
        bool,
        EHero,
        BazaarSaleItem[]
    >? DisplayItems = BindDisplayItems();

    private static Func<
        CollectionManager,
        BazaarInventoryTypes.ECollectionType,
        bool,
        EHero,
        BazaarSaleItem[]
    >? BindDisplayItems()
    {
        var method = AccessTools.DeclaredMethod(
            typeof(CollectionManager),
            DisplayItemsMethod,
            [typeof(BazaarInventoryTypes.ECollectionType), typeof(bool), typeof(EHero)]
        );
        return method?.ReturnType == typeof(BazaarSaleItem[])
            ? AccessTools.MethodDelegate<
                Func<
                    CollectionManager,
                    BazaarInventoryTypes.ECollectionType,
                    bool,
                    EHero,
                    BazaarSaleItem[]
                >
            >(method)
            : null;
    }

    internal bool Toggle(
        CollectionManager collection,
        BazaarInventoryTypes.ECollectionType category,
        EHero hero,
        string? cosmeticId
    )
    {
        var next = Resolve(collection, category, hero).Toggle(cosmeticId);
        if (next == null)
            return false;

        var key = Key(category, hero);
        if (next.Count == 0)
            PlayerPrefs.DeleteKey(key);
        else
            PlayerPrefs.SetString(key, RandomCosmeticPoolPrefs.Serialize(next));
        PlayerPrefs.Save();
        _saved[key] = next.Count == 0 ? null : next;
        return true;
    }

    internal void ApplyTo(CollectionManager collection, EHero hero, EquipLoadoutRequest request)
    {
        foreach (var category in PoolCategories)
        {
            try
            {
                var pick = Resolve(collection, category, hero).Pick(_random.Next);
                if (pick != null)
                    Assign(request, category, pick);
            }
            catch (Exception ex)
            {
                Report(RandomCosmeticPoolOperation.ApplyLoadout, ex);
            }
        }
    }

    internal void Report(RandomCosmeticPoolOperation operation, Exception exception) =>
        BppLog.WarnEvent(
            new BppLogEvent(
                BppLogFeatureScope.Lobby,
                "lobby.random_cosmetic_pool.degraded",
                storm: ["operation"]
            ),
            exception,
            ("operation", operation)
        );

    private IReadOnlyList<string>? Load(BazaarInventoryTypes.ECollectionType category, EHero hero)
    {
        var key = Key(category, hero);
        if (_saved.TryGetValue(key, out var cached))
            return cached;

        var raw = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key, string.Empty) : null;
        var saved = RandomCosmeticPoolPrefs.TryParse(raw);
        if (raw != null && saved == null)
            Report(
                RandomCosmeticPoolOperation.LoadPool,
                new FormatException("The saved Random Cosmetic Pool is unreadable.")
            );
        _saved[key] = saved;
        return saved;
    }

    private static string Key(BazaarInventoryTypes.ECollectionType category, EHero hero) =>
        RandomCosmeticPoolPrefs.BuildKey(category.ToString(), hero.ToString(), AccountScope());

    private static string? AccountScope()
    {
        var accountId = BppClientCacheBridge.TryGetProfileAccountId();
        return string.IsNullOrWhiteSpace(accountId)
            ? BppClientCacheBridge.TryGetProfileUsername()
            : accountId;
    }

    private static void Assign(
        EquipLoadoutRequest request,
        BazaarInventoryTypes.ECollectionType category,
        string cosmeticId
    )
    {
        switch (category)
        {
            case BazaarInventoryTypes.ECollectionType.HeroSkins:
                request.heroSkinId = cosmeticId;
                break;
            case BazaarInventoryTypes.ECollectionType.Boards:
                request.boardId = cosmeticId;
                break;
            case BazaarInventoryTypes.ECollectionType.Carpets:
                request.carpetId = cosmeticId;
                break;
            case BazaarInventoryTypes.ECollectionType.CardBacks:
                request.cardBackId = cosmeticId;
                break;
            case BazaarInventoryTypes.ECollectionType.Album:
                request.albumId = cosmeticId;
                break;
            case BazaarInventoryTypes.ECollectionType.Stash:
                request.stashId = cosmeticId;
                break;
            case BazaarInventoryTypes.ECollectionType.Bank:
                request.bankId = cosmeticId;
                break;
        }
    }
}
