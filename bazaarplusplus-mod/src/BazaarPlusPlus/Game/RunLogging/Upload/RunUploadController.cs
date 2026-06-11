#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;
using UnityEngine;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunUploadController : MonoBehaviour
{
    private IBppServices? _services;
    private IPvpBattleCatalog? _battleCatalog;
    private RunBundleUploadService? _uploadService;
    private CancellationTokenSource? _shutdown;
    private StartupUploadAttemptGate? _startupGate;
    private IDisposable? _runLifecycleSubscription;
    private IDisposable? _replayPersistenceDrainedSubscription;
    private readonly StartupUploadAttemptRunner _startupRunner = new(
        "RunUploadController",
        "Skipping startup run upload because a live run is active.",
        "Starting startup run upload attempt.",
        "Startup upload failed"
    );

    private void Awake() { }

    public void Initialize(IBppServices services, IPvpBattleCatalog battleCatalog)
    {
        _services = services;
        _battleCatalog = battleCatalog;
        InitializeCore();
    }

    private void InitializeCore()
    {
        try
        {
            var services = _services!;
            var databasePath = services.Paths.RunLogDatabasePath;
            var replayRootPath = services.Paths.CombatReplayDirectoryPath;

            var startupDelaySeconds = Math.Max(5, ModApiUploadDefaults.StartupDelaySeconds);
            var retryIntervalSeconds = Math.Max(1, ModApiUploadDefaults.IntervalSeconds);
            var requestTimeoutSeconds = Math.Max(10, ModApiUploadDefaults.RequestTimeoutSeconds);
            if (
                string.IsNullOrWhiteSpace(databasePath) || string.IsNullOrWhiteSpace(replayRootPath)
            )
            {
                BppLog.Warn(
                    "RunUploadController",
                    "Run bundle upload is enabled but local replay or database paths are invalid."
                );
                return;
            }

            var routes = ModApiRoutes.TryCreate(ModApiUploadDefaults.ApiBaseUrl);
            if (routes == null)
                return;

            var uploadStore = new RunBundleUploadStore(
                databasePath,
                replayRootPath,
                _battleCatalog!
            );
            _uploadService = new RunBundleUploadService(
                uploadStore,
                routes,
                timeout: TimeSpan.FromSeconds(requestTimeoutSeconds)
            );
            _shutdown = new CancellationTokenSource();
            _startupGate = new StartupUploadAttemptGate(
                Time.unscaledTime + startupDelaySeconds,
                retryIntervalSeconds
            );
            _runLifecycleSubscription = services.EventBus.Subscribe<RunLifecycleChanged>(
                OnRunLifecycleChanged
            );
            _replayPersistenceDrainedSubscription =
                services.EventBus.Subscribe<CombatReplayPersistenceDrained>(
                    OnCombatReplayPersistenceDrained
                );
            BppLog.Info(
                "RunUploadController",
                $"Startup run-bundle upload armed. timeout={requestTimeoutSeconds}s, startup_delay={startupDelaySeconds}s, retry_interval={retryIntervalSeconds}s."
            );
        }
        catch (Exception ex)
        {
            BppLog.Error("RunUploadController", $"Failed to initialize upload service: {ex}");
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

        _startupRunner.Tick(
            _startupGate,
            Time.unscaledTime,
            _services!.RunContext.IsInGameRun,
            UploadPendingRunBundlesOffMainThreadAsync,
            _shutdown.Token
        );
    }

    private void OnDestroy()
    {
        _replayPersistenceDrainedSubscription?.Dispose();
        _replayPersistenceDrainedSubscription = null;
        _runLifecycleSubscription?.Dispose();
        _runLifecycleSubscription = null;

        if (_shutdown != null)
        {
            _shutdown.Cancel();
            _shutdown.Dispose();
            _shutdown = null;
        }

        _uploadService?.Dispose();
        _uploadService = null;
    }

    private void OnRunLifecycleChanged(RunLifecycleChanged change)
    {
        if (change.IsInGameRun)
            return;

        _startupGate?.ArmImmediateAttempt(Time.unscaledTime);
    }

    private void OnCombatReplayPersistenceDrained(CombatReplayPersistenceDrained _)
    {
        if (_services!.RunContext.IsInGameRun)
            return;

        _startupGate?.ArmImmediateAttempt(Time.unscaledTime);
    }

    private Task UploadPendingRunBundlesOffMainThreadAsync(CancellationToken cancellationToken)
    {
        var uploadService =
            _uploadService
            ?? throw new InvalidOperationException("Run bundle upload service is not initialized.");
        return uploadService.UploadPendingRunBundlesInBackgroundAsync(cancellationToken);
    }
}
