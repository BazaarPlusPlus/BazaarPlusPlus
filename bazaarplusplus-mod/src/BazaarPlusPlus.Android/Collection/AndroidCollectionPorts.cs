using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Android.Collection;

/// <summary>
/// The grid side of <see cref="CollectionViewState"/> on Android: it keeps the visible cards and
/// their Core <see cref="CollectionGridLayout"/>, measured against the grid viewport's width, and
/// reports the count and content height the view state renders.
/// </summary>
internal sealed class AndroidCollectionGridPort : ICollectionGridPort
{
    private bool _published;

    public IReadOnlyList<CollectionCardVm> Visible { get; private set; } =
        Array.Empty<CollectionCardVm>();

    public CollectionTabKind Tab { get; private set; } = CollectionTabKind.Items;

    public CollectionGridLayout Layout { get; private set; } = CollectionGridLayout.Empty;

    /// <summary>Bumped on every publish, so the grid knows to re-place its cards.</summary>
    public int Revision { get; private set; }

    public float ViewportWidth { get; set; }

    public CollectionGridPixelization Pixels =>
        CollectionGridPixelization.ForViewport(ViewportWidth, Layout.Columns);

    public float ContentHeight =>
        Layout.Count == 0
            ? 0f
            : Layout.ContentHeight(Pixels.Unit, CollectionGridConstants.GridGap)
                + 2f * CollectionGridConstants.GridOuterPadding;

    public CollectionGridProjection? Publish(
        IReadOnlyList<CollectionCardVm> cards,
        CollectionTabKind activeTab
    )
    {
        Visible = cards ?? Array.Empty<CollectionCardVm>();
        Tab = activeTab;
        Layout = CollectionGridLayout.Build(Visible, activeTab);
        Revision++;
        _published = true;
        return new CollectionGridProjection(Visible.Count, ContentHeight);
    }

    public CollectionGridProjection Current =>
        _published
            ? new CollectionGridProjection(Visible.Count, ContentHeight)
            : CollectionGridProjection.Empty;
}

/// <summary>
/// The Day filter's port. Android reads the run day while a run is on, but not the GameData
/// day-tier weights the desktop resolves, so the highest tier stays null and the Day gate fails
/// open: the Day toggle shows the day and filters nothing. Out of a run there is no day; the game
/// keeps the last run's object after it ends, so its day is not read there.
/// </summary>
internal sealed class AndroidCollectionDayTierSource(Func<bool> isInRun) : ICollectionDayTierSource
{
    public CollectionDayTiers Resolve() => new(isInRun() ? CurrentDay() : null, null);

    public static int? CurrentDay()
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
}

/// <summary>
/// The remembered out-of-run hero, in PlayerPrefs under the desktop's key scoped by the game
/// account, so each account keeps its own. The account id is only a key; it is never logged.
/// </summary>
internal sealed class AndroidCollectionHeroPreferenceStore : ICollectionPanelHeroPreferenceStore
{
    private bool _degradedReported;

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
        else if (
            result.CanonicalRaw != null
            && !string.Equals(raw, result.CanonicalRaw, StringComparison.Ordinal)
        )
        {
            PlayerPrefs.SetString(key, result.CanonicalRaw);
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
        PlayerPrefs.SetString(Key(), CollectionPanelHeroPreference.Serialize(hero));
        PlayerPrefs.Save();
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
