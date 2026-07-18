#nullable enable
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;

internal static class RandomHeroSkinPoolPlayerPrefs
{
    private const string SelectedPoolPrefsKeyPrefix = "BPP.RandomCollectiblePool.Selected";

    public static IReadOnlyCollection<string>? LoadSelectedIds(
        EHero hero,
        BazaarInventoryTypes.ECollectionType collectionType
    )
    {
        return RandomPoolPrefsHelpers.LoadIdCollection(
            BuildScopedPrefsKey(hero, collectionType),
            RandomPoolKind.Collectible
        );
    }

    public static void SaveSelectedIds(
        EHero hero,
        BazaarInventoryTypes.ECollectionType collectionType,
        IEnumerable<string> ids
    )
    {
        RandomPoolPrefsHelpers.SaveIdCollection(BuildScopedPrefsKey(hero, collectionType), ids);
    }

    private static string BuildScopedPrefsKey(
        EHero hero,
        BazaarInventoryTypes.ECollectionType collectionType
    )
    {
        var scope = RandomPoolPrefsHelpers.ResolveAccountScopeForPrefs(RandomPoolKind.Collectible);
        return $"{SelectedPoolPrefsKeyPrefix}.{Uri.EscapeDataString(collectionType.ToString())}.{Uri.EscapeDataString(hero.ToString())}.{scope}";
    }
}
