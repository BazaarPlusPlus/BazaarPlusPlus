#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Runtime;
using Newtonsoft.Json;
using UnityEngine;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;

internal static class RandomHeroSkinPoolPlayerPrefs
{
    private const string SelectedPoolPrefsKeyPrefix = "BPP.RandomCollectiblePool.Selected";
    private const string LegacyHeroSkinPoolPrefsKeyPrefix = "BPP.RandomHeroSkinPool.Selected";
    private const string AnonymousAccountScope = "anonymous";

    public static IReadOnlyCollection<string>? LoadSelectedIds(
        EHero hero,
        BazaarInventoryTypes.ECollectionType collectionType
    )
    {
        var key = BuildScopedPrefsKey(hero, collectionType);
        var selectedIds = LoadIdCollection(key);
        if (selectedIds != null)
            return selectedIds;

        if (collectionType != BazaarInventoryTypes.ECollectionType.HeroSkins)
            return null;

        var legacy = LoadIdCollection(BuildLegacyHeroSkinPrefsKey(hero));
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
        SaveIdCollection(BuildScopedPrefsKey(hero, collectionType), ids);
    }

    private static IReadOnlyCollection<string>? LoadIdCollection(string key)
    {
        if (!PlayerPrefs.HasKey(key))
            return null;

        var raw = PlayerPrefs.GetString(key, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            return JsonConvert.DeserializeObject<string[]>(raw);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "RandomHeroSkinPool",
                $"Failed to parse saved random hero skin pool '{key}': {ex.Message}"
            );
            return null;
        }
    }

    private static void SaveIdCollection(string key, IEnumerable<string> ids)
    {
        var normalized = ids.Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0)
        {
            PlayerPrefs.DeleteKey(key);
        }
        else
        {
            PlayerPrefs.SetString(key, JsonConvert.SerializeObject(normalized));
        }

        PlayerPrefs.Save();
    }

    private static string BuildScopedPrefsKey(
        EHero hero,
        BazaarInventoryTypes.ECollectionType collectionType
    )
    {
        return $"{SelectedPoolPrefsKeyPrefix}.{Uri.EscapeDataString(collectionType.ToString())}.{Uri.EscapeDataString(hero.ToString())}.{ResolveAccountScopeForPrefs()}";
    }

    private static string BuildLegacyHeroSkinPrefsKey(EHero hero)
    {
        return $"{LegacyHeroSkinPoolPrefsKeyPrefix}.{Uri.EscapeDataString(hero.ToString())}.{ResolveAccountScopeForPrefs()}";
    }

    private static string ResolveAccountScopeForPrefs()
    {
        try
        {
            var accountId = BppClientCacheBridge.TryGetProfileAccountId();
            if (!string.IsNullOrWhiteSpace(accountId))
                return Uri.EscapeDataString(accountId);

            var username = BppClientCacheBridge.TryGetProfileUsername();
            if (!string.IsNullOrWhiteSpace(username))
                return Uri.EscapeDataString(username);
        }
        catch { }

        return AnonymousAccountScope;
    }
}
