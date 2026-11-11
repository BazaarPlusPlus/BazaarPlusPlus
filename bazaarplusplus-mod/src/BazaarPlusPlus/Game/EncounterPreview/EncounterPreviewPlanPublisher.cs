#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.GameInterop.DayTiers;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.EncounterPreview;

internal enum EncounterPreviewPlanPublisherStatus
{
    Loading,
    Ready,
    Degraded,
    Unavailable,
    Disposed,
}

/// <summary>
/// The desktop's encounter plans: observes each new static-data source, loads its plans from the
/// disk cache or compiles them, and publishes them for Core's <see cref="EncounterPreviewModule"/>
/// through <see cref="Reads"/>.
/// </summary>
internal sealed class EncounterPreviewPlanPublisher : IDisposable
{
    private readonly EncounterPreviewPlanRegistry _registry;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly IEncounterPreviewGameRuntime _runtime;
    private readonly EncounterPreviewCacheStore _cacheStore;
    private readonly EncounterPreviewPlanLoader _loader;
    private readonly string _gameBuild;
    private readonly string _buildChannel;
    private object? _observedSource;
    private int _healthDegraded;
    private int _status = (int)EncounterPreviewPlanPublisherStatus.Loading;
    private int _disposed;

    internal EncounterPreviewPlanPublisher(
        IBppServices services,
        BppStaticCardMapProvider cardMapProvider,
        GameDataDayTierResolver dayTierResolver,
        string cachePath
    )
        : this(
            new EncounterPreviewGameRuntime(cardMapProvider, dayTierResolver),
            new EncounterPreviewPlanRegistry(),
            new EncounterPreviewCacheStore(cachePath),
            services?.GameBuild.RawVersion ?? throw new ArgumentNullException(nameof(services)),
            services.GameBuild.Channel.ToString()
        ) { }

    internal EncounterPreviewPlanPublisher(
        IEncounterPreviewGameRuntime runtime,
        EncounterPreviewPlanRegistry registry,
        EncounterPreviewCacheStore cacheStore,
        string gameBuild,
        string buildChannel
    )
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        var compiler = new EncounterPreviewPlanCompiler();
        _loader = new EncounterPreviewPlanLoader(_cacheStore, compiler.Compile);
        _gameBuild = gameBuild ?? string.Empty;
        _buildChannel = buildChannel ?? string.Empty;
        _cacheStore.CleanupOrphanedTempFiles();
        Reads = new DesktopEncounterPreviewReads(this, _runtime);
    }

    internal EncounterPreviewPlanPublisherStatus Status =>
        (EncounterPreviewPlanPublisherStatus)Volatile.Read(ref _status);

    /// <summary>The published plans and the game state a query reads beside them.</summary>
    internal IEncounterPreviewReads Reads { get; }

    internal void ObserveStaticData()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        var source = _runtime.TryGetReadyStaticData();
        if (source == null || ReferenceEquals(source, _observedSource))
            return;

        _observedSource = source;
        var generation = _registry.BeginGeneration(source);
        Volatile.Write(ref _status, (int)EncounterPreviewPlanPublisherStatus.Loading);
        var sourceInfo = _runtime.TryCaptureSourceInfo(source);
        if (sourceInfo == null)
        {
            _registry.TryRunIfCurrent(
                source,
                generation,
                () =>
                    ReportTerminal(
                        new BppLogEvent(
                            BppLogFeatureScope.EncounterPreview,
                            "event_preview.plans.load_failed"
                        ),
                        EncounterPreviewPlanSource.Unknown,
                        EncounterPreviewPlanReasonCode.SourceInfoUnavailable,
                        snapshot: null,
                        sizeBytes: 0,
                        loadDurationMs: 0,
                        compileDurationMs: 0,
                        writeDurationMs: 0,
                        exception: null
                    )
            );
            return;
        }

        var token = _shutdown.Token;
        _ = Task.Run(() => LoadOrBuildAsync(source, sourceInfo, generation, token), token);
    }

    private async Task LoadOrBuildAsync(
        object source,
        BppGameDataSourceInfo sourceInfo,
        long generation,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var identity = EncounterPreviewIdentityResolver.Resolve(
                sourceInfo.ManifestPath,
                sourceInfo.DatabasePath,
                sourceInfo.DataBaseUrl,
                _gameBuild,
                _buildChannel
            );
            cancellationToken.ThrowIfCancellationRequested();
            if (!_registry.IsCurrent(source, generation))
                return;

            var loadResult = await _loader
                .LoadAsync(
                    identity,
                    () => _runtime.LoadCardMapAsync(source),
                    () => _runtime.SnapshotLevelUps(source),
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (!_registry.IsCurrent(source, generation))
                return;

            if (loadResult.WasCacheHit)
            {
                if (_registry.TryPublish(source, generation, loadResult.Snapshot))
                {
                    _registry.TryRunIfCurrent(
                        source,
                        generation,
                        () =>
                            ReportPublishedTerminal(
                                EncounterPreviewPlanSource.Cache,
                                loadResult.Snapshot,
                                persistError: null,
                                TryGetCacheBytes(),
                                loadResult.CacheReadMilliseconds,
                                compileDurationMs: 0,
                                writeDurationMs: 0
                            )
                    );
                }
                return;
            }

            var compileResult = loadResult.CompileResult!;

            Exception? persistError = null;
            var persisted = false;
            var writeMs = 0d;
            var published = _registry.TryCommitAndPublish(
                source,
                generation,
                compileResult.Snapshot,
                () =>
                {
                    var writeStarted = Stopwatch.GetTimestamp();
                    try
                    {
                        _cacheStore.Save(identity, compileResult.Snapshot);
                        persisted = true;
                    }
                    catch (Exception ex)
                    {
                        persistError = ex;
                    }
                    writeMs = ElapsedMilliseconds(writeStarted);
                }
            );
            if (!published)
                return;

            _registry.TryRunIfCurrent(
                source,
                generation,
                () =>
                    ReportPublishedTerminal(
                        EncounterPreviewPlanSource.Rebuild,
                        compileResult.Snapshot,
                        persistError,
                        persisted ? TryGetCacheBytes() : 0,
                        loadResult.CacheReadMilliseconds,
                        loadResult.CompileMilliseconds,
                        writeMs
                    )
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Plugin teardown or a superseded generation; no publication is allowed afterwards.
        }
        catch (Exception ex)
        {
            _registry.TryRunIfCurrent(
                source,
                generation,
                () =>
                    ReportTerminal(
                        new BppLogEvent(
                            BppLogFeatureScope.EncounterPreview,
                            "event_preview.plans.load_failed"
                        ),
                        EncounterPreviewPlanSource.Unknown,
                        EncounterPreviewPlanReasonCode.LoadException,
                        snapshot: null,
                        sizeBytes: 0,
                        loadDurationMs: 0,
                        compileDurationMs: 0,
                        writeDurationMs: 0,
                        ex
                    )
            );
        }
    }

    // The source the plans were published for travels with them: a query must read that
    // source's day tiers and live values, never a newer manager's.
    internal EncounterPreviewSnapshot? TryGetSnapshot(
        out object source,
        out EncounterPreviewAvailability unavailable
    )
    {
        source = null!;
        if (Volatile.Read(ref _disposed) != 0)
        {
            unavailable = EncounterPreviewAvailability.Unavailable;
            return null;
        }

        var current = _runtime.TryGetReadyStaticData();
        if (current != null && _registry.TryGet(current, out var snapshot))
        {
            source = current;
            unavailable = EncounterPreviewAvailability.Available;
            return snapshot;
        }

        unavailable =
            Status == EncounterPreviewPlanPublisherStatus.Unavailable
                ? EncounterPreviewAvailability.Unavailable
                : EncounterPreviewAvailability.Loading;
        return null;
    }

    private void ReportPublishedTerminal(
        EncounterPreviewPlanSource source,
        EncounterPreviewSnapshot snapshot,
        Exception? persistError,
        long sizeBytes,
        double loadDurationMs,
        double compileDurationMs,
        double writeDurationMs
    )
    {
        var coverage = snapshot.Coverage;
        var hasPartialCoverage = EncounterPreviewPlanHealth.HasDegradedCoverage(coverage);
        if (persistError != null || hasPartialCoverage)
        {
            Interlocked.Exchange(ref _healthDegraded, 1);
            Volatile.Write(ref _status, (int)EncounterPreviewPlanPublisherStatus.Degraded);
            ReportTerminal(
                new BppLogEvent(
                    BppLogFeatureScope.EncounterPreview,
                    "event_preview.plans.degraded",
                    storm: ["source", "reason_code"]
                ),
                source,
                persistError != null
                    ? EncounterPreviewPlanReasonCode.CacheWriteException
                    : EncounterPreviewPlanReasonCode.PartialCoverage,
                snapshot,
                sizeBytes,
                loadDurationMs,
                compileDurationMs,
                writeDurationMs,
                persistError
            );
            return;
        }

        var recovered = Interlocked.Exchange(ref _healthDegraded, 0) != 0;
        Volatile.Write(ref _status, (int)EncounterPreviewPlanPublisherStatus.Ready);
        if (recovered)
            BppLog.RecoverStorm(
                new BppLogEvent(
                    BppLogFeatureScope.EncounterPreview,
                    "event_preview.plans.degraded",
                    storm: ["source", "reason_code"]
                )
            );
        ReportTerminal(
            recovered
                ? new BppLogEvent(
                    BppLogFeatureScope.EncounterPreview,
                    "event_preview.plans.recovered"
                )
                : new BppLogEvent(BppLogFeatureScope.EncounterPreview, "event_preview.plans.ready"),
            source,
            EncounterPreviewPlanReasonCode.None,
            snapshot,
            sizeBytes,
            loadDurationMs,
            compileDurationMs,
            writeDurationMs,
            exception: null
        );
    }

    internal static class EncounterPreviewPlanHealth
    {
        internal static bool HasDegradedCoverage(EncounterPreviewCoverage coverage) =>
            coverage.EventFailureCount > 0 || coverage.LevelUpFailureCount > 0;
    }

    private void ReportTerminal(
        BppLogEvent logEvent,
        EncounterPreviewPlanSource source,
        EncounterPreviewPlanReasonCode reasonCode,
        EncounterPreviewSnapshot? snapshot,
        long sizeBytes,
        double loadDurationMs,
        double compileDurationMs,
        double writeDurationMs,
        Exception? exception
    )
    {
        if (
            logEvent.Id == "event_preview.plans.load_failed"
            || logEvent.Id == "event_preview.plans.degraded"
        )
        {
            Interlocked.Exchange(ref _healthDegraded, 1);
            Volatile.Write(
                ref _status,
                (int)(
                    logEvent.Id == "event_preview.plans.degraded"
                        ? EncounterPreviewPlanPublisherStatus.Degraded
                        : EncounterPreviewPlanPublisherStatus.Unavailable
                )
            );
        }

        var coverage = snapshot?.Coverage;
        var fields = new BppLogField[]
        {
            ("source", source),
            ("reason_code", reasonCode),
            ("event_count", snapshot?.EventCount ?? 0),
            ("level_up_count", snapshot?.LevelUpCount ?? 0),
            ("template_count", snapshot?.TemplateCount ?? 0),
            ("event_failure_count", coverage?.EventFailureCount ?? 0),
            ("level_up_failure_count", coverage?.LevelUpFailureCount ?? 0),
            ("unsupported_level_up_part_count", coverage?.UnsupportedLevelUpPartCount ?? 0),
            ("missing_template_count", coverage?.MissingReferencedTemplateCount ?? 0),
            ("size_bytes", Math.Max(0, sizeBytes)),
            ("load_duration_ms", ToMilliseconds(loadDurationMs)),
            ("compile_duration_ms", ToMilliseconds(compileDurationMs)),
            ("write_duration_ms", ToMilliseconds(writeDurationMs)),
            ("cache_path", _cacheStore.CachePath),
        };
        if (logEvent.Id == "event_preview.plans.load_failed")
        {
            if (exception == null)
                BppLog.ErrorEvent(logEvent, fields);
            else
                BppLog.ErrorEvent(logEvent, exception, fields);
            return;
        }
        if (logEvent.Id == "event_preview.plans.degraded")
        {
            if (exception == null)
                BppLog.WarnEvent(logEvent, fields);
            else
                BppLog.WarnEvent(logEvent, exception, fields);
            return;
        }

        BppLog.InfoEvent(logEvent, fields);
    }

    private static int ToMilliseconds(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? 0 : Math.Max(0, (int)Math.Round(value));

    private long TryGetCacheBytes()
    {
        try
        {
            return File.Exists(_cacheStore.CachePath)
                ? new FileInfo(_cacheStore.CachePath).Length
                : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static double ElapsedMilliseconds(long startedAt) =>
        (Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _shutdown.Cancel();
        _registry.Reset();
        _shutdown.Dispose();
        Interlocked.Exchange(ref _healthDegraded, 0);
        Volatile.Write(ref _status, (int)EncounterPreviewPlanPublisherStatus.Disposed);
    }
}
