#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunBundleUploadFeed : IUploadFeed
{
    private readonly IPvpBattleCatalog _battleCatalog;

    public RunBundleUploadFeed(IPvpBattleCatalog battleCatalog)
    {
        _battleCatalog = battleCatalog ?? throw new ArgumentNullException(nameof(battleCatalog));
    }

    public UploadFeedKind Kind => UploadFeedKind.RunBundle;

    public UploadFeedActivation? Activate(IBppServices services, UploadFeedLogState logState)
    {
        try
        {
            var databasePath = services.Paths.RunLogDatabasePath;
            var replayRootPath = services.Paths.CombatReplayDirectoryPath;

            var startupDelaySeconds = Math.Max(5, ModApiUploadDefaults.StartupDelaySeconds);
            var retryIntervalSeconds = Math.Max(1, ModApiUploadDefaults.IntervalSeconds);
            var requestTimeoutSeconds = Math.Max(10, ModApiUploadDefaults.RequestTimeoutSeconds);
            if (
                string.IsNullOrWhiteSpace(databasePath) || string.IsNullOrWhiteSpace(replayRootPath)
            )
            {
                logState.ReportDegraded(null, UploadLogReasonCode.InvalidLocalPaths, null);
                return null;
            }

            var routes = ModApiRoutes.TryCreate(ModApiUploadDefaults.ApiBaseUrl);
            if (routes == null)
                return null;

            var uploadStore = new RunBundleUploadStore(
                databasePath,
                replayRootPath,
                _battleCatalog
            );
            var uploadService = new RunBundleUploadService(
                uploadStore,
                routes,
                timeout: TimeSpan.FromSeconds(requestTimeoutSeconds)
            );
            BppLog.DebugEvent(
                UploadLogEvents.FeedArmed,
                () =>
                    [
                        UploadLogEvents.FeedArmedFeed.Bind(Kind),
                        UploadLogEvents.FeedArmedRequestTimeoutMs.Bind(
                            requestTimeoutSeconds * 1000L
                        ),
                        UploadLogEvents.FeedArmedStartupDelayMs.Bind(startupDelaySeconds * 1000L),
                        UploadLogEvents.FeedArmedRetryIntervalMs.Bind(retryIntervalSeconds * 1000L),
                    ]
            );

            return new UploadFeedActivation
            {
                UploadInBackgroundAsync = uploadService.UploadPendingRunBundlesInBackgroundAsync,
                Disposable = uploadService,
                ExtraArmHook = new UploadArmHook(
                    (hookServices, arm) =>
                        hookServices.EventBus.Subscribe<CombatReplayPersistenceDrained>(_ =>
                        {
                            if (hookServices.RunContext.IsInGameRun)
                                return;

                            arm();
                        })
                ),
            };
        }
        catch (Exception ex)
        {
            logState.ReportDegraded(null, UploadLogReasonCode.InitializationException, ex);
            return null;
        }
    }
}
