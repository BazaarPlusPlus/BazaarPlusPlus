#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus.Game.Upload;

public static class UploadPumpBootstrap
{
    public static bool CanActivate(GameBuildChannel channel) => channel != GameBuildChannel.Ptr;

    public static BundleUploadFeed.Session? ActivateIfAllowed(
        IBppServices services,
        UploadPumpCadence cadence,
        string? platformKey,
        SqliteOpenOptions databaseOptions
    )
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (!CanActivate(services.GameBuild.Channel))
        {
            // Upload stores must also filter persisted build_channel values so PTR runs stay excluded
            // after a player returns to Online.
            BppLog.DebugEvent(
                new BppLogEvent(BppLogFeatureScope.Upload, "upload.feed.skipped"),
                () => [("reason_code", UploadLogReasonCode.PtrBuild)]
            );
            return null;
        }

        return BundleUploadFeed.Activate(services, cadence, platformKey, databaseOptions);
    }
}
