#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Http;
using UnityEngine;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbSnapshotUploadController : MonoBehaviour
{
    private static BazaarDbSnapshotUploadController? _current;

    private IBppServices? _services;
    private BazaarDbSnapshotUploadService? _uploadService;
    private HttpClient? _httpClient;
    private CancellationTokenSource? _shutdown;
    private StartupUploadAttemptGate? _startupGate;
    private IDisposable? _runLifecycleSubscription;
    private readonly StartupUploadAttemptRunner _startupRunner = new(
        "BazaarDbSnapshotUploadController",
        "Skipping BazaarDB screenshot upload because a live run is active.",
        "Starting BazaarDB screenshot upload attempt.",
        "BazaarDB screenshot upload failed"
    );

    private void Awake()
    {
        _current = this;
    }

    public void Initialize(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        InitializeCore();
    }

    private void InitializeCore()
    {
        try
        {
            var services = _services!;
            var databasePath = services.Paths.RunLogDatabasePath;
            var screenshotsDirectoryPath = services.Paths.ScreenshotsDirectoryPath;

            if (
                string.IsNullOrWhiteSpace(databasePath)
                || string.IsNullOrWhiteSpace(screenshotsDirectoryPath)
            )
            {
                BppLog.Warn(
                    "BazaarDbSnapshotUploadController",
                    "BazaarDB screenshot upload is enabled but database or screenshots paths are invalid."
                );
                return;
            }

            var routes = ModApiRoutes.TryCreate(ModApiUploadDefaults.ApiBaseUrl);
            if (routes == null)
                return;

            var store = new BazaarDbSnapshotUploadStore(databasePath, screenshotsDirectoryPath);
            _httpClient = BppHttpClientFactory.Create(
                productVersion: BppPluginVersion.Current,
                userAgentSuffix: "BazaarDbSnapshotUpload",
                timeout: TimeSpan.FromSeconds(
                    Math.Max(10, ModApiUploadDefaults.RequestTimeoutSeconds)
                )
            );
            _uploadService = new BazaarDbSnapshotUploadService(
                store,
                routes,
                _httpClient,
                BppClientCacheBridge.TryGetProfileAccountId
            );
            _shutdown = new CancellationTokenSource();

            var startupDelaySeconds = Math.Max(5, ModApiUploadDefaults.StartupDelaySeconds);
            var retryIntervalSeconds = Math.Max(1, ModApiUploadDefaults.IntervalSeconds);
            _startupGate = new StartupUploadAttemptGate(
                Time.unscaledTime + startupDelaySeconds,
                retryIntervalSeconds
            );

            _runLifecycleSubscription = services.EventBus.Subscribe<RunLifecycleChanged>(
                OnRunLifecycleChanged
            );

            BppLog.Info(
                "BazaarDbSnapshotUploadController",
                $"BazaarDB screenshot uploader armed. enabled={IsEnabled()}, startup_delay={startupDelaySeconds}s, retry_interval={retryIntervalSeconds}s."
            );
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "BazaarDbSnapshotUploadController",
                $"Failed to initialize BazaarDB screenshot upload service: {ex}"
            );
        }
    }

    private void Update()
    {
        if (
            _uploadService == null
            || _shutdown == null
            || _startupGate == null
            || _services == null
        )
            return;

        if (!IsEnabled())
            return;

        _startupRunner.Tick(
            _startupGate,
            Time.unscaledTime,
            _services!.RunContext.IsInGameRun,
            UploadPendingSnapshotsOffMainThreadAsync,
            _shutdown.Token
        );
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_current, this))
            _current = null;

        _runLifecycleSubscription?.Dispose();
        _runLifecycleSubscription = null;

        if (_shutdown != null)
        {
            _shutdown.Cancel();
            _shutdown.Dispose();
            _shutdown = null;
        }

        _httpClient?.Dispose();
        _httpClient = null;
        _uploadService = null;
    }

    private void OnRunLifecycleChanged(RunLifecycleChanged change)
    {
        if (change.IsInGameRun)
            return;

        _startupGate?.ArmImmediateAttempt(Time.unscaledTime);
    }

    private bool IsEnabled()
    {
        return _services?.Config?.BazaarDbUploadEnabled?.Value ?? false;
    }

    private Task UploadPendingSnapshotsOffMainThreadAsync(CancellationToken cancellationToken)
    {
        var uploadService =
            _uploadService
            ?? throw new InvalidOperationException(
                "BazaarDB screenshot upload service is not initialized."
            );
        return uploadService.UploadPendingInBackgroundAsync(cancellationToken);
    }

    public static void OnEnabledChanged(bool enabled)
    {
        if (!enabled)
            return;

        _current?._startupGate?.ArmImmediateAttempt(Time.unscaledTime);
        BppLog.Info(
            "BazaarDbSnapshotUploadController",
            "BazaarDB screenshot upload toggle armed an immediate attempt."
        );
    }
}
