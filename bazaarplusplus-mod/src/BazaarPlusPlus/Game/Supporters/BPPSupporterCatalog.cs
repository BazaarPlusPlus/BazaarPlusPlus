#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.ModApi.Http;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.Supporters;

internal static class BPPSupporterCatalog
{
    private const string SupporterListUrl =
        "https://bpp-static.bazaarplusplus.com/supporter-list.json";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private static readonly string CacheDirectoryPath = Path.Combine(
        Path.GetTempPath(),
        "BazaarPlusPlusV4"
    );
    private static readonly string CacheFilePath = Path.Combine(
        CacheDirectoryPath,
        "supporter-list-cache.json"
    );
    private static readonly object SyncRoot = new();
    private static readonly HttpClient HttpClient = BppHttpClientFactory.Create(
        productVersion: BppPluginVersion.Current,
        userAgentSuffix: "BPPSupporterCatalog",
        timeout: TimeSpan.FromSeconds(10)
    );
    private static readonly IReadOnlyList<BPPSupporterEntry> FallbackEntries = new[]
    {
        new BPPSupporterEntry { Name = "Bronze Sponsor A", Tier = 2 },
        new BPPSupporterEntry { Name = "Bronze Sponsor B", Tier = 2 },
        new BPPSupporterEntry { Name = "Silver Sponsor A", Tier = 3 },
        new BPPSupporterEntry { Name = "Silver Sponsor B", Tier = 3 },
        new BPPSupporterEntry { Name = "Gold Sponsor A", Tier = 4 },
    };

    private static IReadOnlyList<BPPSupporterEntry>? _cachedEntries;
    private static readonly OperationalHealthTracker<
        SupporterCatalogOperation,
        SupporterCatalogFailure
    > CatalogHealth = new();
    private static readonly OperationalHealthTracker<
        SupporterCacheOperation,
        SupporterLogReasonCode
    > CacheWriteHealth = new();
    private static DateTime _cacheExpiresAtUtc = DateTime.MinValue;
    private static Task? _refreshTask;
    private static bool _attemptedDiskCacheLoad;
    private static IBppConfig? _config;
    private static int _generation;

    public static void Install(IBppConfig config)
    {
        lock (SyncRoot)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _generation++;
            _refreshTask = null;
        }
    }

    public static void Reset()
    {
        lock (SyncRoot)
        {
            _config = null;
            _generation++;
            _refreshTask = null;
            CatalogHealth.Reset();
            CacheWriteHealth.Reset();
        }
    }

    public static IReadOnlyList<BPPSupporterEntry> GetCurrentEntries()
    {
        if (IsFixedListEnabled())
            return BPPSupporterFixedList.Entries;

        EnsureRefreshScheduled();
        lock (SyncRoot)
        {
            TryLoadDiskCacheUnderLock();
            return BPPSupporterListSourcePolicy.ResolveEntries(
                useFixedList: false,
                _cachedEntries,
                FallbackEntries
            );
        }
    }

    private static bool IsFixedListEnabled() =>
        _config?.UseFixedSupporterListConfig?.Value
        ?? BPPSupporterListSourcePolicy.DefaultUseFixedList;

    private static void EnsureRefreshScheduled()
    {
        lock (SyncRoot)
        {
            TryLoadDiskCacheUnderLock();

            if (_refreshTask != null && !_refreshTask.IsCompleted)
                return;

            var now = DateTime.UtcNow;
            if (now < _cacheExpiresAtUtc)
                return;

            _refreshTask = RefreshAsync(_generation);
        }
    }

    private static async Task RefreshAsync(int generation)
    {
        try
        {
            var responseBody = await HttpClient
                .GetStringAsync(SupporterListUrl)
                .ConfigureAwait(false);
            if (!IsCurrentGeneration(generation))
                return;
            var parsed =
                JsonConvert.DeserializeObject<List<BPPSupporterEntry>>(responseBody)
                ?? new List<BPPSupporterEntry>();
            var sanitized = parsed.Where(IsRenderable).ToList();
            if (sanitized.Count == 0)
            {
                ReportCatalogFailure(
                    SupporterCatalogSource.Remote,
                    SupporterLogReasonCode.EmptyPayload,
                    generation: generation
                );
                return;
            }

            TryWriteDiskCache(responseBody, generation);
            lock (SyncRoot)
            {
                if (generation != _generation)
                    return;
                _cachedEntries = sanitized;
                _cacheExpiresAtUtc = DateTime.UtcNow.Add(CacheDuration);
            }

            ReportCatalogSuccess(SupporterCatalogSource.Remote, sanitized.Count, generation);
        }
        catch (Exception ex)
        {
            ReportCatalogFailure(
                SupporterCatalogSource.Remote,
                SupporterLogReasonCode.RefreshException,
                ex,
                generation
            );
        }
        finally
        {
            lock (SyncRoot)
            {
                if (generation == _generation)
                {
                    _refreshTask = null;
                    if (_cachedEntries == null)
                        _cacheExpiresAtUtc = DateTime.UtcNow.AddMinutes(5);
                }
            }
        }
    }

    private static void TryLoadDiskCacheUnderLock()
    {
        if (_attemptedDiskCacheLoad)
            return;

        _attemptedDiskCacheLoad = true;
        try
        {
            if (!File.Exists(CacheFilePath))
                return;

            var responseBody = File.ReadAllText(CacheFilePath);
            var parsed =
                JsonConvert.DeserializeObject<List<BPPSupporterEntry>>(responseBody)
                ?? new List<BPPSupporterEntry>();
            var sanitized = parsed.Where(IsRenderable).ToList();
            if (sanitized.Count == 0)
            {
                ReportCatalogFailure(
                    SupporterCatalogSource.DiskCache,
                    SupporterLogReasonCode.EmptyPayload
                );
                return;
            }

            var lastWriteUtc = File.GetLastWriteTimeUtc(CacheFilePath);
            _cachedEntries = sanitized;
            _cacheExpiresAtUtc = lastWriteUtc.Add(CacheDuration);
            ReportCatalogSuccess(SupporterCatalogSource.DiskCache, sanitized.Count);
        }
        catch (Exception ex)
        {
            ReportCatalogFailure(
                SupporterCatalogSource.DiskCache,
                SupporterLogReasonCode.ReadException,
                ex
            );
        }
    }

    private static void TryWriteDiskCache(string responseBody, int generation)
    {
        try
        {
            lock (SyncRoot)
            {
                if (generation != _generation)
                    return;
                Directory.CreateDirectory(CacheDirectoryPath);
                File.WriteAllText(CacheFilePath, responseBody);
                if (
                    CacheWriteHealth.ObserveSuccess(
                        SupporterCacheOperation.Write,
                        out var reasonCode
                    )
                )
                {
                    BppLog.RecoverStorm(
                        SupporterLogEvents.CacheWriteDegraded,
                        SupporterLogEvents.CacheWriteDegradedReasonCode.Bind(reasonCode)
                    );
                    BppLog.InfoEvent(
                        SupporterLogEvents.CacheWriteRecovered,
                        SupporterLogEvents.CacheWriteRecoveredPath.Bind(CacheFilePath)
                    );
                }
            }
        }
        catch (Exception ex)
        {
            lock (SyncRoot)
            {
                if (generation != _generation)
                    return;
                if (
                    CacheWriteHealth.ObserveFailure(
                        SupporterCacheOperation.Write,
                        SupporterLogReasonCode.WriteException
                    )
                )
                {
                    BppLog.WarnEvent(
                        SupporterLogEvents.CacheWriteDegraded,
                        ex,
                        SupporterLogEvents.CacheWriteDegradedPath.Bind(CacheFilePath),
                        SupporterLogEvents.CacheWriteDegradedReasonCode.Bind(
                            SupporterLogReasonCode.WriteException
                        )
                    );
                }
            }
        }
    }

    private static void ReportCatalogFailure(
        SupporterCatalogSource source,
        SupporterLogReasonCode reasonCode,
        Exception? exception = null,
        int? generation = null
    )
    {
        lock (SyncRoot)
        {
            if (generation.HasValue && generation.Value != _generation)
                return;
            if (
                !CatalogHealth.ObserveFailure(
                    SupporterCatalogOperation.Load,
                    new SupporterCatalogFailure(source, reasonCode)
                )
            )
                return;

            var fields = new[]
            {
                SupporterLogEvents.CatalogDegradedSource.Bind(source),
                SupporterLogEvents.CatalogDegradedReasonCode.Bind(reasonCode),
                SupporterLogEvents.CatalogDegradedCachePath.Bind(CacheFilePath),
            };
            if (exception == null)
                BppLog.WarnEvent(SupporterLogEvents.CatalogDegraded, fields);
            else
                BppLog.WarnEvent(SupporterLogEvents.CatalogDegraded, exception, fields);
        }
    }

    private static void ReportCatalogSuccess(
        SupporterCatalogSource source,
        int entryCount,
        int? generation = null
    )
    {
        lock (SyncRoot)
        {
            if (generation.HasValue && generation.Value != _generation)
                return;
            if (!CatalogHealth.ObserveSuccess(SupporterCatalogOperation.Load, out var failure))
            {
                BppLog.DebugEvent(
                    SupporterLogEvents.CatalogLoaded,
                    () =>
                        [
                            SupporterLogEvents.CatalogLoadedSource.Bind(source),
                            SupporterLogEvents.CatalogLoadedEntryCount.Bind(entryCount),
                        ]
                );
                return;
            }

            BppLog.RecoverStorm(
                SupporterLogEvents.CatalogDegraded,
                SupporterLogEvents.CatalogDegradedSource.Bind(failure.Source),
                SupporterLogEvents.CatalogDegradedReasonCode.Bind(failure.Reason)
            );

            BppLog.InfoEvent(
                SupporterLogEvents.CatalogRecovered,
                SupporterLogEvents.CatalogRecoveredSource.Bind(source),
                SupporterLogEvents.CatalogRecoveredEntryCount.Bind(entryCount)
            );
        }
    }

    private static bool IsCurrentGeneration(int generation)
    {
        lock (SyncRoot)
            return generation == _generation;
    }

    private static bool IsRenderable(BPPSupporterEntry? entry)
    {
        return entry != null && !string.IsNullOrWhiteSpace(entry.Name) && entry.Tier > 0;
    }
}
