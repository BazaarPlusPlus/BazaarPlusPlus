using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.Game.VoiceSubtitles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using BepInEx.Logging;
using Xunit;

namespace VoiceSubtitles.Tests;

public sealed class VoiceSubtitlesTests
{
    [Fact]
    [Trait("TestKind", "EmbeddedSeed")]
    public void Embedded_seed_loads_a_valid_nonempty_catalog()
    {
        var lines = LoadEmbeddedLines();

        Assert.NotEmpty(lines);
        var first = lines[0];
        Assert.False(string.IsNullOrWhiteSpace(first.Stem));
        Assert.False(string.IsNullOrWhiteSpace(first.English));
        Assert.False(string.IsNullOrWhiteSpace(first.Chinese));
        Assert.True(first.DurationSeconds > 0);
    }

    [Fact]
    public void Embedded_seed_computed_content_hash_uses_sha256_format()
    {
        var documentType = GetRequiredType("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLinesDocument");
        var computeContentHash = GetRequiredStaticMethod(documentType, "ComputeContentHash");
        var lines = LoadEmbeddedLines();

        var contentHash = Assert.IsType<string>(computeContentHash.Invoke(null, [lines]));

        Assert.StartsWith("sha256:", contentHash, StringComparison.Ordinal);
        Assert.Equal(71, contentHash.Length);
    }

    [Fact]
    public void Parser_rejects_count_mismatch()
    {
        var documentType = GetRequiredType("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLinesDocument");
        var parse = GetRequiredStaticMethod(documentType, "Parse");
        var json = BuildVoiceLinesJson(
            count: 2,
            contentHash: ContentHashFor(("stem", "English", "中文", 1.25)),
            ("stem", "English", "中文", 1.25)
        );

        var ex = Assert.Throws<TargetInvocationException>(() =>
            parse.Invoke(null, [json, VoiceCatalogSource.Embedded])
        );
        Assert.Contains("count", ex.InnerException?.Message);
    }

    [Fact]
    public void Parser_rejects_content_hash_mismatch()
    {
        var documentType = GetRequiredType("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLinesDocument");
        var parse = GetRequiredStaticMethod(documentType, "Parse");
        var json = BuildVoiceLinesJson(
            count: 1,
            contentHash: "sha256:0000000000000000000000000000000000000000000000000000000000000000",
            ("stem", "English", "中文", 1.25)
        );

        var ex = Assert.Throws<TargetInvocationException>(() =>
            parse.Invoke(null, [json, VoiceCatalogSource.Embedded])
        );
        Assert.Contains("contentHash", ex.InnerException?.Message);
    }

    [Fact]
    public void Default_cache_path_uses_game_root_data_directory()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"bpp-data-root-{Guid.NewGuid():N}");

        var cachePath = VoiceLinesCatalogFactory.BuildCacheFilePath(dataRoot);

        Assert.Equal(Path.Combine(dataRoot, "voice-lines.json"), cachePath);
    }

    [Fact]
    public void Runtime_settings_default_chinese_font_scale_matches_english()
    {
        var settingsType = GetRequiredType(
            "BazaarPlusPlus.Game.VoiceSubtitles.Settings.VoiceLineSettings"
        );

        Assert.Equal(1.0f, GetStaticSingle(settingsType, "DefaultEnglishFontScale"), precision: 2);
        Assert.Equal(1.0f, GetStaticSingle(settingsType, "DefaultChineseFontScale"), precision: 2);
    }

    [Fact]
    public void Fresh_cache_loads_without_remote_refresh()
    {
        var observer = new VoiceLinesCatalogObserver();
        var lines = LoadEmbeddedLines();

        observer.OnInitialLoad(
            CatalogInitialLoadResult<VoiceLine[]>.Published(Snapshot(lines, CatalogSource.Cache))
        );

        Assert.Equal(
            lines[0].Stem,
            VoiceLineCatalog.ResolveDetailed(lines[0].Stem, "Hero", "Test").Line.Stem
        );
        VoiceLineCatalog.Reset();
    }

    [Fact]
    public void Fresh_catalog_warm_up_emits_one_structured_ready_event()
    {
        using var capture = new LogCapture();
        var observer = new VoiceLinesCatalogObserver();
        observer.OnInitialLoad(
            CatalogInitialLoadResult<VoiceLine[]>.Published(
                Snapshot(LoadEmbeddedLines(), CatalogSource.Cache)
            )
        );

        var ready = Assert.Single(capture.Events("voice_subtitles.catalog.ready"));
        Assert.Equal(LogLevel.Info, ready.Level);
        Assert.Contains("source=cache", ready.Data?.ToString());
        Assert.Contains("line_count=", ready.Data?.ToString());
        Assert.Empty(capture.Events("voice_subtitles.catalog.degraded"));
        Assert.Empty(capture.Events("voice_subtitles.catalog.failed"));
        VoiceLineCatalog.Reset();
    }

    [Fact]
    public void Catalog_event_catalog_matches_the_locked_manifest()
    {
        var actual = typeof(VoiceCatalogLogEvents)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == typeof(BppLogEventDefinition))
            .Select(field => (BppLogEventDefinition)field.GetValue(null)!)
            .ToDictionary(
                definition => definition.EventId,
                definition =>
                    string.Join(
                        "|",
                        definition.Fields.Select(field =>
                            $"{field.Name}:{field.Cardinality}:{field.Correlation}"
                        )
                    ),
                StringComparer.Ordinal
            );

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["voice_subtitles.catalog.started"] = "",
                ["voice_subtitles.catalog.ready"] = "source:Low:None|line_count:High:None",
                ["voice_subtitles.catalog.degraded"] =
                    "reason_code:Low:None|source:Low:None|endpoint:Low:None",
                ["voice_subtitles.catalog.failed"] = "reason_code:Low:None|source:Low:None",
                ["voice_subtitles.catalog_refresh.started"] =
                    "reason_code:Low:None|endpoint:Low:None",
                ["voice_subtitles.catalog.recovered"] =
                    "reason_code:Low:None|source:Low:None|line_count:High:None",
                ["voice_subtitles.catalog_cache.degraded"] = "reason_code:Low:None",
                ["voice_subtitles.catalog_row.skipped"] =
                    "source:Low:None|row_number:High:None|reason_code:Low:None|stem:High:None",
            },
            actual
        );
        Assert.Equal(
            ["reason_code", "source"],
            VoiceCatalogLogEvents.CatalogDegraded.StormPolicy!.KeyFields.Select(field => field.Name)
        );
        Assert.Equal(
            ["reason_code"],
            VoiceCatalogLogEvents.CatalogCacheDegraded.StormPolicy!.KeyFields.Select(field =>
                field.Name
            )
        );
    }

    [Fact]
    public void Stale_catalog_emits_one_degradation_then_one_remote_recovery()
    {
        using var capture = new LogCapture();
        var observer = new VoiceLinesCatalogObserver();
        var lines = LoadEmbeddedLines();
        observer.OnInitialLoad(
            CatalogInitialLoadResult<VoiceLine[]>.Published(
                Snapshot(
                    lines,
                    CatalogSource.Cache,
                    isStale: true,
                    new CatalogIssue(CatalogIssueKind.CacheStale)
                )
            )
        );

        var degraded = Assert.Single(capture.Events("voice_subtitles.catalog.degraded"));
        Assert.Equal(LogLevel.Warning, degraded.Level);
        Assert.Contains("reason_code=cache_stale", degraded.Data?.ToString());
        Assert.Contains("source=cache", degraded.Data?.ToString());
        Assert.Contains("endpoint=voice_catalog", degraded.Data?.ToString());

        observer.OnRefreshCompleted(
            CatalogRefreshTrigger.Background,
            CatalogRefreshResult<VoiceLine[]>.Published(
                Snapshot(lines, CatalogSource.Remote),
                degraded: false
            )
        );

        var recovered = Assert.Single(capture.Events("voice_subtitles.catalog.recovered"));
        Assert.Equal(LogLevel.Info, recovered.Level);
        Assert.Contains("reason_code=cache_stale", recovered.Data?.ToString());
        Assert.Contains("source=cache", recovered.Data?.ToString());
        Assert.Contains("line_count=", recovered.Data?.ToString());
        VoiceLineCatalog.Reset();
    }

    [Fact]
    public void No_usable_catalog_emits_one_terminal_failed_event()
    {
        using var capture = new LogCapture();
        var observer = new VoiceLinesCatalogObserver();

        observer.OnInitialLoad(
            CatalogInitialLoadResult<VoiceLine[]>.Unavailable(
                new CatalogIssue(CatalogIssueKind.EmbeddedMissing)
            )
        );

        var failed = Assert.Single(capture.Events("voice_subtitles.catalog.failed"));
        Assert.Equal(LogLevel.Error, failed.Level);
        Assert.Contains("reason_code=no_usable_catalog", failed.Data?.ToString());
        Assert.Contains("source=embedded", failed.Data?.ToString());
        Assert.Empty(capture.Events("voice_subtitles.catalog.ready"));
        Assert.Empty(capture.Events("voice_subtitles.catalog.degraded"));
    }

    [Fact]
    public async Task Cache_write_failure_is_independent_from_catalog_health()
    {
        await Task.Yield();
        using var capture = new LogCapture();
        var observer = new VoiceLinesCatalogObserver();
        var lines = LoadEmbeddedLines();
        observer.OnInitialLoad(
            CatalogInitialLoadResult<VoiceLine[]>.Published(Snapshot(lines, CatalogSource.Embedded))
        );
        observer.OnRefreshCompleted(
            CatalogRefreshTrigger.Background,
            CatalogRefreshResult<VoiceLine[]>.Published(
                Snapshot(
                    lines,
                    CatalogSource.Remote,
                    issue: new CatalogIssue(
                        CatalogIssueKind.CacheWriteFailed,
                        new IOException("write failed")
                    )
                ),
                degraded: true
            )
        );

        var cacheDegraded = Assert.Single(capture.Events("voice_subtitles.catalog_cache.degraded"));
        Assert.Equal(LogLevel.Warning, cacheDegraded.Level);
        Assert.Contains("reason_code=write_failed", cacheDegraded.Data?.ToString());
        Assert.Empty(capture.Events("voice_subtitles.catalog.degraded"));
        Assert.Empty(capture.Events("voice_subtitles.catalog.recovered"));
        Assert.Single(capture.Events("voice_subtitles.catalog.ready"));
        VoiceLineCatalog.Reset();
    }

    [Fact]
    public void Malformed_rows_emit_typed_debug_skips_without_operational_warnings()
    {
        var documentType = typeof(VoiceLinesDocument);
        var parse = GetRequiredStaticMethod(documentType, "Parse");
        var json = BuildVoiceLinesJson(
            count: 1,
            contentHash: ContentHashFor(("valid", "English", "中文", 1.25)),
            ("", "missing", "缺失", 1),
            ("valid", "English", "中文", 1.25),
            ("valid", "duplicate", "重复", 1),
            ("empty", "", "", 1)
        );
        using var capture = new LogCapture();

        var lines = Assert.IsAssignableFrom<Array>(
            parse.Invoke(null, [json, VoiceCatalogSource.Remote])
        );

        Assert.Single(lines);
#if DEBUG
        var skipped = capture.Events("voice_subtitles.catalog_row.skipped");
        Assert.Equal(3, skipped.Count);
        Assert.All(skipped, entry => Assert.Equal(LogLevel.Debug, entry.Level));
        Assert.Contains(
            skipped,
            entry => entry.Data?.ToString()?.Contains("reason_code=missing_stem") == true
        );
        Assert.Contains(
            skipped,
            entry => entry.Data?.ToString()?.Contains("reason_code=duplicate_stem") == true
        );
        Assert.Contains(
            skipped,
            entry => entry.Data?.ToString()?.Contains("reason_code=empty_text") == true
        );
#else
        Assert.Empty(capture.Events("voice_subtitles.catalog_row.skipped"));
#endif
        Assert.DoesNotContain(
            capture.All,
            entry => entry.Level is LogLevel.Warning or LogLevel.Error
        );
    }

    [Fact]
    public void Stale_cache_loads_and_requests_background_refresh()
    {
        var observer = new VoiceLinesCatalogObserver();
        var lines = LoadEmbeddedLines();
        using var capture = new LogCapture();

        observer.OnRefreshQueued(new CatalogIssue(CatalogIssueKind.CacheStale));

        Assert.Single(capture.Events("voice_subtitles.catalog_refresh.started"));
        VoiceLineCatalog.Reset();
    }

    [Fact]
    public void Catalog_resolves_a_stem_from_embedded_seed()
    {
        var lines = LoadEmbeddedLines();
        try
        {
            VoiceLineCatalog.ReplaceCatalog(lines, "embedded-test");
            var first = lines[0];
            var resolution = VoiceLineCatalog.ResolveDetailed(
                "event:/VO/Test/" + first.Stem,
                "Hero",
                "Tutorial"
            );

            Assert.Equal(first.Stem, resolution.Line.Stem);
            Assert.Equal("event-stem", resolution.Strategy);
            Assert.Equal("embedded-test", resolution.CatalogName);
        }
        finally
        {
            VoiceLineCatalog.Reset();
        }
    }

    [Fact]
    public void Catalog_resolves_file_extension_suffix_like_bare_stem()
    {
        var catalogType = GetRequiredType("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLineCatalog");
        var resolveDetailed = GetRequiredStaticMethod(catalogType, "ResolveDetailed");

        try
        {
            ReplaceCatalog(
                catalogType,
                "suffix-test",
                ("001_VanessaPvPDefeat1", "Defeat does not defeat me.", "失败不会打败我。", 2.15f)
            );

            var bare = Resolve(catalogType, "001_VanessaPvPDefeat1", "Hero", "Tutorial");
            var withExtension = Resolve(
                catalogType,
                "event:/VO/Vanessa/001-VanessaPvPDefeat1.wav",
                "Hero",
                "Tutorial"
            );

            Assert.Equal(
                GetString(GetPropertyValue(bare, "Line")!, "Stem"),
                GetString(GetPropertyValue(withExtension, "Line")!, "Stem")
            );
            Assert.Equal(
                "001_VanessaPvPDefeat1",
                GetString(GetPropertyValue(withExtension, "Line")!, "Stem")
            );
            Assert.Equal("event-stem", GetString(withExtension, "Strategy"));
        }
        finally
        {
            ResetCatalog(catalogType);
        }
    }

    [Fact]
    public void Catalog_resolves_unique_character_hook_fallback()
    {
        var catalogType = GetRequiredType("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLineCatalog");

        try
        {
            ReplaceCatalog(
                catalogType,
                "hook-test",
                ("001_VanessaPvPDefeat1", "Defeat does not defeat me.", "失败不会打败我。", 2.15f),
                ("002_VanessaIdle1", "Still sailing.", "继续航行。", 1.5f)
            );

            var resolution = Resolve(
                catalogType,
                "event:/VO/Vanessa/UnmappedCategory",
                "Hero",
                "OnPvPVictoryDefeat"
            );
            var line = GetPropertyValue(resolution, "Line");

            Assert.Equal("001_VanessaPvPDefeat1", GetString(line!, "Stem"));
            Assert.Equal("character-hook-unique", GetString(resolution, "Strategy"));
            Assert.Equal("Vanessa:PvPDefeat", GetString(resolution, "MatchedToken"));
            Assert.Equal(1, GetInt32(resolution, "CandidateCount"));
        }
        finally
        {
            ResetCatalog(catalogType);
        }
    }

    [Fact]
    public void Catalog_reports_ambiguous_character_hook_fallback()
    {
        var catalogType = GetRequiredType("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLineCatalog");

        try
        {
            ReplaceCatalog(
                catalogType,
                "hook-ambiguous-test",
                ("001_VanessaPvPDefeat1", "Defeat does not defeat me.", "失败不会打败我。", 2.15f),
                ("002_VanessaPvPDefeat2", "The sea remembers.", "海会记住。", 1.75f)
            );

            var resolution = Resolve(
                catalogType,
                "event:/VO/Vanessa/UnmappedCategory",
                "Hero",
                "OnPvPVictoryDefeat"
            );
            var line = GetPropertyValue(resolution, "Line");

            Assert.True(string.IsNullOrEmpty(GetNullableString(line!, "Stem")));
            Assert.Equal("character-hook-ambiguous", GetString(resolution, "Strategy"));
            Assert.Equal("Vanessa:PvPDefeat", GetString(resolution, "MatchedToken"));
            Assert.Equal(2, GetInt32(resolution, "CandidateCount"));
        }
        finally
        {
            ResetCatalog(catalogType);
        }
    }

    [Fact]
    public void Catalog_reset_swaps_to_empty_snapshot()
    {
        var catalogType = GetRequiredType("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLineCatalog");

        try
        {
            ReplaceCatalog(
                catalogType,
                "reset-test",
                ("999_CustomResetLine1", "Reset line.", "重置台词。", 1.25f)
            );
            ResetCatalog(catalogType);

            var resolution = Resolve(
                catalogType,
                "event:/VO/Vanessa/999_CustomResetLine1",
                "Hero",
                "Tutorial"
            );
            var line = GetPropertyValue(resolution, "Line");

            Assert.True(string.IsNullOrEmpty(GetNullableString(line!, "Stem")));
            Assert.Equal("unresolved", GetString(resolution, "Strategy"));
        }
        finally
        {
            ResetCatalog(catalogType);
        }
    }

    [Fact]
    public async Task Module_stop_prevents_late_remote_catalog_publish()
    {
        var remote = new BlockingRemoteSource();
        var catalog = new RemoteEmbeddedCatalog<VoiceLine[]>(
            new VoiceLinesCatalogParser(),
            new MissingEmbeddedSource(),
            new EmptyCache(),
            remote,
            new FixedClock(),
            new RecordingScheduler(),
            new VoiceLinesCatalogObserver(),
            TimeSpan.FromHours(20)
        );
        var module = new VoiceSubtitlesModule(catalog);
        var refresh = catalog.RefreshAsync().AsTask();
        await remote.Started;

        module.Stop();
        remote.Complete(
            BuildVoiceLinesJson(
                1,
                ContentHashFor(("999_LatePublishOnly", "Late.", "迟到。", 1.0)),
                ("999_LatePublishOnly", "Late.", "迟到。", 1.0)
            )
        );

        Assert.False((await refresh).Succeeded);
        Assert.True(
            string.IsNullOrEmpty(
                VoiceLineCatalog.ResolveDetailed("999_LatePublishOnly", "Hero", "Test").Line.Stem
            )
        );
    }

    private static VoiceLine[] LoadEmbeddedLines()
    {
        var json = LoadEmbeddedJson();
        Assert.False(string.IsNullOrWhiteSpace(json));
        return VoiceLinesDocument.Parse(json!, VoiceCatalogSource.Embedded);
    }

    private static string? LoadEmbeddedJson() =>
        new AssemblyResourceCatalogSource(
            typeof(VoiceLine).Assembly,
            VoiceLinesCatalogFactory.EmbeddedResourceName
        )
            .ReadAsync(CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();

    private static CatalogSnapshot<VoiceLine[]> Snapshot(
        VoiceLine[] lines,
        CatalogSource source,
        bool isStale = false,
        CatalogIssue? issue = null
    ) => new(lines, source, new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc), isStale, issue);

    private sealed class BlockingRemoteSource : IRemoteCatalogSource
    {
        private readonly TaskCompletionSource<string?> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<bool> _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal Task Started => _started.Task;

        public ValueTask<string?> DownloadAsync(CancellationToken cancellationToken)
        {
            _started.TrySetResult(true);
            return new(_completion.Task);
        }

        internal void Complete(string? document) => _completion.SetResult(document);
    }

    private sealed class MissingEmbeddedSource : IEmbeddedCatalogSource
    {
        public ValueTask<string?> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);
    }

    private sealed class EmptyCache : ILocalCatalogCache
    {
        public ValueTask<CatalogCacheDocument?> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<CatalogCacheDocument?>(null);

        public ValueTask WriteAsync(string document, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class FixedClock : ICatalogClock
    {
        public DateTime UtcNow => new(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class RecordingScheduler : ICatalogRefreshScheduler
    {
        public void Queue(Func<Task> refresh) { }
    }

    private static string BuildVoiceLinesJson(
        int count,
        string contentHash,
        params (string Stem, string English, string Chinese, double DurationSeconds)[] lines
    )
    {
        var lineJson = string.Join(
            ",",
            lines.Select(line =>
                "{\"stem\":\""
                + line.Stem
                + "\",\"english\":\""
                + line.English
                + "\",\"chinese\":\""
                + line.Chinese
                + "\",\"durationSeconds\":"
                + line.DurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "}"
            )
        );
        return "{\"schemaVersion\":1,\"contentHash\":\""
            + contentHash
            + "\",\"generatedAt\":\"2026-07-03T00:00:00Z\",\"count\":"
            + count
            + ",\"lines\":["
            + lineJson
            + "]}";
    }

    private static string ContentHashFor(
        params (string Stem, string English, string Chinese, double DurationSeconds)[] lines
    )
    {
        var records = lines.Select(line =>
            string.Join(
                "\x1f",
                line.Stem,
                line.English,
                line.Chinese,
                ((long)Math.Round(line.DurationSeconds * 100.0)).ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                )
            )
        );
        var canonical = string.Join("\x1e", records);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void ReplaceCatalog(
        Type catalogType,
        string catalogName,
        params (string Stem, string English, string Chinese, float DurationSeconds)[] lines
    )
    {
        var voiceLineType = GetRequiredType("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLine");
        var voiceLineArray = Array.CreateInstance(voiceLineType, lines.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            var line =
                Activator.CreateInstance(
                    voiceLineType,
                    lines[i].Stem,
                    lines[i].English,
                    lines[i].Chinese,
                    lines[i].DurationSeconds
                ) ?? throw new InvalidOperationException("Could not create VoiceLine.");
            voiceLineArray.SetValue(line, i);
        }

        GetRequiredStaticMethod(catalogType, "ReplaceCatalog")
            .Invoke(null, new object[] { voiceLineArray, catalogName });
    }

    private static object Resolve(
        Type catalogType,
        string lookupText,
        string sourceLabel,
        string hookName
    )
    {
        var resolution = GetRequiredStaticMethod(catalogType, "ResolveDetailed")
            .Invoke(null, new object[] { lookupText, sourceLabel, hookName });
        return resolution ?? throw new InvalidOperationException("ResolveDetailed returned null.");
    }

    private static void ResetCatalog(Type catalogType)
    {
        GetRequiredStaticMethod(catalogType, "Reset").Invoke(null, null);
    }

    private static Type GetRequiredType(string name)
    {
        return Assembly.Load("BazaarPlusPlus").GetType(name)
            ?? throw new InvalidOperationException($"Missing type {name}");
    }

    private static MethodInfo GetRequiredStaticMethod(Type type, string name)
    {
        return type.GetMethod(
                name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException($"Missing method {type.FullName}.{name}");
    }

    private static MethodInfo GetRequiredInstanceMethod(Type type, string name)
    {
        return type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException($"Missing method {type.FullName}.{name}");
    }

    private static FieldInfo GetRequiredStaticField(Type type, string name)
    {
        return type.GetField(
                name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException($"Missing field {type.FullName}.{name}");
    }

    private static object? GetPropertyValue(object instance, string name)
    {
        var type = instance.GetType();
        if (type.GetProperty(name) is { } property)
            return property.GetValue(instance);
        if (type.GetField(name) is { } field)
            return field.GetValue(instance);

        throw new InvalidOperationException($"Missing property or field {type.FullName}.{name}");
    }

    private static string GetString(object instance, string name)
    {
        return Assert.IsType<string>(GetPropertyValue(instance, name));
    }

    private static string? GetNullableString(object instance, string name)
    {
        return GetPropertyValue(instance, name) as string;
    }

    private static bool GetBool(object instance, string name)
    {
        return Assert.IsType<bool>(GetPropertyValue(instance, name));
    }

    private static int GetInt32(object instance, string name)
    {
        return Assert.IsType<int>(GetPropertyValue(instance, name));
    }

    private static float GetSingle(object instance, string name)
    {
        return Assert.IsType<float>(GetPropertyValue(instance, name));
    }

    private static float GetStaticSingle(Type type, string name)
    {
        return Assert.IsType<float>(GetRequiredStaticField(type, name).GetValue(null));
    }

    private sealed class LogCapture : IDisposable
    {
        private readonly ManualLogSource _source = new("VoiceSubtitles.Tests");
        private readonly List<LogEventArgs> _events = [];

        internal LogCapture()
        {
            _source.LogEvent += OnLogEvent;
            var bppLogType = GetRequiredType("BazaarPlusPlus.Infrastructure.BppLog");
            GetRequiredStaticMethod(bppLogType, "Install").Invoke(null, [_source]);
        }

        internal IReadOnlyList<LogEventArgs> Events(string eventId) =>
            _events
                .Where(entry =>
                    entry.Data?.ToString()?.Contains("event=" + eventId, StringComparison.Ordinal)
                    == true
                )
                .ToArray();

        internal IReadOnlyList<LogEventArgs> All => _events;

        public void Dispose()
        {
            BppLog.Flush();
            _source.LogEvent -= OnLogEvent;
            _source.Dispose();
        }

        private void OnLogEvent(object? sender, LogEventArgs args) => _events.Add(args);
    }
}
