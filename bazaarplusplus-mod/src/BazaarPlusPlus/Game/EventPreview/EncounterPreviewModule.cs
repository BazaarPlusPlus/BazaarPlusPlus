#nullable enable
using System.Diagnostics;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.GameInterop.DayTiers;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.EventPreview;

internal readonly record struct EventPreviewQuery(Guid TemplateId, string NativeText);

internal readonly record struct EncounterStepPreviewQuery(Guid TemplateId, string NativeText);

internal readonly record struct LevelUpPreviewQuery(int CurrentLevel);

internal enum EventPreviewAvailability
{
    Available,
    Loading,
    Missing,
    Unsupported,
    Unavailable,
}

internal readonly record struct EventPreviewResult(
    EventPreviewAvailability Availability,
    string? Content
);

internal enum EncounterPreviewModuleStatus
{
    Loading,
    Ready,
    Degraded,
    Unavailable,
    Disposed,
}

internal sealed class EncounterPreviewModule : IDisposable
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
    private int _status = (int)EncounterPreviewModuleStatus.Loading;
    private int _disposed;

    internal EncounterPreviewModule(
        IBppServices services,
        BppStaticCardMapProvider cardMapProvider,
        IGameDataDayTierResolver dayTierResolver,
        string cachePath
    )
        : this(
            new EncounterPreviewGameRuntime(cardMapProvider, dayTierResolver),
            new EncounterPreviewPlanRegistry(),
            new EncounterPreviewCacheStore(cachePath),
            services?.GameBuild.RawVersion ?? throw new ArgumentNullException(nameof(services)),
            services.GameBuild.Channel.ToString()
        ) { }

    internal EncounterPreviewModule(
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
    }

    internal EncounterPreviewModuleStatus Status =>
        (EncounterPreviewModuleStatus)Volatile.Read(ref _status);

    internal void ObserveStaticData()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        var source = _runtime.TryGetReadyStaticData();
        if (source == null || ReferenceEquals(source, _observedSource))
            return;

        _observedSource = source;
        var generation = _registry.BeginGeneration(source);
        Volatile.Write(ref _status, (int)EncounterPreviewModuleStatus.Loading);
        var sourceInfo = _runtime.TryCaptureSourceInfo(source);
        if (sourceInfo == null)
        {
            _registry.TryRunIfCurrent(
                source,
                generation,
                () =>
                    ReportTerminal(
                        new BppLogEvent(
                            BppLogFeatureScope.EventPreview,
                            "event_preview.plans.load_failed"
                        ),
                        EventPreviewPlanSource.Unknown,
                        EventPreviewPlanReasonCode.SourceInfoUnavailable,
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
                                EventPreviewPlanSource.Cache,
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
                        EventPreviewPlanSource.Rebuild,
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
                            BppLogFeatureScope.EventPreview,
                            "event_preview.plans.load_failed"
                        ),
                        EventPreviewPlanSource.Unknown,
                        EventPreviewPlanReasonCode.LoadException,
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

    public EventPreviewResult ResolveEvent(EventPreviewQuery query)
    {
        try
        {
            if (!TryGetSnapshot(out var source, out var snapshot, out var unavailable))
                return new EventPreviewResult(unavailable, null);
            if (query.TemplateId == Guid.Empty)
                return new EventPreviewResult(EventPreviewAvailability.Missing, null);

            var dayTiers = _runtime.ResolveDayTiers(source);
            var currentDay = dayTiers.Day;
            var template = _runtime.GetCardTemplate(source, query.TemplateId);
            if (template?.Tags?.Contains(ECardTag.Merchant) == true)
            {
                var policy = EncounterMerchantTierResolver.Resolve(template);
                if (!policy.FixedTier.HasValue && !policy.UsesDayDistribution)
                    return new EventPreviewResult(EventPreviewAvailability.Unsupported, null);

                var merchantContent = EncounterPreviewTextFormatter.BuildQualityLine(
                    policy.UsesDayDistribution ? dayTiers.Table : null,
                    policy.FixedTier
                );
                return Presentation(merchantContent);
            }

            if (!snapshot.TryGetEvent(query.TemplateId, out var eventPlan))
                return new EventPreviewResult(EventPreviewAvailability.Missing, null);

            var option = EncounterEventDetailResolver.TryResolve(
                eventPlan,
                snapshot,
                _runtime.ReadCurrentHero(),
                _runtime.ReadInventory(),
                currentDay,
                ResolveLiveValue
            );
            if (option == null || (!option.HasChoiceDetails && !option.HasOutcomeGroups))
                return new EventPreviewResult(EventPreviewAvailability.Unsupported, null);

            return Presentation(
                EncounterPreviewTextFormatter.Build(option, _runtime.ColorKeywords, dayTiers.Table)
            );

            bool ResolveLiveValue(
                Guid templateId,
                string effectId,
                bool isAura,
                out string valueText,
                out string? unit
            ) =>
                _runtime.TryEvaluateAbilityValue(
                    source,
                    templateId,
                    effectId,
                    isAura,
                    out valueText,
                    out unit
                );
        }
        catch (Exception)
        {
            return new EventPreviewResult(EventPreviewAvailability.Unavailable, null);
        }
    }

    public EventPreviewResult ResolveStep(EncounterStepPreviewQuery query)
    {
        try
        {
            if (!TryGetSnapshot(out var source, out var snapshot, out var unavailable))
                return new EventPreviewResult(unavailable, null);
            if (
                query.TemplateId == Guid.Empty
                || !snapshot.TryGetTemplate(query.TemplateId, out var stepPlan)
            )
                return new EventPreviewResult(EventPreviewAvailability.Missing, null);
            if (stepPlan.Kind != EncounterPreviewTemplateKind.EncounterStep)
                return new EventPreviewResult(EventPreviewAvailability.Unsupported, null);

            var dayTiers = _runtime.ResolveDayTiers(source);
            var content = EncounterPreviewTextFormatter.BuildRewardQualityLine(
                stepPlan.RewardFilter,
                query.NativeText,
                dayTiers.Table
            );
            return string.IsNullOrWhiteSpace(content)
                ? new EventPreviewResult(EventPreviewAvailability.Unsupported, null)
                : new EventPreviewResult(EventPreviewAvailability.Available, content);
        }
        catch (Exception)
        {
            return new EventPreviewResult(EventPreviewAvailability.Unavailable, null);
        }
    }

    public EventPreviewResult ResolveLevelUp(LevelUpPreviewQuery query)
    {
        try
        {
            if (!TryGetSnapshot(out var source, out var snapshot, out var unavailable))
                return new EventPreviewResult(unavailable, null);
            if (!snapshot.TryGetLevelUp(query.CurrentLevel, out var levelUpPlan))
                return new EventPreviewResult(EventPreviewAvailability.Missing, null);

            var content = LevelUpPreviewTextFormatter.Build(
                levelUpPlan,
                id => snapshot.TryGetTemplate(id, out var template) ? template : null,
                _runtime.ReadCurrentHero(),
                _runtime.ColorKeywords,
                query.CurrentLevel,
                ResolveLiveValue
            );
            return string.IsNullOrWhiteSpace(content)
                ? new EventPreviewResult(EventPreviewAvailability.Unsupported, null)
                : new EventPreviewResult(EventPreviewAvailability.Available, content);

            bool ResolveLiveValue(
                Guid templateId,
                string effectId,
                bool isAura,
                out string valueText,
                out string? unit
            ) =>
                _runtime.TryEvaluateAbilityValue(
                    source,
                    templateId,
                    effectId,
                    isAura,
                    out valueText,
                    out unit
                );
        }
        catch (Exception)
        {
            return new EventPreviewResult(EventPreviewAvailability.Unavailable, null);
        }
    }

    private bool TryGetSnapshot(
        out object source,
        out EncounterPreviewSnapshot snapshot,
        out EventPreviewAvailability unavailable
    )
    {
        source = null!;
        snapshot = null!;
        if (Volatile.Read(ref _disposed) != 0)
        {
            unavailable = EventPreviewAvailability.Unavailable;
            return false;
        }
        if (_runtime.IsInCombat)
        {
            unavailable = EventPreviewAvailability.Unsupported;
            return false;
        }

        source = _runtime.TryGetReadyStaticData()!;
        if (source != null && _registry.TryGet(source, out snapshot))
        {
            unavailable = EventPreviewAvailability.Available;
            return true;
        }

        unavailable =
            Status == EncounterPreviewModuleStatus.Unavailable
                ? EventPreviewAvailability.Unavailable
                : EventPreviewAvailability.Loading;
        return false;
    }

    private static EventPreviewResult Presentation(string? content) =>
        string.IsNullOrWhiteSpace(content)
            ? new EventPreviewResult(EventPreviewAvailability.Unsupported, null)
            : new EventPreviewResult(EventPreviewAvailability.Available, content);

    private void ReportPublishedTerminal(
        EventPreviewPlanSource source,
        EncounterPreviewSnapshot snapshot,
        Exception? persistError,
        long sizeBytes,
        double loadDurationMs,
        double compileDurationMs,
        double writeDurationMs
    )
    {
        var coverage = snapshot.Coverage;
        var hasPartialCoverage = EventPreviewPlanHealth.HasDegradedCoverage(coverage);
        if (persistError != null || hasPartialCoverage)
        {
            Interlocked.Exchange(ref _healthDegraded, 1);
            Volatile.Write(ref _status, (int)EncounterPreviewModuleStatus.Degraded);
            ReportTerminal(
                new BppLogEvent(
                    BppLogFeatureScope.EventPreview,
                    "event_preview.plans.degraded",
                    storm: ["source", "reason_code"]
                ),
                source,
                persistError != null
                    ? EventPreviewPlanReasonCode.CacheWriteException
                    : EventPreviewPlanReasonCode.PartialCoverage,
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
        Volatile.Write(ref _status, (int)EncounterPreviewModuleStatus.Ready);
        if (recovered)
            BppLog.RecoverStorm(
                new BppLogEvent(
                    BppLogFeatureScope.EventPreview,
                    "event_preview.plans.degraded",
                    storm: ["source", "reason_code"]
                )
            );
        ReportTerminal(
            recovered
                ? new BppLogEvent(BppLogFeatureScope.EventPreview, "event_preview.plans.recovered")
                : new BppLogEvent(BppLogFeatureScope.EventPreview, "event_preview.plans.ready"),
            source,
            EventPreviewPlanReasonCode.None,
            snapshot,
            sizeBytes,
            loadDurationMs,
            compileDurationMs,
            writeDurationMs,
            exception: null
        );
    }

    internal static class EventPreviewPlanHealth
    {
        internal static bool HasDegradedCoverage(EventPreviewCoverage coverage) =>
            coverage.EventFailureCount > 0 || coverage.LevelUpFailureCount > 0;
    }

    private void ReportTerminal(
        BppLogEvent logEvent,
        EventPreviewPlanSource source,
        EventPreviewPlanReasonCode reasonCode,
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
                        ? EncounterPreviewModuleStatus.Degraded
                        : EncounterPreviewModuleStatus.Unavailable
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
        Volatile.Write(ref _status, (int)EncounterPreviewModuleStatus.Disposed);
    }
}
