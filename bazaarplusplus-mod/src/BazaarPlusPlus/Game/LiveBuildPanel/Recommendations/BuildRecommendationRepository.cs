#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

/// <summary>
/// Loads the analyzer-v4 ten-win build corpus (cached locally with a background remote refresh),
/// answers recommendation queries against the live state, and projects matched builds onto
/// renderable item boards. The corpus is a static package — recommendation queries are answered
/// from the local copy and never hit the server.
/// </summary>
internal sealed class BuildRecommendationRepository
{
    private const string TenWinBuildsRemoteUrl =
        "https://bpp-metrics.bazaarplusplus.com/analyzer-v4/mod/tenwin_builds.json";
    private const string TenWinBuildsCacheFileName = "tenwin_builds.json";
    private static readonly LocalizedTextSet FinalBuildLabel = new(
        "Ten-Win Build",
        "十胜阵容",
        "十勝陣容",
        "十勝陣容"
    );
    private static readonly TimeSpan TenWinBuildsCacheDuration = TimeSpan.FromHours(20);
    private static readonly HttpClient TenWinHttpClient = BppHttpClientFactory.Create(
        productVersion: BppPluginVersion.Current,
        userAgentSuffix: "TenWinBuildRepository",
        timeout: TimeSpan.FromSeconds(10)
    );
    private readonly object _syncRoot = new();
    private TenWinBuildCorpus? _corpus;
    private bool _attemptedLoad;
    private string? _cacheFilePath;
    private Func<DateTime> _utcNow = () => DateTime.UtcNow;
    private Func<string, Task<string>> _downloadJsonAsync = DownloadJsonAsync;
    private Func<string?> _loadEmbeddedJson = LoadEmbeddedTenWinJson;
    private Action<Func<Task>> _queueBackgroundRefresh = QueueBackgroundRefresh;
    private bool _backgroundRefreshInProgress;
    private Task? _warmUpTask;

    public IReadOnlyList<BuildRecommendation> FindRecommendations(
        string? hero,
        IReadOnlyCollection<Guid> selectedTemplateIds,
        BuildLiveState? liveState = null
    )
    {
        var corpus = EnsureCorpus();
        if (corpus == null)
            return Array.Empty<BuildRecommendation>();

        var matches = corpus.FindBuilds(
            hero,
            selectedTemplateIds ?? Array.Empty<Guid>(),
            liveState ?? BuildLiveState.Empty
        );
        if (matches.Count == 0)
            return Array.Empty<BuildRecommendation>();

        var label = ResolveFinalBuildLabel();
        var results = new List<BuildRecommendation>(matches.Count);
        foreach (var match in matches)
        {
            var board = ProjectBoard(match.Build);
            if (board.Cards.Count == 0)
                continue;

            results.Add(
                new BuildRecommendation
                {
                    ModeLabel = label,
                    MatchedCardCount = match.MatchedSelectedCount,
                    TenWinRunCount = match.Build.Stats.TenWinRunCount,
                    TenWinRateBps = match.Build.Stats.TenWinRateBps,
                    P75TenWinFinalDay = match.Build.Stats.P75TenWinFinalDay,
                    Score = match.Build.Stats.Score,
                    Board = board,
                }
            );
        }

        for (var i = 0; i < results.Count; i++)
        {
            results[i].ResultIndex = i;
            results[i].ResultCount = results.Count;
        }

        return results;
    }

    private static BppItemBoard ProjectBoard(TenWinBuild build)
    {
        var cards = build
            .Layout.Where(item => item.TemplateId != Guid.Empty)
            .OrderBy(item => item.Slot ?? int.MaxValue)
            .Select(ProjectCard)
            .ToArray();

        return BppItemBoardSlotPlanner.Plan(
            new BppItemBoard(
                BppItemBoardId.FinalBuild,
                BppItemBoardType.Reference,
                cards,
                $"tenwin-build:{build.BuildId}"
            )
        );
    }

    private static BppItemBoardCard ProjectCard(TenWinLayoutItem item)
    {
        var size = ResolveCardSize(item.TemplateId, item.Size);
        return new BppItemBoardCard
        {
            TemplateId = item.TemplateId,
            InstanceId = $"tenwin-{(item.Slot?.ToString() ?? "unsocketed")}-{item.TemplateId:N}",
            Order = item.Slot ?? 0,
            Tier = MapTier(item.Tier),
            Size = size,
            Span = BppItemBoardSpan.Resolve(size),
            SourceSocketId = item.Slot.HasValue
                ? (EContainerSocketId?)Math.Clamp(item.Slot.Value, 0, 9)
                : null,
            EnchantmentType = MapEnchant(item.EnchantName),
        };
    }

    private static ECardSize ResolveCardSize(Guid templateId, int? size)
    {
        return size switch
        {
            1 => ECardSize.Small,
            2 => ECardSize.Medium,
            3 => ECardSize.Large,
            // Out-of-range/absent payload size: fall back to the game's authoritative card size.
            _ => ResolveCardSizeFromStaticData(templateId),
        };
    }

    private static ECardSize ResolveCardSizeFromStaticData(Guid templateId)
    {
        try
        {
            var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
            var template = BppStaticDataAccess.GetCardTemplate(staticData, templateId);
            return template?.Size switch
            {
                ECardSize.Small => ECardSize.Small,
                ECardSize.Medium => ECardSize.Medium,
                ECardSize.Large => ECardSize.Large,
                _ => ECardSize.Small,
            };
        }
        catch
        {
            return ECardSize.Small;
        }
    }

    private static ETier MapTier(int? tier)
    {
        // Layout tier is the analyzer's mod value 1..5 (Bronze..Legendary); ETier is 0..4.
        var normalized = tier.GetValueOrDefault();
        if (normalized > 0)
            normalized--;

        normalized = Math.Clamp(normalized, (int)ETier.Bronze, (int)ETier.Legendary);
        return (ETier)normalized;
    }

    private static EEnchantmentType? MapEnchant(string? enchantName)
    {
        if (string.IsNullOrWhiteSpace(enchantName))
            return null;

        return Enum.TryParse<EEnchantmentType>(enchantName, true, out var type)
            ? type
            : (EEnchantmentType?)null;
    }

    private static string ResolveFinalBuildLabel() => L.Resolve(FinalBuildLabel);

    /// <summary>
    /// Snapshot of the currently loaded corpus's provenance (analyzer emission time, build/hero
    /// counts) for status surfaces; null while no corpus is loaded.
    /// </summary>
    public TenWinCorpusSummary? GetCorpusSummary()
    {
        var corpus = EnsureCorpus();
        return corpus == null
            ? (TenWinCorpusSummary?)null
            : new TenWinCorpusSummary(
                corpus.GeneratedAtUtc,
                corpus.BuildCount,
                corpus.HeroCount,
                corpus.HeroBuildCounts
            );
    }

    // ---- Corpus loading / cache / remote refresh --------------------------

    private TenWinBuildCorpus? EnsureCorpus()
    {
        EnsureLoaded();
        return _corpus;
    }

    /// <summary>
    /// Starts loading the ten-win build corpus on a background thread so the first panel open
    /// does not block the Unity main thread on file I/O and JSON parsing. Idempotent.
    /// Call from the panel's Awake() / Initialize() as early as possible.
    /// </summary>
    public void BeginCorpusLoad()
    {
        lock (_syncRoot)
        {
            if (_attemptedLoad || _warmUpTask != null)
                return;

            _warmUpTask = Task.Run(LoadCorpusInBackground);
        }
        BppLog.Debug("BuildRecommendationRepository", "Corpus warm-up task started.");
    }

    private void LoadCorpusInBackground()
    {
        var corpus = LoadCorpus(out var shouldRefreshInBackground);
        lock (_syncRoot)
        {
            if (!_attemptedLoad)
            {
                _corpus = corpus;
                _attemptedLoad = true;
            }
        }

        BppLog.Info(
            "BuildRecommendationRepository",
            $"Corpus warm-up complete: {(corpus != null ? $"{corpus.BuildCount} builds" : "no corpus")}."
        );

        if (shouldRefreshInBackground)
            TryQueueRefreshFromRemote("cache_stale_or_missing");
    }

    private void EnsureLoaded()
    {
        lock (_syncRoot)
        {
            // Warm-up task running or already finished — either way, don't block.
            if (_attemptedLoad || _warmUpTask != null)
                return;
        }

        // BeginCorpusLoad() was never called (unexpected path). Load synchronously as a
        // last-resort fallback — log a warning so it shows up during testing.
        BppLog.Warn(
            "BuildRecommendationRepository",
            "EnsureLoaded reached synchronous fallback; BeginCorpusLoad was not called."
        );

        var corpus = LoadCorpus(out var shouldRefreshInBackground);
        lock (_syncRoot)
        {
            if (!_attemptedLoad)
            {
                _corpus = corpus;
                _attemptedLoad = true;
            }
        }

        if (shouldRefreshInBackground)
            TryQueueRefreshFromRemote("cache_stale_or_missing");
    }

    private TenWinBuildCorpus? LoadCorpus(out bool shouldRefreshInBackground)
    {
        shouldRefreshInBackground = false;

        if (TryLoadCache(allowExpired: false, out var freshCorpus))
            return freshCorpus;

        if (TryLoadCache(allowExpired: true, out var staleCorpus))
        {
            shouldRefreshInBackground = true;
            BppLog.Info(
                "BuildRecommendationRepository",
                "Using expired ten-win builds cache; remote refresh was queued in the background."
            );
            return staleCorpus;
        }

        // Cold start with no cache: seed from the bundled corpus (same compact format, same parser)
        // so the panel is never empty offline, and still queue a remote refresh.
        shouldRefreshInBackground = true;
        var embeddedJson = _loadEmbeddedJson();
        return string.IsNullOrWhiteSpace(embeddedJson)
            ? null
            : DeserializeCorpus(embeddedJson!, "embedded");
    }

    private static string? LoadEmbeddedTenWinJson()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(name =>
                    name.EndsWith(TenWinBuildsCacheFileName, StringComparison.OrdinalIgnoreCase)
                );
            if (resourceName == null)
            {
                BppLog.Warn(
                    "BuildRecommendationRepository",
                    "Embedded ten-win builds seed resource was not found."
                );
                return null;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return null;

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "BuildRecommendationRepository",
                $"Failed to load embedded ten-win builds seed: {ex.Message}"
            );
            return null;
        }
    }

    internal async Task<(bool Succeeded, string? Error)> TryRefreshFinalBuildsFromRemoteAsync()
    {
        var (remoteCorpus, error) = await LoadRemoteAsync().ConfigureAwait(false);
        if (remoteCorpus == null)
            return (false, error);

        lock (_syncRoot)
        {
            _corpus = remoteCorpus;
            _attemptedLoad = true;
        }

        return (true, null);
    }

    private void TryQueueRefreshFromRemote(string reason)
    {
        if (!TryBeginBackgroundRefresh())
            return;

        try
        {
            _queueBackgroundRefresh(() => RefreshFromRemoteInBackgroundAsync(reason));
            BppLog.Info(
                "BuildRecommendationRepository",
                $"Queued background ten-win builds refresh reason={reason}."
            );
        }
        catch (Exception ex)
        {
            EndBackgroundRefresh();
            BppLog.Warn(
                "BuildRecommendationRepository",
                $"Failed to queue background ten-win builds refresh reason={reason}: {ex.Message}"
            );
        }
    }

    private bool TryBeginBackgroundRefresh()
    {
        lock (_syncRoot)
        {
            if (_backgroundRefreshInProgress)
                return false;

            _backgroundRefreshInProgress = true;
            return true;
        }
    }

    private void EndBackgroundRefresh()
    {
        lock (_syncRoot)
        {
            _backgroundRefreshInProgress = false;
        }
    }

    private async Task RefreshFromRemoteInBackgroundAsync(string reason)
    {
        try
        {
            var (remoteCorpus, error) = await LoadRemoteAsync().ConfigureAwait(false);
            if (remoteCorpus != null)
            {
                lock (_syncRoot)
                {
                    _corpus = remoteCorpus;
                    _attemptedLoad = true;
                }

                BppLog.Info(
                    "BuildRecommendationRepository",
                    $"Background ten-win builds refresh succeeded reason={reason}."
                );
                return;
            }

            BppLog.Warn(
                "BuildRecommendationRepository",
                $"Background ten-win builds refresh failed reason={reason} error={error ?? "unknown"}."
            );
        }
        finally
        {
            // Atomically clear the in-progress flag and, on a cold start with no usable corpus,
            // re-arm the one-shot load so a later query retries (and can re-queue) the fetch.
            // Doing both under one lock avoids a window where a racing query re-arms the load
            // while the refresh is still marked in-progress and so suppresses its own re-queue.
            lock (_syncRoot)
            {
                _backgroundRefreshInProgress = false;
                if (_corpus == null)
                    _attemptedLoad = false;
            }
        }
    }

    private bool TryLoadCache(bool allowExpired, out TenWinBuildCorpus? corpus)
    {
        corpus = null;

        try
        {
            var cacheFilePath = ResolveCacheFilePath();
            if (!File.Exists(cacheFilePath))
                return false;

            var lastWriteUtc = File.GetLastWriteTimeUtc(cacheFilePath);
            var expiresAtUtc = lastWriteUtc.Add(TenWinBuildsCacheDuration);
            if (!allowExpired && _utcNow() >= expiresAtUtc)
                return false;

            var json = File.ReadAllText(cacheFilePath);
            corpus = DeserializeCorpus(json, "cache");
            if (corpus == null)
                return false;

            BppLog.Info(
                "BuildRecommendationRepository",
                $"Loaded ten-win builds from cache path={cacheFilePath} "
                    + $"expired={_utcNow() >= expiresAtUtc} expiresAtUtc={expiresAtUtc:O}"
            );
            return true;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "BuildRecommendationRepository",
                $"Failed to read ten-win builds cache {ResolveCacheFilePath()}: {ex.Message}"
            );
            return false;
        }
    }

    private async Task<(TenWinBuildCorpus? Corpus, string? Error)> LoadRemoteAsync()
    {
        try
        {
            var json = await _downloadJsonAsync(TenWinBuildsRemoteUrl).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return (null, "empty_response");

            var corpus = DeserializeCorpus(json, "remote");
            if (corpus == null)
                return (null, "invalid_response");

            TryWriteCache(json);
            BppLog.Info(
                "BuildRecommendationRepository",
                $"Loaded ten-win builds from remote url={TenWinBuildsRemoteUrl}"
            );
            return (corpus, null);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "BuildRecommendationRepository",
                $"Failed to refresh ten-win builds from {TenWinBuildsRemoteUrl}: {ex.Message}"
            );
            return (null, ex.Message);
        }
    }

    private static TenWinBuildCorpus? DeserializeCorpus(string json, string source)
    {
        var corpus = TenWinBuildCorpus.Parse(json);
        if (corpus == null)
        {
            BppLog.Warn(
                "BuildRecommendationRepository",
                $"Ten-win builds JSON from {source} was missing or malformed."
            );
        }

        return corpus;
    }

    private void TryWriteCache(string json)
    {
        try
        {
            var cacheFilePath = ResolveCacheFilePath();
            var cacheDirectory = Path.GetDirectoryName(cacheFilePath);
            if (!string.IsNullOrWhiteSpace(cacheDirectory))
                Directory.CreateDirectory(cacheDirectory);

            File.WriteAllText(cacheFilePath, json);
            File.SetLastWriteTimeUtc(cacheFilePath, _utcNow());
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "BuildRecommendationRepository",
                $"Failed to write ten-win builds cache {ResolveCacheFilePath()}: {ex.Message}"
            );
        }
    }

    private string ResolveCacheFilePath()
    {
        return _cacheFilePath ?? BuildDefaultTenWinCacheFilePath(BepInEx.Paths.GameRootPath);
    }

    private static string BuildDefaultTenWinCacheFilePath(string gameRootPath)
    {
        return Path.Combine(gameRootPath, "BazaarPlusPlusV4", TenWinBuildsCacheFileName);
    }

    private static Task<string> DownloadJsonAsync(string url)
    {
        return TenWinHttpClient.GetStringAsync(url);
    }

    private static void QueueBackgroundRefresh(Func<Task> refresh)
    {
        _ = Task.Run(refresh);
    }

    // ---- Test hooks -------------------------------------------------------

    private void ConfigureTenWinRemoteForTests(
        string cacheFilePath,
        Func<DateTime> utcNow,
        Func<string, Task<string>> downloadJsonAsync
    )
    {
        lock (_syncRoot)
        {
            _corpus = null;
            _attemptedLoad = false;
            _backgroundRefreshInProgress = false;
            _warmUpTask = null;
            _cacheFilePath = cacheFilePath;
            _utcNow = utcNow;
            _downloadJsonAsync = downloadJsonAsync;
            _loadEmbeddedJson = () => null;
            _queueBackgroundRefresh = QueueBackgroundRefresh;
        }
    }

    private void ConfigureTenWinRemoteForTests(
        string cacheFilePath,
        Func<DateTime> utcNow,
        Func<string, Task<string>> downloadJsonAsync,
        Action<Func<Task>> queueBackgroundRefresh
    )
    {
        lock (_syncRoot)
        {
            _corpus = null;
            _attemptedLoad = false;
            _backgroundRefreshInProgress = false;
            _warmUpTask = null;
            _cacheFilePath = cacheFilePath;
            _utcNow = utcNow;
            _downloadJsonAsync = downloadJsonAsync;
            _loadEmbeddedJson = () => null;
            _queueBackgroundRefresh = queueBackgroundRefresh ?? QueueBackgroundRefresh;
        }
    }

    private static string? ReadEmbeddedSeedForTests() => LoadEmbeddedTenWinJson();

    private void SetEmbeddedJsonForTests(Func<string?> loadEmbeddedJson)
    {
        lock (_syncRoot)
        {
            _corpus = null;
            _attemptedLoad = false;
            _loadEmbeddedJson = loadEmbeddedJson ?? (() => null);
        }
    }

    private void ResetTenWinRemoteForTests()
    {
        lock (_syncRoot)
        {
            _corpus = null;
            _attemptedLoad = false;
            _backgroundRefreshInProgress = false;
            _warmUpTask = null;
            _cacheFilePath = null;
            _utcNow = () => DateTime.UtcNow;
            _downloadJsonAsync = DownloadJsonAsync;
            _loadEmbeddedJson = LoadEmbeddedTenWinJson;
            _queueBackgroundRefresh = QueueBackgroundRefresh;
        }
    }
}
