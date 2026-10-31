using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Android.Collection;

/// <summary>
/// The Day filter's port. Android reads the run day while a run is on, but not the GameData
/// day-tier weights the desktop resolves, so the highest tier stays null and the Day gate fails
/// open: the Day toggle shows the day and filters nothing. Out of a run there is no day; the game
/// keeps the last run's object after it ends, so its day is not read there.
/// </summary>
internal sealed class AndroidCollectionDayTierSource(ICollectionRunReads run)
    : ICollectionDayTierSource
{
    public CollectionDayTiers Resolve() => new(run.IsInRun() ? run.Day() : null, null);
}

/// <summary>The touch Collection's reads of the run, over the IL2CPP game data.</summary>
internal sealed class AndroidCollectionRunReads : ICollectionRunReads
{
    private readonly AndroidGameStateProbe _gameState = new();

    public bool IsInRun()
    {
        try
        {
            return _gameState.ComputeIsInGameRun();
        }
        catch (Exception)
        {
            return false;
        }
    }

    public int? Day()
    {
        try
        {
            var run = Il2CppTheBazaar.Data.Run;
            return run == null ? null : unchecked((int)run.Day);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public CardHero? Hero()
    {
        try
        {
            var player = Il2CppTheBazaar.Data.Run?.Player;
            if (
                player != null
                && AndroidCardFacets.Hero.TryToFacet(player.Hero, out var runHero)
                && CollectionPanelSelectionState.IsConcreteHero(runHero)
            )
                return runHero;
            return
                AndroidCardFacets.Hero.TryToFacet(
                    Il2CppTheBazaar.Data.SelectedHero,
                    out var selected
                ) && CollectionPanelSelectionState.IsConcreteHero(selected)
                ? selected
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// The remembered out-of-run hero, in PlayerPrefs under the desktop's key scoped by the game
/// account, so each account keeps its own. The account id is only a key; it is never logged.
/// </summary>
internal sealed class AndroidCollectionHeroPreferenceStore : ICollectionPanelHeroPreferenceStore
{
    private bool _degradedReported;
    private bool _unsaved;

    public CollectionPanelHeroPreferenceLoadResult Load(
        CollectionCatalogReadiness catalogReadiness,
        IReadOnlyCollection<CardHero> availableHeroes
    )
    {
        var key = Key();
        var hasStored = PlayerPrefs.HasKey(key);
        var raw = hasStored ? PlayerPrefs.GetString(key, string.Empty) : null;
        var result = CollectionPanelHeroPreference.ResolveStored(
            hasStored,
            raw,
            catalogReadiness,
            availableHeroes
        );
        if (!hasStored)
            return result;
        if (result.Status == CollectionPanelHeroPreferenceLoadStatus.Invalid)
        {
            ReportDegraded("invalid_saved_hero");
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
        return result;
    }

    public void Save(CardHero hero)
    {
        if (!CollectionPanelHeroPreference.IsSupportedHero(hero))
        {
            ReportDegraded("unsupported_hero");
            return;
        }
        // PlayerPrefs.Save rewrites the SharedPreferences file; a hero tap only stages the value
        // and the panel flushes it on close or when the app pauses (leaves the foreground).
        PlayerPrefs.SetString(Key(), CollectionPanelHeroPreference.Serialize(hero));
        _unsaved = true;
    }

    public void Flush()
    {
        if (!_unsaved)
            return;
        // Cleared only after the write, so a save that throws is retried on the next flush.
        PlayerPrefs.Save();
        _unsaved = false;
    }

    private static string Key()
    {
        string? scope = null;
        try
        {
            scope = AndroidClientCache.TryGetProfileAccountId();
            if (string.IsNullOrWhiteSpace(scope))
                scope = AndroidClientCache.TryGetProfileUsername();
        }
        catch (Exception)
        {
            scope = null;
        }
        return CollectionPanelHeroPreference.BuildPrefsKey(scope);
    }

    private void ReportDegraded(string reason)
    {
        if (_degradedReported)
            return;
        _degradedReported = true;
        BppLog.WarnEvent(
            new BppLogEvent(
                BppLogFeatureScope.Android,
                "android.collection.hero_preference_degraded",
                storm: ["reason_code"]
            ),
            ("reason_code", reason)
        );
    }
}
