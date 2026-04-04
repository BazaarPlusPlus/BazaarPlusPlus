#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.ModApi;
using BazaarPlusPlus.Game.Upload;
using UnityEngine;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunUploadController : MonoBehaviour
{
    private RunSummaryUploadService? _uploadService;
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

    private void Awake()
    {
        try
        {
            if (BppRuntimeHost.Config.EnableCommunityContributionConfig?.Value != true)
                return;

            var databasePath = BppRuntimeHost.Paths.RunLogDatabasePath;
            var identityPath = BppRuntimeHost.Paths.RunUploadInstallIdentityPath;
            var clientStatePath = BppRuntimeHost.Paths.RunUploadClientStatePath;
            var privateKeyPath = BppRuntimeHost.Paths.RunUploadPrivateKeyPath;

            var startupDelaySeconds = Math.Max(5, ModApiDefaults.StartupDelaySeconds);
            var retryIntervalSeconds = Math.Max(1, ModApiDefaults.IntervalSeconds);
            var batchSize = Math.Max(1, ModApiDefaults.BatchSize);
            var requestTimeoutSeconds = Math.Max(10, ModApiDefaults.RequestTimeoutSeconds);
            var context = ModApiBootstrapContext.TryCreate(
                databasePath,
                replayRootPath: null,
                identityPath,
                clientStatePath,
                privateKeyPath,
                ModApiDefaults.ApiBaseUrl
            );
            if (context == null)
            {
                BppLog.Warn(
                    "RunUploadController",
                    "Run upload is enabled but local auth/state paths or endpoints are invalid."
                );
                return;
            }

            var uploadStore = new RunUploadSqliteStore(context.DatabasePath);
            _uploadService = new RunSummaryUploadService(
                uploadStore,
                context.CreateIdentityStore(),
                context.CreateClientStateStore(),
                context.CreateKeyStore(),
                context.Routes,
                batchSize,
                timeout: TimeSpan.FromSeconds(requestTimeoutSeconds)
            );
            _shutdown = new CancellationTokenSource();
            _startupGate = new StartupUploadAttemptGate(
                Time.unscaledTime + startupDelaySeconds,
                retryIntervalSeconds
            );
            _runLifecycleSubscription = BppRuntimeHost.EventBus.Subscribe<RunLifecycleChanged>(
                OnRunLifecycleChanged
            );
            _replayPersistenceDrainedSubscription =
                BppRuntimeHost.EventBus.Subscribe<CombatReplayPersistenceDrained>(
                    OnCombatReplayPersistenceDrained
                );
            BppLog.Info(
                "RunUploadController",
                $"Startup run upload armed. timeout={requestTimeoutSeconds}s, batch_size={batchSize}, startup_delay={startupDelaySeconds}s, retry_interval={retryIntervalSeconds}s."
            );
        }
        catch (Exception ex)
        {
            BppLog.Error("RunUploadController", $"Failed to initialize upload service: {ex}");
        }
    }

    private void Update()
    {
        if (_uploadService == null || _shutdown == null || _startupGate == null)
            return;
        _startupRunner.Tick(
            _startupGate,
            Time.unscaledTime,
            BppRuntimeHost.RunContext.IsInGameRun,
            _uploadService.UploadPendingRunSummariesAsync,
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
        if (BppRuntimeHost.RunContext.IsInGameRun)
            return;

        _startupGate?.ArmImmediateAttempt(Time.unscaledTime);
    }
}
