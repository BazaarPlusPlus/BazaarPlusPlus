#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbSnapshotUploadFeed : IUploadFeed
{
    public UploadFeedKind Kind => UploadFeedKind.BazaarDbSnapshot;

    public UploadFeedActivation? Activate(IBppServices services, UploadFeedLogState _)
    {
        var screenshotLogState = new ScreenshotUploadLogState();
        try
        {
            var databasePath = services.Paths.RunLogDatabasePath;
            var screenshotsDirectoryPath = services.Paths.ScreenshotsDirectoryPath;

            if (
                string.IsNullOrWhiteSpace(databasePath)
                || string.IsNullOrWhiteSpace(screenshotsDirectoryPath)
            )
            {
                screenshotLogState.ReportInitializationDegraded(
                    ScreenshotUploadLogValues.InvalidLocalPaths
                );
                return null;
            }

            var routes = ModApiRoutes.TryCreate(ModApiUploadDefaults.ApiBaseUrl);
            if (routes == null)
            {
                screenshotLogState.ReportInitializationDegraded(
                    ScreenshotUploadLogValues.RouteUnavailable
                );
                return null;
            }

            var requestTimeoutSeconds = Math.Max(10, ModApiUploadDefaults.RequestTimeoutSeconds);
            var store = new BazaarDbSnapshotUploadStore(databasePath, screenshotsDirectoryPath);
            var httpClient = BppHttpClientFactory.Create(
                productVersion: BppPluginVersion.Current,
                userAgentSuffix: "BazaarDbSnapshotUpload",
                timeout: TimeSpan.FromSeconds(requestTimeoutSeconds)
            );
            var uploadService = new BazaarDbSnapshotUploadService(
                store,
                routes,
                httpClient,
                BppClientCacheBridge.TryGetProfileAccountId,
                screenshotLogState
            );

            return new UploadFeedActivation
            {
                UploadInBackgroundAsync = cancellationToken =>
                    RunAttemptAsync(
                        uploadService.UploadPendingInBackgroundAsync,
                        screenshotLogState,
                        cancellationToken
                    ),
                IsEnabled = () => IsEnabled(services),
                Disposable = httpClient,
            };
        }
        catch (Exception ex)
        {
            screenshotLogState.ReportInitializationDegraded(
                ScreenshotUploadLogValues.InitializationException,
                ex
            );
            return null;
        }
    }

    private static bool IsEnabled(IBppServices services) =>
        services.Config.BazaarDbUploadEnabled?.Value ?? false;

    internal static async Task<UploadAttemptResult> RunAttemptAsync(
        Func<CancellationToken, Task> uploadAsync,
        ScreenshotUploadLogState logState,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await uploadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            logState.ReportHealthDegraded(
                ScreenshotUploadLogValues.ServiceException,
                roundTripMilliseconds: null
            );
        }
        return UploadAttemptResult.NoHealthSignal();
    }
}
