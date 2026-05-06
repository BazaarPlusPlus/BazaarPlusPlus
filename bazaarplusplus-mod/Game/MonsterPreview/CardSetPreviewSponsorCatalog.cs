#nullable enable
using System;
using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;
    using BazaarPlusPlus.Core.Config;
    using BazaarPlusPlus.Game.Settings;
using Newtonsoft.Json;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal static class CardSetPreviewSponsorCatalog
{
    private const string DefaultSupporterListUrl =
        "https://api.example.com/supporter-list.json";
    private static readonly LocalizedTextSet SupportedByPrefix = new(
        "Supported by",
        "由",
        "由",
        "由"
    );
    private static readonly LocalizedTextSet SupportedBySuffix = new(
        string.Empty,
        "支持",
        "支持",
        "支持"
    );
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private static readonly string CacheDirectoryPath = Path.Combine(
        Path.GetTempPath(),
        "BazaarPlusPlus"
    );
    private static readonly string CacheFilePath = Path.Combine(
        CacheDirectoryPath,
        "supporter-list-cache.json"
    );
    private static readonly object SyncRoot = new();
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static string _supporterListUrl = DefaultSupporterListUrl;
    private static readonly IReadOnlyList<SupporterEntry> FallbackEntries = new[]
    {
        new SupporterEntry { Name = "Bronze Sponsor A", Tier = 2 },
        new SupporterEntry { Name = "Bronze Sponsor B", Tier = 2 },
        new SupporterEntry { Name = "Silver Sponsor A", Tier = 3 },
        new SupporterEntry { Name = "Silver Sponsor B", Tier = 3 },
        new SupporterEntry { Name = "Gold Sponsor A", Tier = 4 },
    };

    private static IReadOnlyList<SupporterEntry>? _cachedEntries;
    private static DateTime _cacheExpiresAtUtc = DateTime.MinValue;
    private static Task? _refreshTask;
    private static bool _attemptedDiskCacheLoad;

    internal static void Configure(IBppConfig? config)
    {
        _supporterListUrl = ResolveConfiguredUrl(config?.SponsorListUrlConfig?.Value);
    }

    public static CardSetPreviewSponsorSelection PickDisplay()
    {
        EnsureRefreshScheduled();
        var supporters = GetCurrentEntries();
        if (supporters.Count == 0)
            return new CardSetPreviewSponsorSelection();

        var buckets = supporters
            .GroupBy(entry => entry.Tier)
            .Select(group => new TierBucket
            {
                Tier = group.Key,
                Entries = group.Where(entry => IsRenderable(entry)).ToList(),
            })
            .Where(bucket => bucket.Entries.Count > 0)
            .ToList();
        if (buckets.Count == 0)
            return new CardSetPreviewSponsorSelection();

        var selectedBucket = PickWeighted(buckets, bucket => ResolveTierWeight(bucket.Tier));
        if (selectedBucket == null)
            return new CardSetPreviewSponsorSelection();

        var selectedEntry = PickWeighted(selectedBucket.Entries, _ => 1f);
        if (selectedEntry == null)
            return new CardSetPreviewSponsorSelection();

        var languageCode = PlayerPreferences.Data?.LanguageCode ?? string.Empty;
        var text = LanguageCodeMatcher.IsChinese(languageCode)
            ? $"{SupportedByPrefix.Resolve(languageCode)} {selectedEntry.Name} {SupportedBySuffix.Resolve(languageCode)}"
            : $"{SupportedByPrefix.Resolve(languageCode)} {selectedEntry.Name}";
        return new CardSetPreviewSponsorSelection
        {
            Text = text,
            Name = selectedEntry.Name,
            Tier = selectedEntry.Tier,
        };
    }

    public static string PickDisplayText()
    {
        return PickDisplay().Text;
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

    private static IReadOnlyList<SupporterEntry> GetCurrentEntries()
    {
        lock (SyncRoot)
        {
            TryLoadDiskCacheUnderLock();
            return _cachedEntries?.Count > 0 ? _cachedEntries : FallbackEntries;
        }
    }

    private static async Task RefreshAsync()
    {
        try
        {
            var responseBody = await HttpClient
                .GetStringAsync(_supporterListUrl)
                .ConfigureAwait(false);
            var parsed =
                JsonConvert.DeserializeObject<List<SupporterEntry>>(responseBody)
                ?? new List<SupporterEntry>();
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
                "CardSetPreviewSponsorCatalog",
                $"Loaded supporter list from remote count={sanitized.Count} expiresAtUtc={_cacheExpiresAtUtc:O}"
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CardSetPreviewSponsorCatalog",
                $"Failed to refresh supporter list from {_supporterListUrl}: {ex.Message}"
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
                JsonConvert.DeserializeObject<List<SupporterEntry>>(responseBody)
                ?? new List<SupporterEntry>();
            var sanitized = parsed.Where(IsRenderable).ToList();
            if (sanitized.Count == 0)
                return;

            var lastWriteUtc = File.GetLastWriteTimeUtc(CacheFilePath);
            _cachedEntries = sanitized;
            _cacheExpiresAtUtc = lastWriteUtc.Add(CacheDuration);
            BppLog.Info(
                "CardSetPreviewSponsorCatalog",
                $"Loaded supporter list from temp cache path={CacheFilePath} count={sanitized.Count} expiresAtUtc={_cacheExpiresAtUtc:O}"
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CardSetPreviewSponsorCatalog",
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
                "CardSetPreviewSponsorCatalog",
                $"Failed to write temp cache {CacheFilePath}: {ex.Message}"
            );
        }
    }

    private static string ResolveConfiguredUrl(string? configuredUrl)
    {
        if (string.IsNullOrWhiteSpace(configuredUrl))
            return DefaultSupporterListUrl;

        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri))
            return DefaultSupporterListUrl;

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return DefaultSupporterListUrl;

        return uri.ToString();
    }

    private static bool IsRenderable(SupporterEntry? entry)
    {
        return entry != null && !string.IsNullOrWhiteSpace(entry.Name) && entry.Tier > 0;
    }

    private static float ResolveTierWeight(int tier)
    {
        return tier switch
        {
            4 => 6f,
            3 => 4f,
            2 => 2f,
            _ => 1f,
        };
    }

    private static T? PickWeighted<T>(IReadOnlyList<T> items, Func<T, float> weightSelector)
        where T : class
    {
        if (items == null || items.Count == 0)
            return null;

        var totalWeight = 0f;
        for (var i = 0; i < items.Count; i++)
            totalWeight += Mathf.Max(0f, weightSelector(items[i]));

        if (totalWeight <= 0f)
            return null;

        var roll = UnityEngine.Random.value * totalWeight;
        for (var i = 0; i < items.Count; i++)
        {
            roll -= Mathf.Max(0f, weightSelector(items[i]));
            if (roll <= 0f)
                return items[i];
        }

        return items[items.Count - 1];
    }

    private sealed class TierBucket
    {
        public int Tier { get; set; }

        public IReadOnlyList<SupporterEntry> Entries { get; set; } = Array.Empty<SupporterEntry>();
    }

    private sealed class SupporterEntry
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("tier")]
        public int Tier { get; set; }
    }
}
