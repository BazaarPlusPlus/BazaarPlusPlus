#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionPanelHeroPreferenceStore : ICollectionPanelHeroPreferenceStore
{
    private readonly HashSet<CollectionPanelLogReasonCode> _reportedPreferenceReasons = [];
    private bool _scopeDegradedReported;

    public CollectionPanelHeroPreferenceLoadResult Load(
        CollectionCatalogReadiness catalogReadiness,
        IReadOnlyCollection<EHero> availableHeroes
    )
    {
        var key = BuildScopedPrefsKey();
        if (!PlayerPrefs.HasKey(key))
        {
            return CollectionPanelHeroPreference.ResolveStored(
                hasStoredValue: false,
                raw: null,
                catalogReadiness,
                availableHeroes
            );
        }

        var raw = PlayerPrefs.GetString(key, string.Empty);
        var result = CollectionPanelHeroPreference.ResolveStored(
            hasStoredValue: true,
            raw,
            catalogReadiness,
            availableHeroes
        );
        if (result.Status != CollectionPanelHeroPreferenceLoadStatus.Invalid)
        {
            if (
                result.CanonicalRaw != null
                && !string.Equals(raw, result.CanonicalRaw, StringComparison.Ordinal)
            )
            {
                PlayerPrefs.SetString(key, result.CanonicalRaw);
                PlayerPrefs.Save();
            }
            return result;
        }

        ReportPreferenceDegraded(CollectionPanelLogReasonCode.InvalidSavedHero, null);
        PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();
        return result;
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
            new BppLogEvent(
                BppLogFeatureScope.CollectionPanel,
                "collection_panel.hero_preference.degraded",
                storm: ["reason_code"]
            ),
            ("reason_code", reasonCode),
            ("hero", hero)
        );
    }

    private void ReportScopeDegraded(Exception? exception)
    {
        if (_scopeDegradedReported)
            return;

        _scopeDegradedReported = true;
        BppLogField field = ("reason_code", CollectionPanelLogReasonCode.IdentityUnavailable);
        if (exception == null)
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CollectionPanel,
                    "collection_panel.hero_preference.scope_degraded",
                    storm: ["reason_code"]
                ),
                field
            );
        else
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CollectionPanel,
                    "collection_panel.hero_preference.scope_degraded",
                    storm: ["reason_code"]
                ),
                exception,
                field
            );
    }
}
