#nullable enable
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
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

/// <summary>
/// The Random Cosmetic Pool on the desktop: reads the game's collection, keeps the saved
/// exclusions in PlayerPrefs, and replaces the game's random pick at run start. Pool Mode is
/// the game's own per-hero Randomize preference, never the toggle's on-screen state.
/// </summary>
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

    // ---- loadout list ----

    /// <summary>
    /// Opening a category sets the native Randomize toggle off. In Pool Mode the toggle is
    /// moved off silently first, so that assignment changes nothing; true when it was moved.
    /// </summary>
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

    /// <summary>
    /// A click on a cosmetic of the loadout list in Pool Mode leaves it out of the pool or
    /// brings it back instead of equipping it; true when the click was taken. The Profile
    /// skin list reuses the list type and keeps the native click.
    /// </summary>
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
        // The click marked only the clicked item selected; every item shows its pool state again.
        RefreshList(list);
        return true;
    }

    /// <summary>
    /// Gives every cosmetic of a loadout list its pool state and selected look: in Pool Mode
    /// a member is selected, outside it the equipped cosmetic is, as natively.
    /// </summary>
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

    /// <summary>In Pool Mode an item shows selected exactly when it is in the pool.</summary>
    internal void ResolveSelected(CosmeticItem item, ref bool selected)
    {
        var state = item.GetComponent<RandomCosmeticPoolItem>();
        if (state != null && state.PoolMode)
            selected = state.IsMember;
    }

    /// <summary>
    /// Hovering one cosmetic natively drops every other item's selected look until the pointer
    /// leaves. In Pool Mode the pool's members keep theirs; true when the native hover is skipped.
    /// </summary>
    internal bool KeepPoolLook(CosmeticButtonBase button, CosmeticButtonBase hovered) =>
        button is CosmeticItem
        && button != hovered
        && button.GetComponent<RandomCosmeticPoolItem>() is { PoolMode: true, IsMember: true };

    private static bool IsLoadoutList(Component component) =>
        component.GetComponentInParent<LoadoutPopupController>(includeInactive: true) != null;

    // ---- pool ----

    internal RandomCosmeticPool Resolve(
        CollectionManager collection,
        BazaarInventoryTypes.ECollectionType category,
        EHero hero
    ) =>
        RandomCosmeticPool.Create(
            collection
                .GetCollectionDisplayItems(category, includeDefault: false, hero)
                .Where(item => !item.IsDefault)
                .Select(item => item.CollectionItemID),
            Load(category, hero)
        );

    /// <summary>Leaves out or brings back one cosmetic; false when the click changes nothing.</summary>
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

    /// <summary>Replaces the game's pick in every category whose pool leaves something out.</summary>
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
