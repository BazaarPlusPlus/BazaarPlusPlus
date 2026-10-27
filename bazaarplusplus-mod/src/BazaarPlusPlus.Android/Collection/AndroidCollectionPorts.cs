using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Android.Collection;

/// <summary>
/// The grid side of <see cref="CollectionViewState"/> on Android: it keeps the visible cards and
/// their Core <see cref="CollectionGridLayout"/>, packed into the column count the touch layout
/// chooses for the grid viewport's width (<see cref="CollectionTouchLayout.GridColumns"/>), and
/// reports the count and content height the view state renders. A new width that changes the
/// column count re-packs the same cards.
/// </summary>
internal sealed class AndroidCollectionGridPort : ICollectionGridPort
{
    private bool _published;
    private float _viewportWidth;

    public IReadOnlyList<CollectionCardVm> Visible { get; private set; } =
        Array.Empty<CollectionCardVm>();

    public CollectionTabKind Tab { get; private set; } = CollectionTabKind.Items;

    public CollectionGridLayout Layout { get; private set; } = CollectionGridLayout.Empty;

    /// <summary>Bumped on every publish, so the grid knows to re-place its cards.</summary>
    public int Revision { get; private set; }

    /// <summary>The touch layout the columns follow; set before the grid measures its viewport.</summary>
    public CollectionTouchLayout? Touch { get; set; }

    public float ViewportWidth
    {
        get => _viewportWidth;
        set
        {
            _viewportWidth = value;
            var columns = Columns(Tab);
            if (columns != Layout.Columns)
            {
                Layout = CollectionGridLayout.Build(Visible, Tab, columns);
                Revision++;
            }
        }
    }

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
        Layout = CollectionGridLayout.Build(Visible, activeTab, Columns(activeTab));
        Revision++;
        _published = true;
        return new CollectionGridProjection(Visible.Count, ContentHeight);
    }

    private int Columns(CollectionTabKind tab) =>
        Touch?.GridColumns(tab.CardType(), _viewportWidth)
        ?? CollectionGridConstants.ColumnsFor(tab.CardType());

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
