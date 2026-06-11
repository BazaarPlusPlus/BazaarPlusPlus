#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.Lobby;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;

internal static class RandomHeroSkinPoolPlayerPrefs
{
    private const string SelectedPoolPrefsKeyPrefix = "BPP.RandomCollectiblePool.Selected";
    private const string LogScope = "RandomHeroSkinPool";

    public static IReadOnlyCollection<string>? LoadSelectedIds(
        EHero hero,
        BazaarInventoryTypes.ECollectionType collectionType
    )
    {
        return RandomPoolPrefsHelpers.LoadIdCollection(
            BuildScopedPrefsKey(hero, collectionType),
            LogScope
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
        var scope = RandomPoolPrefsHelpers.ResolveAccountScopeForPrefs(LogScope);
        return $"{SelectedPoolPrefsKeyPrefix}.{Uri.EscapeDataString(collectionType.ToString())}.{Uri.EscapeDataString(hero.ToString())}.{scope}";
    }
}
