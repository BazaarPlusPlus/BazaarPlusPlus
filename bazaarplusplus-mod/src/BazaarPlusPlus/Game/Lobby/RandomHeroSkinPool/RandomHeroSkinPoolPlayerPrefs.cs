#nullable enable
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Heroes;
using UnityEngine;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;

internal static class RandomHeroSkinPoolPlayerPrefs
{
    private const string SelectedPoolPrefsKeyPrefix = "BPP.RandomCollectiblePool.Selected";

    public static IReadOnlyCollection<string>? LoadSelectedIds(
        EHero hero,
        BazaarInventoryTypes.ECollectionType collectionType
    )
    {
        var readIds = TheDragonsHeroIdentity.PersistenceReadIds(hero);
        var accountScope = RandomPoolPrefsHelpers.ResolveAccountScopeForPrefs(
            RandomPoolKind.Collectible
        );
        var readKeys = readIds
            .Select(heroId => BuildScopedPrefsKey(heroId, collectionType, accountScope))
            .ToArray();
        return RandomHeroSkinPoolPreferenceMigration.LoadCanonicalFirst(
            readKeys,
            PlayerPrefs.HasKey,
            key => RandomPoolPrefsHelpers.LoadIdCollection(key, RandomPoolKind.Collectible),
            (key, selectedIds) => RandomPoolPrefsHelpers.SaveIdCollection(key, selectedIds),
            key =>
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        );
    }

    public static void SaveSelectedIds(
        EHero hero,
        BazaarInventoryTypes.ECollectionType collectionType,
        IEnumerable<string> ids
    )
    {
        var accountScope = RandomPoolPrefsHelpers.ResolveAccountScopeForPrefs(
            RandomPoolKind.Collectible
        );
        RandomPoolPrefsHelpers.SaveIdCollection(
            BuildScopedPrefsKey(
                TheDragonsHeroIdentity.ToCanonicalId(hero),
                collectionType,
                accountScope
            ),
            ids
        );
    }

    private static string BuildScopedPrefsKey(
        string heroId,
        BazaarInventoryTypes.ECollectionType collectionType,
        string accountScope
    )
    {
        return $"{SelectedPoolPrefsKeyPrefix}.{Uri.EscapeDataString(collectionType.ToString())}.{Uri.EscapeDataString(heroId)}.{accountScope}";
    }
}
