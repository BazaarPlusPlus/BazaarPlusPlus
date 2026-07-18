#nullable enable
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
    private readonly HashSet<CollectionPanelLogReasonCode> _reportedPreferenceReasons = [];
    private bool _scopeDegradedReported;

    public EHero? Load()
    {
        var key = BuildScopedPrefsKey();
        if (!PlayerPrefs.HasKey(key))
            return null;

        var raw = PlayerPrefs.GetString(key, string.Empty);
        if (CollectionPanelHeroPreference.TryParse(raw, out var hero))
            return hero;

        ReportPreferenceDegraded(CollectionPanelLogReasonCode.InvalidSavedHero, null);
        PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();
        return null;
    }

    public void Save(EHero hero)
    {
        if (!CollectionPanelHeroPreference.IsSupportedHero(hero))
        {
            ReportPreferenceDegraded(CollectionPanelLogReasonCode.UnsupportedHero, hero);
            return;
        }

        PlayerPrefs.SetString(BuildScopedPrefsKey(), CollectionPanelHeroPreference.Serialize(hero));
        PlayerPrefs.Save();
    }

    private string BuildScopedPrefsKey()
    {
        return CollectionPanelHeroPreference.BuildPrefsKey(ResolveAccountScopeForPrefs());
    }

    private string? ResolveAccountScopeForPrefs()
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
            ReportScopeDegraded(ex);
            return null;
        }

        ReportScopeDegraded(null);
        return null;
    }

    private void ReportPreferenceDegraded(CollectionPanelLogReasonCode reasonCode, EHero? hero)
    {
        if (!_reportedPreferenceReasons.Add(reasonCode))
            return;

        BppLog.WarnEvent(
            CollectionPanelLogEvents.HeroPreferenceDegraded,
            CollectionPanelLogEvents.HeroPreferenceDegradedReasonCode.Bind(reasonCode),
            CollectionPanelLogEvents.HeroPreferenceDegradedHero.Bind(hero)
        );
    }

    private void ReportScopeDegraded(Exception? exception)
    {
        if (_scopeDegradedReported)
            return;

        _scopeDegradedReported = true;
        var field = CollectionPanelLogEvents.HeroPreferenceScopeDegradedReasonCode.Bind(
            CollectionPanelLogReasonCode.IdentityUnavailable
        );
        if (exception == null)
            BppLog.WarnEvent(CollectionPanelLogEvents.HeroPreferenceScopeDegraded, field);
        else
            BppLog.WarnEvent(
                CollectionPanelLogEvents.HeroPreferenceScopeDegraded,
                exception,
                field
            );
    }
}
