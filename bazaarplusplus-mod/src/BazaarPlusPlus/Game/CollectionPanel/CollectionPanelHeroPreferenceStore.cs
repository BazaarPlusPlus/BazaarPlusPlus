#nullable enable
using System;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal interface ICollectionPanelHeroPreferenceStore
{
    EHero? Load();

    void Save(EHero hero);
}

internal sealed class CollectionPanelHeroPreferenceStore : ICollectionPanelHeroPreferenceStore
{
    private const string LogScope = "CollectionPanelHeroPrefs";

    public EHero? Load()
    {
        var key = BuildScopedPrefsKey();
        if (!PlayerPrefs.HasKey(key))
            return null;

        var raw = PlayerPrefs.GetString(key, string.Empty);
        if (CollectionPanelHeroPreference.TryParse(raw, out var hero))
            return hero;

        BppLog.Warn(LogScope, $"Ignoring invalid saved hero '{raw}' for key '{key}'.");
        PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();
        return null;
    }

    public void Save(EHero hero)
    {
        if (!CollectionPanelHeroPreference.IsSupportedHero(hero))
        {
            BppLog.Warn(LogScope, $"Ignoring unsupported hero '{hero}'.");
            return;
        }

        PlayerPrefs.SetString(BuildScopedPrefsKey(), CollectionPanelHeroPreference.Serialize(hero));
        PlayerPrefs.Save();
    }

    private static string BuildScopedPrefsKey()
    {
        return CollectionPanelHeroPreference.BuildPrefsKey(ResolveAccountScopeForPrefs());
    }

    private static string? ResolveAccountScopeForPrefs()
    {
        try
        {
            var accountId = BppClientCacheBridge.TryGetProfileAccountId();
            if (!string.IsNullOrWhiteSpace(accountId))
                return accountId;

            var username = BppClientCacheBridge.TryGetProfileUsername();
            if (!string.IsNullOrWhiteSpace(username))
                return username;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                LogScope,
                $"Failed to resolve account scope; using anonymous CollectionPanel hero preference: {ex.Message}"
            );
        }

        return null;
    }
}
