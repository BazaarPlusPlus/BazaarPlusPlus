#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Core.Runtime;
using HarmonyLib;
using Newtonsoft.Json;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

internal static class RandomHeroPoolPlayerPrefs
{
    private const string SelectedPoolPrefsKeyPrefix = "BPP.RandomHeroPool.Selected";
    private const string AnonymousAccountScope = "anonymous";

    public static IReadOnlyCollection<string>? LoadSelectedHeroIds()
    {
        return LoadHeroIdCollection(BuildScopedPrefsKey(SelectedPoolPrefsKeyPrefix));
    }

    public static void SaveSelectedHeroIds(IEnumerable<string> heroIds)
    {
        SaveHeroIdCollection(BuildScopedPrefsKey(SelectedPoolPrefsKeyPrefix), heroIds);
    }

    public static bool TryResolveState(
        IEnumerable<string> unlockedHeroIds,
        out RandomHeroPoolState? state
    )
    {
        if (unlockedHeroIds is null)
            throw new ArgumentNullException(nameof(unlockedHeroIds));

        var normalizedUnlockedHeroIds = NormalizeHeroIds(unlockedHeroIds);
        if (normalizedUnlockedHeroIds.Length == 0)
        {
            state = null;
            return false;
        }

        state = RandomHeroPoolStateFactory.Create(normalizedUnlockedHeroIds, LoadSelectedHeroIds());
        return true;
    }

    public static IReadOnlyList<string> ResolveEffectivePool(IEnumerable<string> unlockedHeroIds)
    {
        if (!TryResolveState(unlockedHeroIds, out var state) || state == null)
        {
            return Array.Empty<string>();
        }

        var candidateHeroIds = state.SelectedHeroIds.ToArray();
        SaveSelectedHeroIds(candidateHeroIds);
        return candidateHeroIds;
    }

    private static IReadOnlyCollection<string>? LoadHeroIdCollection(string key)
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
                "RandomHeroPool",
                $"Failed to parse saved random hero pool '{key}': {ex.Message}"
            );
            return null;
        }
    }

    private static void SaveHeroIdCollection(string key, IEnumerable<string> heroIds)
    {
        var normalized = NormalizeHeroIds(heroIds);

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

    private static string[] NormalizeHeroIds(IEnumerable<string> heroIds)
    {
        return heroIds
            .Where(heroId => !string.IsNullOrWhiteSpace(heroId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string BuildScopedPrefsKey(string keyPrefix)
    {
        return $"{keyPrefix}.{ResolveAccountScopeForPrefs()}";
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
