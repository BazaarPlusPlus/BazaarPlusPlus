using System.Diagnostics;
using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.Mobile.Collection;
using Il2CppBazaarGameShared.Domain.Cards;
using Il2CppTheBazaar.DataManagement;
using Microsoft.Data.Sqlite;
using UnityEngine;

namespace BazaarPlusPlus.Android.Collection;

internal enum AndroidCollectionCatalogStatus
{
    Idle,
    Preparing,
    Projecting,
    Ready,
    Unavailable,
}

internal enum AndroidCollectionCatalogPace
{
    Waiting,

    Warm,
}

// GetCardMap deserializes every card on the Unity thread and can stall it for seconds. Load
// individual cards instead.
internal sealed class AndroidCollectionCatalog : ICollectionCardCatalog
{
    private const float SlowFrameSeconds = 0.02f;

    private readonly CollectionCatalogCacheStore _cache;
    private readonly List<CollectionCardVm> _building = new();
    private readonly List<double> _cardMs = new();
    private readonly Stopwatch _projectClock = new();
    private Task<Preparation>? _prepare;
    private Request? _request;
    private CollectionCatalogCacheIdentity? _identity;
    private string _cacheOutcome = string.Empty;
    private long _prepareMs;
    private IReadOnlyList<Guid>? _candidates;
    private int _cursor;
    private int _rejected;
    private int _unmapped;
    private int _failed;
    private int _unresolved;
    private int _frames;
    private string? _language;
    private IntPtr _manager;

    public AndroidCollectionCatalog(string cacheDirectory) => _cache = new(cacheDirectory);

    public AndroidCollectionCatalogStatus Status { get; private set; } =
        AndroidCollectionCatalogStatus.Idle;

    public IReadOnlyList<CollectionCardVm>? Cards { get; private set; }

    public bool IsBuilding =>
        Status
            is AndroidCollectionCatalogStatus.Idle
                or AndroidCollectionCatalogStatus.Preparing
                or AndroidCollectionCatalogStatus.Projecting;

    public IReadOnlyList<CollectionCardVm>? Cached =>
        Status == AndroidCollectionCatalogStatus.Ready
        && string.Equals(_language, L.CurrentLanguageCode, StringComparison.Ordinal)
            ? Cards
            : null;

    public void Advance(AndroidCollectionCatalogPace pace)
    {
        if (
            Status == AndroidCollectionCatalogStatus.Ready
            && !string.Equals(_language, L.CurrentLanguageCode, StringComparison.Ordinal)
        )
            Reset();
        switch (Status)
        {
            case AndroidCollectionCatalogStatus.Idle:
                Start();
                return;
            case AndroidCollectionCatalogStatus.Preparing:
                Collect();
                return;
            case AndroidCollectionCatalogStatus.Projecting:
                ProjectSlice(pace);
                return;
            default:
                return;
        }
    }

    public CollectionCardCatalogStatus Advance()
    {
        Advance(AndroidCollectionCatalogPace.Waiting);
        return Status switch
        {
            AndroidCollectionCatalogStatus.Ready => CollectionCardCatalogStatus.Ready,
            AndroidCollectionCatalogStatus.Unavailable => CollectionCardCatalogStatus.Unavailable,
            _ => CollectionCardCatalogStatus.Building,
        };
    }

    public void RetryIfUnavailable()
    {
        if (Status == AndroidCollectionCatalogStatus.Unavailable)
            Reset();
    }

    // The SQL prefilter must keep a superset of CollectionCardEligibility, including rows it cannot
    // parse.
    internal static List<Guid> ReadCandidateIds(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        // The cast matters: SQLite reads a BLOB argument as its binary JSON.
        command.CommandText = """
            SELECT Id FROM (SELECT Id, CAST(Data AS TEXT) AS Json FROM cards)
            WHERE NOT json_valid(Json)
                OR ((json_type(Json, '$.Type') IS NOT 'text'
                        OR json_extract(Json, '$.Type') IN ('Item', 'Skill'))
                    AND (json_type(Json, '$.SpawningEligibility') IS NOT 'text'
                        OR json_extract(Json, '$.SpawningEligibility') <> 'Never'))
            """;
        using var reader = command.ExecuteReader();
        var ids = new List<Guid>();
        while (reader.Read())
        {
            if (Guid.TryParse(reader.GetString(0), out var id) && id != Guid.Empty)
                ids.Add(id);
        }
        return ids;
    }

    private void Reset()
    {
        Status = AndroidCollectionCatalogStatus.Idle;
        Cards = null;
        _prepare = null;
        _request = null;
        _identity = null;
        _candidates = null;
        _building.Clear();
        _cardMs.Clear();
        _cursor = _rejected = _unmapped = _failed = _unresolved = _frames = 0;
    }

    private void Start()
    {
        if (!GameInitialized())
            return; // keep waiting: until then the game's localization answers in English
        try
        {
            var manager = Il2CppTheBazaar.Data.GetStatic();
            if (manager == null)
                return;
            var databasePath = manager._dbPath;
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                Fail("no_database_path");
                return;
            }
            _manager = manager.Pointer;
            _language = L.CurrentLanguageCode;
            var request = new Request(
                databasePath,
                Path.Combine(DataManifestActions.Default.GetCachePath(), "manifest.json"),
                Path.Combine(DataManifestActions.Translations.GetCachePath(), "manifest.json"),
                _language,
                Application.version ?? string.Empty
            );
            _request = request;
            _prepare = Task.Run(() => Prepare(request));
            Status = AndroidCollectionCatalogStatus.Preparing;
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.collection.catalog_failed"),
                exception
            );
            Fail("start_threw");
        }
    }

    private static bool GameInitialized()
    {
        try
        {
            return Il2CppTheBazaar.Data.IsManagerCreated()
                && Il2CppTheBazaar.GameInstance.Instance?.IsInitialized == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Pool thread: file and SQLite reads only, never the IL2CPP runtime.
    private Preparation Prepare(Request request)
    {
        var clock = Stopwatch.StartNew();
        var identity = CollectionCatalogCacheIdentity.Resolve(
            request.GameDataManifest,
            request.TranslationsManifest,
            request.Language,
            request.GameVersion,
            AndroidCollectionCardProjection.CacheVersion
        );
        var outcome = "no_identity";
        if (identity != null && _cache.TryLoad(identity, out var cards, out outcome))
            return new(identity, cards, null, "hit", clock.ElapsedMilliseconds);
        var candidates = ReadCandidateIds(request.DatabasePath);
        return new(identity, null, candidates, outcome, clock.ElapsedMilliseconds);
    }

    private void Collect()
    {
        var prepare = _prepare!;
        if (!prepare.IsCompleted)
            return;
        _prepare = null;
        if (!prepare.IsCompletedSuccessfully)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.collection.catalog_failed"),
                prepare.Exception?.GetBaseException()
                    ?? new OperationCanceledException("The catalog read was cancelled.")
            );
            Fail("candidates_unreadable");
            return;
        }
        var prepared = prepare.Result;
        _identity = prepared.Identity;
        _cacheOutcome = prepared.CacheOutcome;
        _prepareMs = prepared.ElapsedMs;
        if (prepared.Cards != null)
        {
            Complete(prepared.Cards, "cache", 0);
            return;
        }
        if (prepared.Candidates is not { Count: > 0 } candidates)
        {
            Complete([], "built", 0);
            return;
        }
        _candidates = candidates;
        _projectClock.Reset();
        Status = AndroidCollectionCatalogStatus.Projecting;
    }

    private void ProjectSlice(AndroidCollectionCatalogPace pace)
    {
        var warm = pace == AndroidCollectionCatalogPace.Warm;
        if (warm && Time.unscaledDeltaTime > SlowFrameSeconds)
            return; // the game is already behind this frame
        var candidates = _candidates!;
        var manager = Il2CppTheBazaar.Data.GetStatic();
        if (manager == null)
            return;
        var slice = Stopwatch.StartNew();
        _projectClock.Start();
        _frames++;
        do
        {
            var id = candidates[_cursor++];
            var started = Stopwatch.GetTimestamp();
            try
            {
                var template = manager
                    .GetCardById(Il2CppSystem.Guid.Parse(id.ToString()))
                    ?.TryCast<TCardBase>();
                if (template == null)
                    _unresolved++;
                else
                    Project(template);
            }
            catch (Exception)
            {
                _failed++;
            }
            _cardMs.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        } while (
            !warm
            && _cursor < candidates.Count
            && slice.Elapsed.TotalMilliseconds < CollectionCardCatalogBudget.WaitingFrameBudgetMs
        );
        _projectClock.Stop();
        if (_cursor < candidates.Count)
            return;
        Complete(_building.ToArray(), "built", candidates.Count);
        if (Cacheable())
            Save(_identity!, _request!, Cards!);
    }

    private void Project(TCardBase template)
    {
        var vm = AndroidCollectionCardProjection.From(template, out var eligible);
        if (vm == null)
        {
            if (eligible)
                _unmapped++;
            else
                _rejected++;
            return;
        }
        _building.Add(vm);
        AndroidCardRental.Remember(vm.Id, template);
    }

    private void Complete(IReadOnlyList<CollectionCardVm> cards, string source, int candidates)
    {
        Cards = cards;
        _candidates = null;
        _building.Clear();
        Status =
            cards.Count > 0
                ? AndroidCollectionCatalogStatus.Ready
                : AndroidCollectionCatalogStatus.Unavailable;
        _cardMs.Sort();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.collection.catalog_built"),
            ("source", source),
            ("cache", _cacheOutcome),
            ("candidates", candidates),
            ("accepted", cards.Count),
            ("rejected", _rejected),
            ("unmapped", _unmapped),
            ("unresolved", _unresolved),
            ("failed", _failed),
            ("prepare_ms", _prepareMs),
            ("project_ms", _projectClock.ElapsedMilliseconds),
            ("frames", _frames),
            ("card_p50_ms", Percentile(0.5)),
            ("card_p95_ms", Percentile(0.95)),
            ("card_max_ms", Percentile(1)),
            ("language", _language)
        );
        _cardMs.Clear();
    }

    private double Percentile(double fraction)
    {
        if (_cardMs.Count == 0)
            return 0;
        var index = Math.Clamp(
            (int)Math.Ceiling(fraction * _cardMs.Count) - 1,
            0,
            _cardMs.Count - 1
        );
        return Math.Round(_cardMs[index], 1);
    }

    private bool Cacheable()
    {
        if (_identity == null || _failed > 0 || _unresolved > 0 || Cards is not { Count: > 0 })
            return false;
        try
        {
            return string.Equals(_language, L.CurrentLanguageCode, StringComparison.Ordinal)
                && Il2CppTheBazaar.Data.GetStatic()?.Pointer == _manager;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Re-read the manifests so a download during projection cannot cache the results under a stale
    // identity.
    private void Save(
        CollectionCatalogCacheIdentity identity,
        Request request,
        IReadOnlyList<CollectionCardVm> cards
    ) =>
        _ = Task.Run(() =>
        {
            try
            {
                var current = CollectionCatalogCacheIdentity.Resolve(
                    request.GameDataManifest,
                    request.TranslationsManifest,
                    request.Language,
                    request.GameVersion,
                    AndroidCollectionCardProjection.CacheVersion
                );
                if (!identity.Equals(current))
                {
                    BppLog.InfoEvent(
                        new BppLogEvent(
                            BppLogFeatureScope.Android,
                            "android.collection.catalog_cache_skipped"
                        ),
                        ("reason_code", "identity_changed")
                    );
                    return;
                }
                _cache.Save(identity, cards);
                BppLog.InfoEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Android,
                        "android.collection.catalog_cache_saved"
                    ),
                    ("cards", cards.Count),
                    ("language", identity.Language)
                );
            }
            catch (Exception exception)
            {
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Android,
                        "android.collection.catalog_cache_failed"
                    ),
                    exception
                );
            }
        });

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

    private sealed record Request(
        string DatabasePath,
        string GameDataManifest,
        string TranslationsManifest,
        string Language,
        string GameVersion
    );

    private sealed record Preparation(
        CollectionCatalogCacheIdentity? Identity,
        IReadOnlyList<CollectionCardVm>? Cards,
        IReadOnlyList<Guid>? Candidates,
        string CacheOutcome,
        long ElapsedMs
    );
}
