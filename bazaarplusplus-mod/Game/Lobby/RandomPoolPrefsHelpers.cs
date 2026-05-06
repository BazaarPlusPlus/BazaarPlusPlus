#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Core.Runtime;
using Newtonsoft.Json;
using UnityEngine;

namespace BazaarPlusPlus.Game.Lobby;

internal static class RandomPoolPrefsHelpers
{
    private const string AnonymousAccountScope = "anonymous";

    public static IReadOnlyCollection<string>? LoadIdCollection(string key, string logScope)
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
            BppLog.Warn(logScope, $"Failed to parse saved pool '{key}': {ex.Message}");
            return null;
        }
    }

    public static void SaveIdCollection(string key, IEnumerable<string> ids)
    {
        var normalized = NormalizeIds(ids);
        if (normalized.Length == 0)
            PlayerPrefs.DeleteKey(key);
        else
            PlayerPrefs.SetString(key, JsonConvert.SerializeObject(normalized));

        PlayerPrefs.Save();
    }

    public static string[] NormalizeIds(IEnumerable<string> ids) =>
        ids.Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static string ResolveAccountScopeForPrefs(string logScope)
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
        catch (Exception ex)
        {
            BppLog.Warn(
                logScope,
                $"Failed to resolve account scope; falling back to anonymous: {ex.Message}"
            );
        }

        return AnonymousAccountScope;
    }
}
