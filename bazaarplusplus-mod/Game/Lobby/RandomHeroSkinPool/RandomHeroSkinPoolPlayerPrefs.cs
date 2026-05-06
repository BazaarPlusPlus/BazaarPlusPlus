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
    private const string LegacyHeroSkinPoolPrefsKeyPrefix = "BPP.RandomHeroSkinPool.Selected";
    private const string LogScope = "RandomHeroSkinPool";

    public static IReadOnlyCollection<string>? LoadSelectedIds(
        EHero hero,
        BazaarInventoryTypes.ECollectionType collectionType
    )
    {
        var key = BuildScopedPrefsKey(hero, collectionType);
        var selectedIds = RandomPoolPrefsHelpers.LoadIdCollection(key, LogScope);
        if (selectedIds != null)
            return selectedIds;

        if (collectionType != BazaarInventoryTypes.ECollectionType.HeroSkins)
            return null;

        var legacy = RandomPoolPrefsHelpers.LoadIdCollection(
            BuildLegacyHeroSkinPrefsKey(hero),
            LogScope
        );
        if (legacy == null)
            return null;

        SaveSelectedIds(hero, collectionType, legacy);
        return legacy;
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

    private static string BuildLegacyHeroSkinPrefsKey(EHero hero)
    {
        var scope = RandomPoolPrefsHelpers.ResolveAccountScopeForPrefs(LogScope);
        return $"{LegacyHeroSkinPoolPrefsKeyPrefix}.{Uri.EscapeDataString(hero.ToString())}.{scope}";
    }
}
