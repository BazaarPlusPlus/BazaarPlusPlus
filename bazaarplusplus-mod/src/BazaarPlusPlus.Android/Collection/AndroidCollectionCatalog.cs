using System.Diagnostics;
using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using Il2CppBazaarGameShared.Domain.Cards;

namespace BazaarPlusPlus.Android.Collection;

internal enum AndroidCollectionCatalogStatus
{
    Idle,
    WaitingForGameData,
    Projecting,
    Ready,
    Unavailable,
}

/// <summary>
/// The Collection catalog on Android, built once per game language from the game's static card
/// map and kept for the session. The first <c>GetCardMap</c> call can hold the main thread for
/// seconds while the game deserializes its card data, so the panel shows its loading state for a
/// frame before <see cref="Advance"/> makes it; the projection then runs in frame-budgeted slices.
/// Every projected template is handed to the card rental, so renting it needs no lookup.
/// </summary>
internal sealed class AndroidCollectionCatalog
{
    private const double SliceBudgetMs = 8;

    private readonly List<CollectionCardVm> _building = new();
    private List<TCardBase>? _templates;
    private int _cursor;
    private int _rejected;
    private int _unmapped;
    private int _failed;
    private int _frames;
    private long _mapMs;
    private readonly Stopwatch _projectClock = new();
    private string? _language;
    private bool _mapRequested;

    public AndroidCollectionCatalogStatus Status { get; private set; } =
        AndroidCollectionCatalogStatus.Idle;

    public IReadOnlyList<CollectionCardVm>? Cards { get; private set; }

    /// <summary>The cached cards for the current language, or null when a build is needed.</summary>
    public IReadOnlyList<CollectionCardVm>? Cached =>
        Status == AndroidCollectionCatalogStatus.Ready
        && string.Equals(_language, L.CurrentLanguageCode, StringComparison.Ordinal)
            ? Cards
            : null;

    /// <summary>Main thread, once per frame while a panel waits for the catalog.</summary>
    public void Advance()
    {
        if (
            Status == AndroidCollectionCatalogStatus.Ready
            && !string.Equals(_language, L.CurrentLanguageCode, StringComparison.Ordinal)
        )
            Reset();
        switch (Status)
        {
            case AndroidCollectionCatalogStatus.Idle:
                Status = AndroidCollectionCatalogStatus.WaitingForGameData;
                return; // one frame of loading chrome before the map can stall
            case AndroidCollectionCatalogStatus.WaitingForGameData:
                if (!_mapRequested)
                {
                    _mapRequested = true;
                    return;
                }
                FetchTemplates();
                return;
            case AndroidCollectionCatalogStatus.Projecting:
                ProjectSlice();
                return;
            default:
                return;
        }
    }

    /// <summary>A failed build may be retried on the next open.</summary>
    public void RetryIfUnavailable()
    {
        if (Status == AndroidCollectionCatalogStatus.Unavailable)
            Reset();
    }

    private void Reset()
    {
        Status = AndroidCollectionCatalogStatus.Idle;
        Cards = null;
        _templates = null;
        _building.Clear();
        _cursor = _rejected = _unmapped = _failed = _frames = 0;
        _mapRequested = false;
    }

    private void FetchTemplates()
    {
        try
        {
            if (!Il2CppTheBazaar.Data.IsManagerCreated())
                return; // keep waiting: the game has not created its data manager yet
            var clock = Stopwatch.StartNew();
            var map = Il2CppTheBazaar.Data.GetStatic()?.GetCardMap();
            if (map == null)
            {
                Fail("card_map_missing");
                return;
            }
            var templates = new List<TCardBase>(map.Count);
            foreach (var entry in map)
            {
                var value = entry.Value;
                if (value != null)
                    templates.Add(new TCardBase(value.Pointer));
            }
            _mapMs = clock.ElapsedMilliseconds;
            _templates = templates;
            _language = L.CurrentLanguageCode;
            _projectClock.Reset();
            Status = AndroidCollectionCatalogStatus.Projecting;
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.collection.catalog_failed"),
                exception
            );
            Fail("card_map_threw");
        }
    }

    private void ProjectSlice()
    {
        var templates = _templates!;
        var slice = Stopwatch.StartNew();
        _projectClock.Start();
        _frames++;
        while (_cursor < templates.Count && slice.Elapsed.TotalMilliseconds < SliceBudgetMs)
        {
            var template = templates[_cursor++];
            try
            {
                var vm = AndroidCollectionCardProjection.From(template, out var eligible);
                if (vm == null)
                {
                    if (eligible)
                        _unmapped++;
                    else
                        _rejected++;
                    continue;
                }
                _building.Add(vm);
                AndroidCardRental.Remember(vm.Id, template);
            }
            catch (Exception)
            {
                // One card the projection cannot read stays out; the rest of the catalog loads.
                _failed++;
            }
        }
        _projectClock.Stop();
        if (_cursor < templates.Count)
            return;
        Cards = _building.ToArray();
        _templates = null;
        Status =
            Cards.Count > 0
                ? AndroidCollectionCatalogStatus.Ready
                : AndroidCollectionCatalogStatus.Unavailable;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.collection.catalog_built"),
            ("templates", templates.Count),
            ("accepted", Cards.Count),
            ("rejected", _rejected),
            ("unmapped", _unmapped),
            ("failed", _failed),
            ("map_ms", _mapMs),
            ("project_ms", _projectClock.ElapsedMilliseconds),
            ("frames", _frames),
            ("language", _language)
        );
    }

    private void Fail(string reason)
    {
        Status = AndroidCollectionCatalogStatus.Unavailable;
        BppLog.WarnEvent(
            new BppLogEvent(
                BppLogFeatureScope.Android,
                "android.collection.catalog_unavailable",
                storm: ["reason_code"]
            ),
            ("reason_code", reason)
        );
    }
}
