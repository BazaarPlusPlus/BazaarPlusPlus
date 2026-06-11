#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BazaarPlusPlus.Infrastructure;
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
    private static DateTime _cacheExpiresAtUtc = DateTime.MinValue;
    private static Task? _refreshTask;
    private static bool _attemptedDiskCacheLoad;

    public static IReadOnlyList<BPPSupporterEntry> GetCurrentEntries()
    {
        EnsureRefreshScheduled();
        lock (SyncRoot)
        {
            TryLoadDiskCacheUnderLock();
            return _cachedEntries?.Count > 0 ? _cachedEntries : FallbackEntries;
        }
    }

    private static void EnsureRefreshScheduled()
    {
        lock (SyncRoot)
        {
            TryLoadDiskCacheUnderLock();

            if (_refreshTask != null && !_refreshTask.IsCompleted)
                return;

            var now = DateTime.UtcNow;
            if (_cachedEntries != null && now < _cacheExpiresAtUtc)
                return;

            _refreshTask = RefreshAsync();
        }
    }

    private static async Task RefreshAsync()
    {
        try
        {
            var responseBody = await HttpClient
                .GetStringAsync(SupporterListUrl)
                .ConfigureAwait(false);
            var parsed =
                JsonConvert.DeserializeObject<List<BPPSupporterEntry>>(responseBody)
                ?? new List<BPPSupporterEntry>();
            var sanitized = parsed.Where(IsRenderable).ToList();
            if (sanitized.Count == 0)
                return;

            TryWriteDiskCache(responseBody);
            lock (SyncRoot)
            {
                _cachedEntries = sanitized;
                _cacheExpiresAtUtc = DateTime.UtcNow.Add(CacheDuration);
            }

            BppLog.Info(
                "BPPSupporterCatalog",
                $"Loaded supporter list from remote count={sanitized.Count} expiresAtUtc={_cacheExpiresAtUtc:O}"
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "BPPSupporterCatalog",
                $"Failed to refresh supporter list from {SupporterListUrl}: {ex.Message}"
            );
        }
        finally
        {
            lock (SyncRoot)
            {
                _refreshTask = null;
                if (_cachedEntries == null)
                    _cacheExpiresAtUtc = DateTime.UtcNow.AddMinutes(5);
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
                return;

            var lastWriteUtc = File.GetLastWriteTimeUtc(CacheFilePath);
            _cachedEntries = sanitized;
            _cacheExpiresAtUtc = lastWriteUtc.Add(CacheDuration);
            BppLog.Info(
                "BPPSupporterCatalog",
                $"Loaded supporter list from temp cache path={CacheFilePath} count={sanitized.Count} expiresAtUtc={_cacheExpiresAtUtc:O}"
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "BPPSupporterCatalog",
                $"Failed to read temp cache {CacheFilePath}: {ex.Message}"
            );
        }
    }

    private static void TryWriteDiskCache(string responseBody)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectoryPath);
            File.WriteAllText(CacheFilePath, responseBody);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "BPPSupporterCatalog",
                $"Failed to write temp cache {CacheFilePath}: {ex.Message}"
            );
        }
    }

    private static bool IsRenderable(BPPSupporterEntry? entry)
    {
        return entry != null && !string.IsNullOrWhiteSpace(entry.Name) && entry.Tier > 0;
    }
}
